using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Interfaces;

namespace ScrapSAE.Api.Tests.Stubs;

public sealed class StubScrapingService : IScrapingService
{
    public void RegisterSite(SiteProfile site)
    {
        // No-op for tests.
    }

    public Task<IEnumerable<ScrapedProduct>> ScrapeAsync(SiteProfile site, CancellationToken cancellationToken = default)
    {
        var products = new[]
        {
            new ScrapedProduct { SkuSource = "SKU-001", RawHtml = "<html>one</html>" },
            new ScrapedProduct { SkuSource = "SKU-002", RawHtml = "<html>two</html>" }
        };
        return Task.FromResult<IEnumerable<ScrapedProduct>>(products);
    }

    public Task<byte[]?> DownloadImageAsync(string imageUrl)
    {
        return Task.FromResult<byte[]?>(null);
    }

    public Task<List<ScrapedProduct>> ScrapeDirectUrlsAsync(
        List<string> urls,
        Guid siteId,
        DirectUrlScrapeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var products = urls.Select((url, idx) => new ScrapedProduct
        {
            SkuSource = $"DIRECT-{idx + 1}",
            Title = $"Direct Product {idx + 1}",
            SourceUrl = url
        }).ToList();

        return Task.FromResult(products);
    }

    public Task<List<string>> DiscoverProductUrlsAsync(
        SiteProfile site,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new List<string>());
    }
}

