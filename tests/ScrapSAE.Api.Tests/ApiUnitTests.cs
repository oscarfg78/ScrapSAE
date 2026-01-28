using FluentAssertions;
using Moq;
using ScrapSAE.Api.Services;
using ScrapSAE.Api.Tests.Fakes;
using ScrapSAE.Api.Tests.Stubs;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Interfaces;

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
            .Setup(x => x.ProcessProductAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessedProduct());
        var syncLogService = new SupabaseTableService<SyncLog>(client, "sync_logs");
        var categoryMappingService = new SupabaseTableService<CategoryMapping>(client, "category_mapping");
        var scrapeControl = new Mock<IScrapeControlService>();
        scrapeControl.Setup(x => x.Start(It.IsAny<Guid>())).Returns(CancellationToken.None);

        var runner = new ScrapingRunner(
            new StubScrapingService(),
            client,
            aiProcessor.Object,
            syncLogService,
            categoryMappingService,
            scrapeControl.Object);
        var result = await runner.RunForSiteAsync(site.Id, CancellationToken.None);

        result.ProductsCreated.Should().Be(2);
    }
}
