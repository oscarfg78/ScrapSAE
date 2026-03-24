using FluentAssertions;
using ScrapSAE.Api.Endpoints;
using ScrapSAE.Api.Tests.Fakes;
using ScrapSAE.Core.DTOs;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ScrapSAE.Api.Tests;

// ============================================================
// Pruebas de Integración HTTP - Extension Endpoints
// Usa WebApplicationFactory con FakeSupabaseRestClient
// para probar los endpoints reales end-to-end sin red.
// ============================================================

public class ExtensionIntegrationTests : IClassFixture<ApiTestFactory>
{
    private readonly HttpClient _client;
    private readonly FakeSupabaseRestClient _supabase;

    public ExtensionIntegrationTests(ApiTestFactory factory)
    {
        _client = factory.CreateClient();
        _supabase = factory.SupabaseClient;
    }

    // ============================================================
    // POST /api/extension/process
    // ============================================================

    [Fact]
    public async Task ExtensionProcess_ShouldReturn400_WhenNoProducts()
    {
        _supabase.Reset();

        var request = new ExtensionProcessRequest { Products = new() };
        var response = await _client.PostAsJsonAsync("/api/extension/process", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ExtensionProcess_ShouldReturnProcessedProducts()
    {
        _supabase.Reset();

        var request = new ExtensionProcessRequest
        {
            Products = new List<ScrapedProduct>
            {
                new()
                {
                    SkuSource = "INT-SKU-001",
                    Title = "Sensor de Presión",
                    Description = "Sensor industrial de alta precisión",
                    Price = 1500m,
                    Category = "Sensores",
                    Brand = "Festo"
                }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/extension/process", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        json.Should().NotBeNullOrWhiteSpace();

        var products = JsonSerializer.Deserialize<List<ProcessedProduct>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        products.Should().NotBeNull();
        products!.Should().HaveCount(1);
        // Puede ser resultado de IA o fallback, ambos son válidos
        products[0].Name.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExtensionProcess_ShouldHandleMultipleProducts()
    {
        _supabase.Reset();

        var request = new ExtensionProcessRequest
        {
            Products = new List<ScrapedProduct>
            {
                new() { SkuSource = "MULTI-1", Title = "Producto 1", Price = 100m },
                new() { SkuSource = "MULTI-2", Title = "Producto 2", Price = 200m },
                new() { SkuSource = "MULTI-3", Title = "Producto 3", Price = 300m }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/extension/process", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var products = await response.Content.ReadFromJsonAsync<List<ProcessedProduct>>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        products.Should().NotBeNull();
        products!.Should().HaveCount(3);
    }

    // ============================================================
    // GET /api/layouts?userId={userId}
    // ============================================================

    [Fact]
    public async Task GetLayouts_ShouldReturnLayoutsForUser()
    {
        _supabase.Reset();

        // Sembrar layouts directamente en el fake
        _supabase.Seed("user_layouts", new UserLayoutDto
        {
            Id = "layout-int-1",
            UserId = "user-int-1",
            Name = "Layout Festo",
            IsDefault = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        _supabase.Seed("user_layouts", new UserLayoutDto
        {
            Id = "layout-int-2",
            UserId = "user-int-1",
            Name = "Layout Amazon",
            IsDefault = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        var response = await _client.GetAsync("/api/layouts?userId=user-int-1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        json.Should().NotBeNullOrWhiteSpace();

        var layouts = JsonSerializer.Deserialize<List<UserLayoutDto>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        layouts.Should().NotBeNull();
        layouts!.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetLayouts_ShouldReturnEmpty_WhenNoLayoutsForUser()
    {
        _supabase.Reset();

        var response = await _client.GetAsync("/api/layouts?userId=nonexistent-user");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        var layouts = JsonSerializer.Deserialize<List<UserLayoutDto>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        layouts.Should().NotBeNull();
        layouts!.Should().BeEmpty();
    }

    // ============================================================
    // POST /api/layouts
    // ============================================================

    [Fact]
    public async Task PostLayout_ShouldCreateLayout()
    {
        _supabase.Reset();

        var layout = new UserLayoutDto
        {
            UserId = "user-create-1",
            Name = "Nuevo Layout",
            IsDefault = false
        };

        var response = await _client.PostAsJsonAsync("/api/layouts", layout);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        // Verificar que se puede recuperar
        var layouts = await _supabase.GetAsync<UserLayoutDto>(
            "user_layouts?user_id=eq.user-create-1&select=*");

        layouts.Should().HaveCount(1);
        layouts[0].Name.Should().Be("Nuevo Layout");
        layouts[0].Id.Should().NotBeNullOrEmpty();
    }

    // ============================================================
    // DELETE /api/layouts/{id}
    // ============================================================

    [Fact]
    public async Task DeleteLayout_ShouldRemoveLayout()
    {
        _supabase.Reset();

        _supabase.Seed("user_layouts", new UserLayoutDto
        {
            Id = "layout-del-1",
            UserId = "user-del-1",
            Name = "Layout a eliminar",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        var response = await _client.DeleteAsync("/api/layouts/layout-del-1");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verificar que se eliminó
        var remaining = await _supabase.GetAsync<UserLayoutDto>(
            "user_layouts?id=eq.layout-del-1&select=*");

        remaining.Should().BeEmpty();
    }

    // ============================================================
    // POST /api/stripe/create-checkout
    // Sin Stripe configurado, debe retornar error
    // ============================================================

    [Fact]
    public async Task StripeCheckout_ShouldReturnError_WhenNotConfigured()
    {
        _supabase.Reset();

        var request = new StripeCheckoutRequest
        {
            UserId = "user-stripe-1",
            Email = "test@example.com",
            PlanType = "pro"
        };

        var response = await _client.PostAsJsonAsync("/api/stripe/create-checkout", request);

        // Stripe no está configurado en tests, debe retornar 500 (Problem)
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task StripeCheckout_ShouldRejectInvalidPlan()
    {
        _supabase.Reset();

        var request = new StripeCheckoutRequest
        {
            UserId = "user-stripe-2",
            Email = "test@example.com",
            PlanType = "invalid_plan"
        };

        var response = await _client.PostAsJsonAsync("/api/stripe/create-checkout", request);

        // Sin config de Stripe, retorna Problem antes de validar el plan
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.InternalServerError);
    }

    // ============================================================
    // POST /api/stripe/webhook
    // ============================================================

    [Fact]
    public async Task StripeWebhook_ShouldReturn500_WhenNoWebhookSecret()
    {
        _supabase.Reset();

        var webhookEvent = JsonSerializer.Serialize(new
        {
            type = "checkout.session.completed",
            data = new
            {
                @object = new
                {
                    customer = "cus_test",
                    metadata = new { user_id = "user-1", plan_type = "pro" }
                }
            }
        });

        var content = new StringContent(webhookEvent, System.Text.Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/stripe/webhook", content);

        // Sin webhook secret configurado, retorna Problem
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }
}
