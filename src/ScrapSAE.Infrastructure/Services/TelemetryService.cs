using System.Text.Json;
using Microsoft.Extensions.Logging;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Interfaces;

namespace ScrapSAE.Infrastructure.Services;

/// <summary>
/// Servicio de telemetría enriquecida que captura contexto completo de diagnóstico
/// </summary>
public class TelemetryService : ITelemetryService
{
    private readonly ILogger<TelemetryService> _logger;
    private readonly string _telemetryPath;
    private readonly JsonSerializerOptions _jsonOptions;

    public TelemetryService(ILogger<TelemetryService> logger)
    {
        _logger = logger;
        _telemetryPath = Path.Combine(Path.GetTempPath(), "scrapsae-telemetry");
        
        // Crear el directorio si no existe
        if (!Directory.Exists(_telemetryPath))
        {
            Directory.CreateDirectory(_telemetryPath);
        }

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task RecordFailureAsync(DiagnosticPackage package)
    {
        try
        {
            var fileName = $"failure_{package.ExecutionId}_{package.Id}.json";
            var filePath = Path.Combine(_telemetryPath, fileName);
            
            var json = JsonSerializer.Serialize(package, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json);
            
            _logger.LogWarning(
                "[Telemetry] Fallo registrado: {FailureType} en {Url} - Selector: {Selector} - Archivo: {File}",
                package.FailureType,
                package.Url,
                package.SelectorAttempted,
                fileName
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Telemetry] Error al guardar paquete de diagnóstico");
        }
    }

    public async Task RecordSuccessAsync(Guid executionId, string message, string url)
    {
        try
        {
            var fileName = $"success_{executionId}_{Guid.NewGuid()}.json";
            var filePath = Path.Combine(_telemetryPath, fileName);
            
            var successRecord = new
            {
                ExecutionId = executionId,
                Timestamp = DateTime.UtcNow,
                Message = message,
                Url = url
            };
            
            var json = JsonSerializer.Serialize(successRecord, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json);
            
            _logger.LogInformation("[Telemetry] Éxito registrado: {Message} en {Url}", message, url);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Telemetry] Error al guardar registro de éxito");
        }
    }

    public async Task<IEnumerable<DiagnosticPackage>> GetDiagnosticPackagesAsync(Guid executionId)
    {
        var packages = new List<DiagnosticPackage>();
        
        try
        {
            var pattern = $"failure_{executionId}_*.json";
            var files = Directory.GetFiles(_telemetryPath, pattern);
            
            foreach (var file in files)
            {
                var json = await File.ReadAllTextAsync(file);
                var package = JsonSerializer.Deserialize<DiagnosticPackage>(json, _jsonOptions);
                if (package != null)
                {
                    packages.Add(package);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Telemetry] Error al recuperar paquetes de diagnóstico");
        }
        
        return packages;
    }
}
