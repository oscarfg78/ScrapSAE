using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using ScrapSAE.Api.Tests.Fakes;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;

namespace ScrapSAE.Api.Tests;

public class ApiE2eTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public ApiE2eTests(ApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_ShouldReturnOk()
    {
        var client = _factory.CreateClient();
        _factory.SupabaseClient.Reset();

        var response = await client.GetAsync("/api/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Scraping_Run_ShouldCreateStagingProducts()
    {
        var client = _factory.CreateClient();
        _factory.SupabaseClient.Reset();
        var site = new SiteProfile
        {
            Id = Guid.NewGuid(),
            Name = "Site A",
            BaseUrl = "https://example.com",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _factory.SupabaseClient.Seed("config_sites", site);

        var response = await client.PostAsync($"/api/scraping/run/{site.Id}", null);
        response.EnsureSuccessStatusCode();

        var stagingResponse = await client.GetAsync("/api/staging-products");
        var products = await stagingResponse.Content.ReadFromJsonAsync<List<StagingProduct>>();

        products.Should().NotBeNull();
        products.Should().HaveCount(2);
    }

    [Fact]
    public async Task SendSelected_ShouldRejectExcludedProduct()
    {
        var client = _factory.CreateClient();
        _factory.SupabaseClient.Reset();
        var product = new StagingProduct
        {
            Id = Guid.NewGuid(),
            Status = "validated",
            ExcludeFromSae = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _factory.SupabaseClient.Seed("staging_products", product);

        var response = await client.PostAsync($"/api/sae/send/{product.Id}", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SendPending_ShouldOnlySendValidatedNotExcluded()
    {
        var client = _factory.CreateClient();
        _factory.SupabaseClient.Reset();
        var validated = new StagingProduct
        {
            Id = Guid.NewGuid(),
            Status = "validated",
            ExcludeFromSae = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var excluded = new StagingProduct
        {
            Id = Guid.NewGuid(),
            Status = "validated",
            ExcludeFromSae = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _factory.SupabaseClient.Seed("staging_products", validated, excluded);

        var response = await client.PostAsync("/api/sae/send-pending", null);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, int>>();
        body.Should().NotBeNull();
        body!["total"].Should().Be(1);
        body["sent"].Should().Be(1);
    }

    [Fact]
    public async Task Settings_ShouldPersistAndReturnValues()
    {
        var client = _factory.CreateClient();

        var payload = new
        {
            supabaseUrl = "https://example.supabase.co",
            supabaseServiceKey = "test-key",
            saeDbPath = @"C:\Temp\SAE90EMPRE01.FDB",
            saeDbHost = "localhost",
            saeDbUser = "SYSDBA",
            saeDbPassword = "masterkey",
            saeDbPort = 3050,
            saeDbCharset = "ISO8859_1",
            saeDbDialect = 3,
            saeDefaultLineCode = "LINEA"
        };

        var saveResponse = await client.PostAsJsonAsync("/api/settings", payload);
        saveResponse.EnsureSuccessStatusCode();

        var settingsResponse = await client.GetAsync("/api/settings");
        settingsResponse.EnsureSuccessStatusCode();

        var json = await settingsResponse.Content.ReadAsStringAsync();
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var root = document.RootElement;
        root.GetProperty("supabaseUrl").GetString().Should().Be(payload.supabaseUrl);
        root.GetProperty("saeDbPath").GetString().Should().Be(payload.saeDbPath);
    }

    [Fact]
    public async Task Inspect_WithInspectOnly_ShouldReturnWrappedResponse_WithoutPersistence()
    {
        var client = _factory.CreateClient();
        _factory.SupabaseClient.Reset();
        var site = new SiteProfile
        {
            Id = Guid.NewGuid(),
            Name = "Site Inspect",
            BaseUrl = "https://example.com",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _factory.SupabaseClient.Seed("config_sites", site);

        var payload = new
        {
            urls = new[] { "https://example.com/p/1" },
            inspectOnly = true,
            manualLogin = false,
            headless = true
        };

        var response = await client.PostAsJsonAsync($"/api/scraping/inspect/{site.Id}", payload);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<InspectUrlsResponse>();
        body.Should().NotBeNull();
        body!.TotalUrls.Should().Be(1);
        body.InspectOnly.Should().BeTrue();
        body.ProductsCreated.Should().Be(0);
        body.ProductsUpdated.Should().Be(0);
    }

    [Fact]
    public async Task Rescrape_ShouldCreateJobAndItems()
    {
        var client = _factory.CreateClient();
        _factory.SupabaseClient.Reset();

        var site = new SiteProfile
        {
            Id = Guid.NewGuid(),
            Name = "Site Rescrape",
            BaseUrl = "https://example.com",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _factory.SupabaseClient.Seed("config_sites", site);

        var p1 = new StagingProduct
        {
            Id = Guid.NewGuid(),
            SiteId = site.Id,
            SkuSource = "SKU-1",
            SourceUrl = "https://example.com/p/1",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var p2 = new StagingProduct
        {
            Id = Guid.NewGuid(),
            SiteId = site.Id,
            SkuSource = "SKU-2",
            SourceUrl = "https://example.com/p/2",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _factory.SupabaseClient.Seed("staging_products", p1, p2);

        var create = await client.PostAsJsonAsync("/api/scraping/rescrape", new RescrapeRequest
        {
            ProductIds = new List<Guid> { p1.Id, p2.Id },
            ManualLogin = true
        });

        create.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var created = await create.Content.ReadFromJsonAsync<RescrapeJobResponse>();
        created.Should().NotBeNull();
        created!.TotalItems.Should().Be(2);

        var statusResponse = await client.GetAsync($"/api/scraping/rescrape/{created.JobId}");
        statusResponse.EnsureSuccessStatusCode();
        var status = await statusResponse.Content.ReadFromJsonAsync<RescrapeJobStatusResponse>();
        status.Should().NotBeNull();
        status!.TotalItems.Should().Be(2);

        var itemsResponse = await client.GetAsync($"/api/scraping/rescrape/{created.JobId}/items");
        itemsResponse.EnsureSuccessStatusCode();
        var items = await itemsResponse.Content.ReadFromJsonAsync<List<RescrapeJobItemResponse>>();
        items.Should().NotBeNull();
        items!.Should().HaveCount(2);

        var logsResponse = await client.GetAsync($"/api/scraping/rescrape/{created.JobId}/logs");
        logsResponse.EnsureSuccessStatusCode();
        var logs = await logsResponse.Content.ReadFromJsonAsync<List<RescrapeJobLogResponse>>();
        logs.Should().NotBeNull();
        logs!.Count.Should().BeGreaterThan(0);
    }
}
