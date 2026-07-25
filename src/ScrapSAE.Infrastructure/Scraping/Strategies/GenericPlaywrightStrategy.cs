using Microsoft.Extensions.DependencyInjection;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Interfaces;

namespace ScrapSAE.Infrastructure.Scraping.Strategies;

/// <summary>
/// Estrategia genérica por defecto que utiliza el motor completo de Playwright y OpenAI.
/// Actúa como wrapper hacia el IScrapingService original para preservar la lógica existente.
/// </summary>
public class GenericPlaywrightStrategy : IProviderScraperStrategy
{
    private readonly IServiceProvider _serviceProvider;

    public GenericPlaywrightStrategy(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Task<IEnumerable<ScrapedProduct>> ScrapeAsync(
        SiteProfile site, 
        ScrapeExecutionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var scrapingService = _serviceProvider.GetRequiredService<IScrapingService>();
        return scrapingService.ScrapeAsync(site, context, cancellationToken);
    }

    public Task<List<ScrapedProduct>> ScrapeDirectUrlsAsync(
        List<string> urls,
        SiteProfile site,
        DirectUrlScrapeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var scrapingService = _serviceProvider.GetRequiredService<IScrapingService>();
        return scrapingService.ScrapeDirectUrlsAsync(urls, site.Id, options, cancellationToken);
    }
}
