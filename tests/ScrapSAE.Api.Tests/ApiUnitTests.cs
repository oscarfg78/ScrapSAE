using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using ScrapSAE.Api.Services;
using ScrapSAE.Api.Tests.Fakes;
using ScrapSAE.Api.Tests.Stubs;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Interfaces;
using System.Text.Json;

namespace ScrapSAE.Api.Tests;

public class ApiUnitTests
{
    [Fact]
    public async Task SupabaseTableService_ShouldCreateAndReadEntity()
    {
        var client = new FakeSupabaseRestClient();
        var service = new SupabaseTableService<SiteProfile>(client, "config_sites");
        var site = new SiteProfile
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            BaseUrl = "https://test",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var created = await service.CreateAsync(site);
        var fetched = await service.GetByIdAsync(site.Id);

        created.Should().NotBeNull();
        fetched.Should().NotBeNull();
        fetched!.Name.Should().Be("Test");
    }

    [Fact]
    public async Task ScrapingRunner_ShouldInsertNewStagingProducts()
    {
        var client = new FakeSupabaseRestClient();
        var site = new SiteProfile
        {
            Id = Guid.NewGuid(),
            Name = "Site A",
            BaseUrl = "https://example.com",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        client.Seed("config_sites", site);

        var aiProcessor = new Mock<IAIProcessorService>();
        aiProcessor
            .Setup(x => x.ProcessProductAsync(It.IsAny<string>(), It.IsAny<Action<string,string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessedProduct());
        var syncLogService = new SupabaseTableService<SyncLog>(client, "sync_logs");
        var categoryMappingService = new SupabaseTableService<CategoryMapping>(client, "category_mapping");
        var scrapeControl = new Mock<IScrapeControlService>();
        scrapeControl.Setup(x => x.Start(It.IsAny<Guid>())).Returns(CancellationToken.None);
        var logger = new Mock<ILogger<ScrapingRunner>>();

        var runner = new ScrapingRunner(
            new Mock<IServiceProvider>().Object,
            new StubScrapingService(),
            client,
            aiProcessor.Object,
            syncLogService,
            categoryMappingService,
            scrapeControl.Object,
            logger.Object);
        var result = await runner.RunForSiteAsync(site.Id, null, CancellationToken.None);

        result.ProductsCreated.Should().Be(2);
    }

    [Fact]
    public async Task ScrapingRunner_ShouldSendRichPayloadToAi()
    {
        var client = new FakeSupabaseRestClient();
        var site = new SiteProfile
        {
            Id = Guid.NewGuid(),
            Name = "Site B",
            BaseUrl = "https://example.com",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        client.Seed("config_sites", site);

        var scrapingService = new Mock<IScrapingService>();
        scrapingService
            .Setup(x => x.ScrapeAsync(It.IsAny<SiteProfile>(), It.IsAny<ScrapeExecutionContext?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ScrapedProduct>
            {
                new()
                {
                    SkuSource = "SKU-RICH-1",
                    Title = "Producto",
                    SourceUrl = "https://example.com/p/1",
                    ImageUrl = "https://example.com/img/main.jpg",
                    ImageUrls = new List<string> { "https://example.com/img/main.jpg", "https://example.com/img/2.jpg" },
                    Attachments = new List<ProductAttachment>
                    {
                        new() { FileName = "Ficha", FileUrl = "https://example.com/files/ds.pdf", FileType = "pdf" }
                    },
                    NavigationUrls = new List<string> { "https://example.com/p/2" }
                }
            });
        scrapingService
            .Setup(x => x.DownloadImageAsync(It.IsAny<string>()))
            .ReturnsAsync((byte[]?)null);

        string? capturedRaw = null;
        var aiProcessor = new Mock<IAIProcessorService>();
        aiProcessor
            .Setup(x => x.ProcessProductAsync(It.IsAny<string>(), It.IsAny<Action<string,string>?>(), It.IsAny<CancellationToken>()))
            .Callback<string, Action<string,string>?, CancellationToken>((raw, _, _) => capturedRaw = raw)
            .ReturnsAsync(new ProcessedProduct());

        var syncLogService = new SupabaseTableService<SyncLog>(client, "sync_logs");
        var categoryMappingService = new SupabaseTableService<CategoryMapping>(client, "category_mapping");
        var scrapeControl = new Mock<IScrapeControlService>();
        scrapeControl.Setup(x => x.Start(It.IsAny<Guid>())).Returns(CancellationToken.None);
        var logger = new Mock<ILogger<ScrapingRunner>>();

        var runner = new ScrapingRunner(
            new Mock<IServiceProvider>().Object,
            scrapingService.Object,
            client,
            aiProcessor.Object,
            syncLogService,
            categoryMappingService,
            scrapeControl.Object,
            logger.Object);

        await runner.RunForSiteAsync(site.Id, null, CancellationToken.None);

        capturedRaw.Should().NotBeNull();
        capturedRaw.Should().Contain("ImageUrls");
        capturedRaw.Should().Contain("Attachments");
        capturedRaw.Should().Contain("SourceUrl");
        capturedRaw.Should().Contain("NavigationUrls");
    }

    [Fact]
    public async Task ScrapingRunner_ShouldBuildStructuredFallback_WhenAiFails()
    {
        var client = new FakeSupabaseRestClient();
        var site = new SiteProfile
        {
            Id = Guid.NewGuid(),
            Name = "Site Fallback",
            BaseUrl = "https://example.com",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        client.Seed("config_sites", site);

        var scrapingService = new Mock<IScrapingService>();
        scrapingService
            .Setup(x => x.ScrapeAsync(It.IsAny<SiteProfile>(), It.IsAny<ScrapeExecutionContext?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ScrapedProduct>
            {
                new()
                {
                    SkuSource = "SKU-FALLBACK-1",
                    Title = "Producto de prueba",
                    Description = "Descripcion prueba",
                    SourceUrl = "https://example.com/p/sku-fallback-1",
                    ImageUrl = "https://example.com/img/main.jpg",
                    ImageUrls = new List<string> { "https://example.com/img/main.jpg", "https://example.com/img/alt.jpg" },
                    Attachments = new List<ProductAttachment>
                    {
                        new() { FileName = "Ficha tecnica", FileUrl = "https://example.com/files/datasheet.pdf", FileType = "pdf" }
                    },
                    Attributes = new Dictionary<string, string>
                    {
                        ["tech_diametro_exterior"] = "6 mm",
                        ["tech_temperatura_ambiente"] = "-35 °C ... 60 °C",
                        ["currency"] = "MXN",
                        ["stock"] = "12"
                    }
                }
            });

        var aiProcessor = new Mock<IAIProcessorService>();
        aiProcessor
            .Setup(x => x.ProcessProductAsync(It.IsAny<string>(), It.IsAny<Action<string,string>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ai unavailable"));

        var syncLogService = new SupabaseTableService<SyncLog>(client, "sync_logs");
        var categoryMappingService = new SupabaseTableService<CategoryMapping>(client, "category_mapping");
        var scrapeControl = new Mock<IScrapeControlService>();
        scrapeControl.Setup(x => x.Start(It.IsAny<Guid>())).Returns(CancellationToken.None);
        var logger = new Mock<ILogger<ScrapingRunner>>();

        var runner = new ScrapingRunner(
            new Mock<IServiceProvider>().Object,
            scrapingService.Object,
            client,
            aiProcessor.Object,
            syncLogService,
            categoryMappingService,
            scrapeControl.Object,
            logger.Object);

        await runner.RunForSiteAsync(site.Id, null, CancellationToken.None);

        var rows = await client.GetAsync<StagingProduct>($"staging_products?site_id=eq.{site.Id}&select=*");
        rows.Should().HaveCount(1);
        rows[0].AIProcessedJson.Should().NotBeNullOrWhiteSpace();

        var processed = JsonSerializer.Deserialize<ProcessedProduct>(rows[0].AIProcessedJson!,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        processed.Should().NotBeNull();
        processed!.Images.Should().Contain("https://example.com/img/main.jpg");
        processed.Attachments.Should().ContainSingle(a => a.FileUrl == "https://example.com/files/datasheet.pdf");
        processed.Currency.Should().Be("MXN");
        processed.Stock.Should().Be(12);
        processed.Specifications.Should().ContainKey("diametro exterior");
        processed.Specifications.Should().ContainKey("source_url");
    }
}
