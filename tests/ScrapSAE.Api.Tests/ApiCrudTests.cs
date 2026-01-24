using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using ScrapSAE.Api.Tests.Fakes;
using ScrapSAE.Core.Entities;

namespace ScrapSAE.Api.Tests;

public class ApiCrudTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public ApiCrudTests(ApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Sites_Crud_ShouldWork()
    {
        var client = _factory.CreateClient();
        _factory.SupabaseClient.Reset();
        var site = new SiteProfile
        {
            Id = Guid.NewGuid(),
            Name = "Proveedor",
            BaseUrl = "https://proveedor.test",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var createResponse = await client.PostAsJsonAsync("/api/sites", site);
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var listResponse = await client.GetAsync("/api/sites");
        var list = await listResponse.Content.ReadFromJsonAsync<List<SiteProfile>>();
        list.Should().NotBeNull();
        list.Should().ContainSingle(item => item.Id == site.Id);

        site.Name = "Proveedor editado";
        var updateResponse = await client.PutAsJsonAsync($"/api/sites/{site.Id}", site);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var deleteResponse = await client.DeleteAsync($"/api/sites/{site.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
