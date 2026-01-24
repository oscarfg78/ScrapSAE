using System.Net.Http.Json;
using System.Text.Json;

namespace ScrapSAE.Api.Services;

public sealed class SupabaseRestClient : ISupabaseRestClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly JsonSerializerOptions _jsonOptions;

    public SupabaseRestClient(IConfiguration configuration)
    {
        var url = configuration["Supabase:Url"]
            ?? throw new ArgumentNullException("Supabase:Url not configured");
        var key = configuration["Supabase:ServiceKey"]
            ?? throw new ArgumentNullException("Supabase:ServiceKey not configured");

        _baseUrl = url.TrimEnd('/');
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("apikey", key);
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {key}");
        _httpClient.DefaultRequestHeaders.Add("Prefer", "return=representation");

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };
    }

    public JsonSerializerOptions JsonOptions => _jsonOptions;

    public async Task<T[]> GetAsync<T>(string pathAndQuery)
    {
        var response = await _httpClient.GetAsync($"{_baseUrl}/rest/v1/{pathAndQuery}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T[]>(_jsonOptions) ?? Array.Empty<T>();
    }

    public async Task<T?> PostAsync<T>(string path, T body) where T : class
    {
        var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/rest/v1/{path}", body, _jsonOptions);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<T[]>(_jsonOptions);
        return result?.FirstOrDefault();
    }

    public async Task<T?> PatchAsync<T>(string pathAndQuery, object update) where T : class
    {
        var response = await _httpClient.PatchAsJsonAsync($"{_baseUrl}/rest/v1/{pathAndQuery}", update, _jsonOptions);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<T[]>(_jsonOptions);
        return result?.FirstOrDefault();
    }

    public async Task DeleteAsync(string pathAndQuery)
    {
        var response = await _httpClient.DeleteAsync($"{_baseUrl}/rest/v1/{pathAndQuery}");
        response.EnsureSuccessStatusCode();
    }
}
