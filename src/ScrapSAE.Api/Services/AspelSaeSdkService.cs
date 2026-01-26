using Microsoft.Extensions.Logging;
using ScrapSAE.Api.Models;
using ScrapSAE.Infrastructure.Sae;
using ScrapSAE.Core.Entities;

namespace ScrapSAE.Api.Services;

public sealed class AspelSaeSdkService : ISaeSdkService
{
    private readonly ILogger<AspelSaeSdkService> _logger;
    private readonly IConfiguration _configuration;
    private readonly SettingsStore _settingsStore;

    public AspelSaeSdkService(
        ILogger<AspelSaeSdkService> logger,
        IConfiguration configuration,
        SettingsStore settingsStore)
    {
        _logger = logger;
        _configuration = configuration;
        _settingsStore = settingsStore;
    }

    public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var settings = GetEffectiveSettings();
        if (string.IsNullOrWhiteSpace(settings.SaeSdkPath))
        {
            return Task.FromResult(false);
        }

        var dllPath = ResolveInterfaseDll(settings.SaeSdkPath);
        if (!File.Exists(dllPath))
        {
            _logger.LogWarning("Interfase SAE DLL not found at {Path}", dllPath);
            return Task.FromResult(false);
        }

        using var client = new SaeNativeClient(dllPath);
        var loaded = client.Load();
        return Task.FromResult(loaded);
    }

    public Task<bool> SendProductAsync(StagingProduct product, CancellationToken cancellationToken = default)
    {
        var settings = GetEffectiveSettings();
        if (string.IsNullOrWhiteSpace(settings.SaeSdkPath))
        {
            return Task.FromResult(false);
        }

        var dllPath = ResolveInterfaseDll(settings.SaeSdkPath);
        if (!File.Exists(dllPath))
        {
            _logger.LogWarning("Interfase SAE DLL not found at {Path}", dllPath);
            return Task.FromResult(false);
        }

        var commandTemplate = _configuration["SAE:CommandTemplate"];
        if (string.IsNullOrWhiteSpace(commandTemplate))
        {
            _logger.LogWarning("SAE:CommandTemplate not configured; cannot send product.");
            return Task.FromResult(false);
        }

        var command = commandTemplate
            .Replace("{sku}", product.SkuSae ?? product.SkuSource ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{name}", product.SkuSource ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        try
        {
            using var client = new SaeNativeClient(dllPath);
            if (!client.Load())
            {
                return Task.FromResult(false);
            }

            client.ExecuteCommand(command, out var response);
            _logger.LogInformation("SAE response: {Response}", response);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing SAE command.");
            return Task.FromResult(false);
        }
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

    private static string ResolveInterfaseDll(string sdkPath)
    {
        if (File.Exists(sdkPath))
        {
            return sdkPath;
        }

        return Path.Combine(sdkPath, "InterfaseSae70.dll");
    }
}
