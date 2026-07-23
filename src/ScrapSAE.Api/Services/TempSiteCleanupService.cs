using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScrapSAE.Core.Entities;
using ScrapSAE.Api.Services;

namespace ScrapSAE.Api.Services;

/// <summary>
/// Hosted service que elimina automáticamente cada 15 minutos los SiteProfile
/// cuyo nombre comienza con "[TEMP]" y tienen más de 60 minutos de antigüedad.
/// Estos sites temporales son creados por el wizard de proveedores durante el test de scraping.
/// </summary>
public sealed class TempSiteCleanupService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan TempSiteMaxAge = TimeSpan.FromMinutes(60);

    private readonly ILogger<TempSiteCleanupService> _logger;
    private readonly SupabaseTableService<SiteProfile> _siteService;

    public TempSiteCleanupService(
        ILogger<TempSiteCleanupService> logger,
        SupabaseTableService<SiteProfile> siteService)
    {
        _logger = logger;
        _siteService = siteService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[TempCleanup] Servicio de limpieza de sites temporales iniciado. Intervalo: {Interval}min", CheckInterval.TotalMinutes);

        // Initial delay to let the API finish starting up
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupTempSitesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "[TempCleanup] Error durante limpieza de sites temporales.");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task CleanupTempSitesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<SiteProfile> allSites;
        try
        {
            allSites = await _siteService.GetAllAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[TempCleanup] No se pudo obtener la lista de sites.");
            return;
        }

        var cutoffTime = DateTime.UtcNow - TempSiteMaxAge;
        var tempSitesToDelete = allSites
            .Where(s => s.Name.StartsWith("[TEMP]", StringComparison.OrdinalIgnoreCase)
                     && s.CreatedAt < cutoffTime)
            .ToList();

        if (tempSitesToDelete.Count == 0)
        {
            return;
        }

        _logger.LogInformation("[TempCleanup] Eliminando {Count} site(s) temporal(es) expirado(s).", tempSitesToDelete.Count);

        var deleted = 0;
        foreach (var site in tempSitesToDelete)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await _siteService.DeleteAsync(site.Id);
                deleted++;
                _logger.LogDebug("[TempCleanup] Site temporal eliminado: {SiteId} ({SiteName})", site.Id, site.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[TempCleanup] No se pudo eliminar site temporal {SiteId} ({SiteName}).", site.Id, site.Name);
            }
        }

        if (deleted > 0)
        {
            _logger.LogInformation("[TempCleanup] Limpieza completada: {Deleted} site(s) eliminado(s).", deleted);
        }
    }
}
