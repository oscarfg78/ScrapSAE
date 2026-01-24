using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Interfaces;

namespace ScrapSAE.Api.Tests.Stubs;

public sealed class StubScrapingService : IScrapingService
{
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
}
