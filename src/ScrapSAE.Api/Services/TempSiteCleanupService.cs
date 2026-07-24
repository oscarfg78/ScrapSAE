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
        _logger.LogInformation("[TempCleanup] Servicio de limpieza de sites temporales y duplicados iniciado.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupTempSitesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "[TempCleanup] Error durante limpieza de sites.");
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

        // 1. Limpieza de sites temporales expirados
        var cutoffTime = DateTime.UtcNow - TempSiteMaxAge;
        var tempSitesToDelete = allSites
            .Where(s => s.Name.StartsWith("[TEMP]", StringComparison.OrdinalIgnoreCase)
                     && s.CreatedAt.ToUniversalTime() < cutoffTime)
            .ToList();

        foreach (var site in tempSitesToDelete)
        {
            if (cancellationToken.IsCancellationRequested) break;
            try
            {
                await _siteService.DeleteAsync(site.Id);
                _logger.LogInformation("[TempCleanup] Site temporal eliminado: {SiteId} ({SiteName})", site.Id, site.Name);
            }
            catch { }
        }

        // 2. Limpieza de proveedores duplicados por nombre
        var duplicateGroups = allSites
            .Where(s => !s.Name.StartsWith("[TEMP]", StringComparison.OrdinalIgnoreCase))
            .GroupBy(s => s.Name.Trim().ToLowerInvariant())
            .Where(g => g.Count() > 1);

        foreach (var group in duplicateGroups)
        {
            if (cancellationToken.IsCancellationRequested) break;

            // Mantener el registro más reciente
            var toKeep = group.OrderByDescending(s => s.CreatedAt).First();
            var duplicates = group.Where(s => s.Id != toKeep.Id).ToList();

            foreach (var dup in duplicates)
            {
                try
                {
                    await _siteService.DeleteAsync(dup.Id);
                    _logger.LogInformation("[TempCleanup] Registro duplicado de proveedor eliminado de Supabase: {Name} (ID: {Id})", dup.Name, dup.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[TempCleanup] No se pudo eliminar duplicado {Name}.", dup.Name);
                }
            }
        }
    }
}
