using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;

namespace ScrapSAE.Core.Interfaces;

/// <summary>
/// Estrategia de scraping específica por proveedor/plataforma.
/// </summary>
public interface IProviderScraperStrategy
{
    /// <summary>
    /// Ejecuta el scraping de un sitio proveedor de acuerdo a la estrategia concreta.
    /// </summary>
    Task<IEnumerable<ScrapedProduct>> ScrapeAsync(
        SiteProfile site, 
        ScrapeExecutionContext? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extrae datos de una lista de URLs específicas utilizando esta estrategia.
    /// </summary>
    Task<List<ScrapedProduct>> ScrapeDirectUrlsAsync(
        List<string> urls,
        SiteProfile site,
        DirectUrlScrapeOptions? options = null,
        CancellationToken cancellationToken = default);
}
