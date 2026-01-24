using System.Net.Http.Json;
using System.Text.Json;
using ScrapSAE.Api.Models;

namespace ScrapSAE.Api.Services;

public sealed class DiagnosticsService
{
    private readonly IConfiguration _configuration;
    private readonly SettingsStore _settingsStore;
    private readonly ISaeSdkService _saeSdkService;

    public DiagnosticsService(IConfiguration configuration, SettingsStore settingsStore, ISaeSdkService saeSdkService)
    {
        _configuration = configuration;
        _settingsStore = settingsStore;
        _saeSdkService = saeSdkService;
    }

    public async Task<DiagnosticsResult> RunAsync(CancellationToken cancellationToken)
    {
        var result = new DiagnosticsResult
        {
            BackendOk = true,
            CheckedAtUtc = DateTime.UtcNow
        };

        var settings = GetEffectiveSettings();
        var (supabaseOk, supabaseMessage, supabaseCount) = await TestSupabaseAsync(settings, cancellationToken);
        result.SupabaseOk = supabaseOk;
        result.SupabaseMessage = supabaseMessage;
        result.SupabaseSampleCount = supabaseCount;

        var (saeOk, saeMessage) = await TestSaeSdkAsync(cancellationToken);
        result.SaeSdkOk = saeOk;
        result.SaeMessage = saeMessage;

        return result;
    }

    private AppSettingsDto GetEffectiveSettings()
    {
        var stored = _settingsStore.Get();
        return new AppSettingsDto
        {
            SupabaseUrl = stored?.SupabaseUrl ?? _configuration["Supabase:Url"],
            SupabaseServiceKey = stored?.SupabaseServiceKey ?? _configuration["Supabase:ServiceKey"],
            SaeSdkPath = stored?.SaeSdkPath ?? _configuration["SAE:SdkPath"],
            SaeUser = stored?.SaeUser ?? _configuration["SAE:User"],
            SaePassword = stored?.SaePassword ?? _configuration["SAE:Password"]
        };
    }

    private static async Task<(bool Ok, string Message, int? SampleCount)> TestSupabaseAsync(
        AppSettingsDto settings,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.SupabaseUrl) || string.IsNullOrWhiteSpace(settings.SupabaseServiceKey))
        {
            return (false, "Supabase no configurado.", null);
        }

        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("apikey", settings.SupabaseServiceKey);
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {settings.SupabaseServiceKey}");
            var url = settings.SupabaseUrl.TrimEnd('/');
            var response = await client.GetAsync($"{url}/rest/v1/config_sites?select=id&limit=1", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return (false, $"Supabase respondió {response.StatusCode}.", null);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var count = CountJsonArray(json);
            return (true, "Supabase accesible.", count);
        }
        catch (Exception ex)
        {
            return (false, $"Error Supabase: {ex.Message}", null);
        }
    }

    private async Task<(bool Ok, string Message)> TestSaeSdkAsync(CancellationToken cancellationToken)
    {
        try
        {
            var ok = await _saeSdkService.TestConnectionAsync(cancellationToken);
            return ok ? (true, "SDK SAE respondió correctamente.") : (false, "SDK SAE no está configurado.");
        }
        catch (Exception ex)
        {
            return (false, $"Error SAE SDK: {ex.Message}");
        }
    }

    private static int CountJsonArray(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.GetArrayLength()
            : 0;
    }
}
