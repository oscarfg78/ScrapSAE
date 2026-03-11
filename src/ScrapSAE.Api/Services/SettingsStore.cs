using System.Text.Json;
using ScrapSAE.Api.Models;

namespace ScrapSAE.Api.Services;

public sealed class SettingsStore
{
    private readonly string _settingsPath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public SettingsStore(IWebHostEnvironment environment)
    {
        var overridePath = Environment.GetEnvironmentVariable("SCRAPSAE_RUNTIME_SETTINGS_PATH");
        _settingsPath = !string.IsNullOrWhiteSpace(overridePath)
            ? overridePath
            : Path.Combine(environment.ContentRootPath, "appsettings.runtime.json");
    }

    public AppSettingsDto? Get()
    {
        if (!File.Exists(_settingsPath))
        {
            return null;
        }

        var json = File.ReadAllText(_settingsPath);
        return JsonSerializer.Deserialize<AppSettingsDto>(json, _jsonOptions);
    }

    public async Task SaveAsync(AppSettingsDto settings, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        await File.WriteAllTextAsync(_settingsPath, json, cancellationToken);
    }
}
