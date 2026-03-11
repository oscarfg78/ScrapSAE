using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace ScrapSAE.Api.Services;

public sealed class SupabaseRestClient : ISupabaseRestClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly SettingsStore _settingsStore;
    private readonly JsonSerializerOptions _jsonOptions;

    public SupabaseRestClient(IConfiguration configuration, SettingsStore settingsStore)
    {
        _configuration = configuration;
        _settingsStore = settingsStore;
        _httpClient = new HttpClient();

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };
    }

    public JsonSerializerOptions JsonOptions => _jsonOptions;

    public async Task<T[]> GetAsync<T>(string pathAndQuery)
    {
        using var request = CreateRequest(HttpMethod.Get, pathAndQuery);
        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T[]>(_jsonOptions) ?? Array.Empty<T>();
    }
    
    public async Task<string> GetAsync(string pathAndQuery)
    {
        using var request = CreateRequest(HttpMethod.Get, pathAndQuery);
        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public async Task<T?> PostAsync<T>(string path, T body) where T : class
    {
        using var content = JsonContent.Create(body, options: _jsonOptions);
        using var request = CreateRequest(HttpMethod.Post, path, content);
        using var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Error posting to {path}: {response.StatusCode} - {errorContent}");
        }

        var result = await response.Content.ReadFromJsonAsync<T[]>(_jsonOptions);
        return result?.FirstOrDefault();
    }

    public async Task<T?> PatchAsync<T>(string pathAndQuery, object update) where T : class
    {
        using var content = JsonContent.Create(update, options: _jsonOptions);
        using var request = CreateRequest(HttpMethod.Patch, pathAndQuery, content);
        using var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Error patching {pathAndQuery}: {response.StatusCode} - {errorContent}");
        }

        var result = await response.Content.ReadFromJsonAsync<T[]>(_jsonOptions);
        return result?.FirstOrDefault();
    }
    
    public async Task PatchAsync(string pathAndQuery, string jsonBody)
    {
        using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        using var request = CreateRequest(HttpMethod.Patch, pathAndQuery, content);
        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(string pathAndQuery)
    {
        using var request = CreateRequest(HttpMethod.Delete, pathAndQuery);
        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string pathAndQuery, HttpContent? content = null)
    {
        var (baseUrl, key) = ResolveRuntimeSettings();
        var request = new HttpRequestMessage(method, $"{baseUrl}/rest/v1/{pathAndQuery}");
        request.Headers.Add("apikey", key);
        request.Headers.Add("Authorization", $"Bearer {key}");
        request.Headers.Add("Prefer", "return=representation");
        if (content != null)
        {
            request.Content = content;
        }

        return request;
    }

    private (string BaseUrl, string ServiceKey) ResolveRuntimeSettings()
    {
        var stored = _settingsStore.Get();
        var url = FirstNonEmpty(
            stored?.SupabaseUrl,
            _configuration["Supabase:Url"],
            _configuration["supabaseUrl"]);
        var key = FirstNonEmpty(
            stored?.SupabaseServiceKey,
            _configuration["Supabase:ServiceKey"],
            _configuration["supabaseServiceKey"]);

        if (string.IsNullOrWhiteSpace(url) || IsPlaceholderUrl(url))
        {
            throw new SupabaseConfigurationException(
                "Supabase URL no configurada. Ve a Configuración y captura la URL real del proyecto (https://<project-ref>.supabase.co).");
        }

        if (string.IsNullOrWhiteSpace(key) || IsPlaceholderKey(key))
        {
            throw new SupabaseConfigurationException(
                "Supabase Service Key no configurada. Ve a Configuración y captura la service_role key real del proyecto.");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            throw new SupabaseConfigurationException("Supabase URL inválida. Debe iniciar con http:// o https://.");
        }

        var baseUrl = $"{uri.Scheme}://{uri.Host}";
        if (!uri.IsDefaultPort)
        {
            baseUrl += $":{uri.Port}";
        }

        return (baseUrl.TrimEnd('/'), key.Trim());
    }

    private static bool IsPlaceholderUrl(string url)
    {
        var normalized = url.Trim();
        if (normalized.Contains("YOUR_PROJECT", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return string.Equals(uri.Host, "example.supabase.co", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(uri.Host, "your_project.supabase.co", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPlaceholderKey(string key)
    {
        var normalized = key.Trim();
        return string.Equals(normalized, "YOUR_SERVICE_KEY", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "test-key", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "changeme", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}
