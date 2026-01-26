using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using ScrapSAE.Api.Tests.Fakes;
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
}
