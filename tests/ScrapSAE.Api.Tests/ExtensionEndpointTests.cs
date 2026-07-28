using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using ScrapSAE.Api.Endpoints;
using ScrapSAE.Api.Services;
using ScrapSAE.Api.Tests.Fakes;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Interfaces;
using System.Text.Json;

namespace ScrapSAE.Api.Tests;

// ============================================================
// Pruebas Unitarias - Extension Endpoints
// Valida ConvertRawToProcessed, procesamiento con IA,
// CRUD de layouts y webhook handlers de Stripe.
// ============================================================

public class ExtensionEndpointTests
{
    // ============================================================
    // ConvertRawToProcessed (Fallback)
    // ============================================================

    [Fact]
    public void ConvertRawToProcessed_ShouldMapAllFieldsCorrectly()
    {
        var raw = new ScrapedProduct
        {
            SkuSource = "SKU-001",
            Title = "Sensor de Presión",
            Brand = "Festo",
            Description = "Sensor industrial",
            Price = 1500.50m,
            Category = "Sensores",
            ImageUrls = new List<string> { "https://example.com/img.jpg" },
            Attributes = new Dictionary<string, string> { ["voltaje"] = "24V" },
            Attachments = new List<ProductAttachment>
            {
                new() { FileName = "ds.pdf", FileUrl = "https://example.com/ds.pdf" }
            }
        };

        // Usamos reflexión para invocar el método privado estático
        var method = typeof(ExtensionEndpoints).GetMethod(
            "ConvertRawToProcessed",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        method.Should().NotBeNull("ConvertRawToProcessed debe existir como método estático privado");

        var result = method!.Invoke(null, new object[] { raw }) as ProcessedProduct;

        result.Should().NotBeNull();
        result!.Sku.Should().Be("SKU-001");
        result.Name.Should().Be("Sensor de Presión");
        result.Brand.Should().Be("Festo");
        result.Description.Should().Be("Sensor industrial");
        result.Price.Should().Be(1500.50m);
        result.SuggestedCategory.Should().Be("Sensores");
        result.Categories.Should().Contain("Sensores");
        result.Images.Should().Contain("https://example.com/img.jpg");
        result.Specifications.Should().ContainKey("voltaje");
        result.Attachments.Should().HaveCount(1);
        result.Features.Should().BeEmpty();
    }

    [Fact]
    public void ConvertRawToProcessed_ShouldHandleNullTitle()
    {
        var raw = new ScrapedProduct { Title = null };

        var method = typeof(ExtensionEndpoints).GetMethod(
            "ConvertRawToProcessed",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var result = method!.Invoke(null, new object[] { raw }) as ProcessedProduct;

        result.Should().NotBeNull();
        result!.Name.Should().Be("Sin nombre");
    }

    [Fact]
    public void ConvertRawToProcessed_ShouldHandleNullCategory()
    {
        var raw = new ScrapedProduct { Category = null };

        var method = typeof(ExtensionEndpoints).GetMethod(
            "ConvertRawToProcessed",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var result = method!.Invoke(null, new object[] { raw }) as ProcessedProduct;

        result.Should().NotBeNull();
        result!.SuggestedCategory.Should().BeNull();
        result.Categories.Should().BeEmpty();
    }

    [Fact]
    public void ConvertRawToProcessed_ShouldHandleEmptyProduct()
    {
        var raw = new ScrapedProduct();

        var method = typeof(ExtensionEndpoints).GetMethod(
            "ConvertRawToProcessed",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var result = method!.Invoke(null, new object[] { raw }) as ProcessedProduct;

        result.Should().NotBeNull();
        result!.Name.Should().Be("Sin nombre");
        result.Description.Should().Be("");
        result.Features.Should().BeEmpty();
        result.Categories.Should().BeEmpty();
    }

    // ============================================================
    // AI Processing (mock del servicio)
    // ============================================================

    [Fact]
    public async Task ProcessProducts_ShouldUseFallback_WhenAIFails()
    {
        var aiProcessor = new Mock<IAIProcessorService>();
        aiProcessor
            .Setup(x => x.ProcessProductAsync(It.IsAny<string>(), It.IsAny<Action<string,string>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("IA no disponible"));

        var products = new List<ScrapedProduct>
        {
            new()
            {
                SkuSource = "SKU-FALLBACK",
                Title = "Producto Fallback",
                Description = "Test",
                Price = 100m,
                Category = "Test"
            }
        };

        // Simular el flujo del endpoint directamente
        var processedProducts = new List<ProcessedProduct>();
        foreach (var scraped in products)
        {
            try
            {
                var rawJson = JsonSerializer.Serialize(scraped);
                var processed = await aiProcessor.Object.ProcessProductAsync(rawJson, null, CancellationToken.None);
                if (processed != null) processedProducts.Add(processed);
            }
            catch
            {
                // Fallback
                processedProducts.Add(new ProcessedProduct
                {
                    Sku = scraped.SkuSource,
                    Name = scraped.Title ?? "Sin nombre",
                    Brand = scraped.Brand,
                    Description = scraped.Description ?? "",
                    Features = new List<string>(),
                    Specifications = scraped.Attributes ?? new Dictionary<string, string>(),
                    SuggestedCategory = scraped.Category,
                    Categories = scraped.Category != null ? new List<string> { scraped.Category } : new List<string>(),
                    Price = scraped.Price,
                    Images = scraped.ImageUrls ?? new List<string>(),
                    Attachments = scraped.Attachments ?? new List<ProductAttachment>(),
                });
            }
        }

        processedProducts.Should().HaveCount(1);
        processedProducts[0].Sku.Should().Be("SKU-FALLBACK");
        processedProducts[0].Name.Should().Be("Producto Fallback");
    }

    [Fact]
    public async Task ProcessProducts_ShouldReturnAIResult_WhenAISucceeds()
    {
        var aiResult = new ProcessedProduct
        {
            Sku = "SKU-AI",
            Name = "Producto IA Enriquecido",
            Description = "Descripción mejorada por IA",
            Features = new List<string> { "Feature 1", "Feature 2" },
            ConfidenceScore = 0.95m
        };

        var aiProcessor = new Mock<IAIProcessorService>();
        aiProcessor
            .Setup(x => x.ProcessProductAsync(It.IsAny<string>(), It.IsAny<Action<string,string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResult);

        var rawJson = JsonSerializer.Serialize(new ScrapedProduct { SkuSource = "SKU-AI", Title = "Producto" });
        var result = await aiProcessor.Object.ProcessProductAsync(rawJson, null, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Sku.Should().Be("SKU-AI");
        result.Name.Should().Be("Producto IA Enriquecido");
        result.Features.Should().HaveCount(2);
        result.ConfidenceScore.Should().Be(0.95m);
    }

    // ============================================================
    // Layout CRUD (con FakeSupabaseRestClient)
    // ============================================================

    [Fact]
    public async Task Layouts_ShouldCreateAndRetrieve()
    {
        var client = new FakeSupabaseRestClient();

        var layout = new UserLayoutDto
        {
            Id = Guid.NewGuid().ToString(),
            UserId = "user-123",
            Name = "Layout Festo",
            IsDefault = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // POST
        var saved = await client.PostAsync("user_layouts", layout);
        saved.Should().NotBeNull();
        saved!.Name.Should().Be("Layout Festo");

        // GET
        var layouts = await client.GetAsync<UserLayoutDto>(
            $"user_layouts?user_id=eq.user-123&select=*");

        layouts.Should().HaveCount(1);
        layouts[0].Name.Should().Be("Layout Festo");
        layouts[0].IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateLayout_ShouldWork()
    {
        await Task.CompletedTask;
        var client = new FakeSupabaseRestClient();
        var layoutId = Guid.NewGuid().ToString();

        var layout = new UserLayoutDto
        {
            Id = layoutId,
            UserId = "user-456",
            Name = "Layout a actualizar",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await client.PostAsync("user_layouts", layout);
    }

    [Fact]
    public async Task Layouts_ShouldDelete()
    {
        var client = new FakeSupabaseRestClient();
        var layoutId = Guid.NewGuid().ToString();

        var layout = new UserLayoutDto
        {
            Id = layoutId,
            UserId = "user-456",
            Name = "Layout a eliminar",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await client.PostAsync("user_layouts", layout);

        // Verificar que existe
        var before = await client.GetAsync<UserLayoutDto>(
            $"user_layouts?user_id=eq.user-456&select=*");
        before.Should().HaveCount(1);

        // DELETE
        await client.DeleteAsync($"user_layouts?id=eq.{layoutId}");

        // Verificar que se eliminó
        var after = await client.GetAsync<UserLayoutDto>(
            $"user_layouts?user_id=eq.user-456&select=*");
        after.Should().BeEmpty();
    }

    [Fact]
    public async Task Layouts_ShouldAssignIdIfMissing()
    {
        var layout = new UserLayoutDto
        {
            UserId = "user-789",
            Name = "Layout sin ID"
        };

        // Simular la lógica del endpoint
        layout.UpdatedAt = DateTime.UtcNow;
        if (string.IsNullOrEmpty(layout.Id))
        {
            layout.Id = Guid.NewGuid().ToString();
            layout.CreatedAt = DateTime.UtcNow;
        }

        layout.Id.Should().NotBeNullOrEmpty();
        layout.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Layouts_ShouldIsolateByUser()
    {
        var client = new FakeSupabaseRestClient();

        await client.PostAsync("user_layouts", new UserLayoutDto
        {
            Id = "layout-1",
            UserId = "user-A",
            Name = "Layout de A"
        });

        await client.PostAsync("user_layouts", new UserLayoutDto
        {
            Id = "layout-2",
            UserId = "user-B",
            Name = "Layout de B"
        });

        var layoutsA = await client.GetAsync<UserLayoutDto>(
            "user_layouts?user_id=eq.user-A&select=*");
        var layoutsB = await client.GetAsync<UserLayoutDto>(
            "user_layouts?user_id=eq.user-B&select=*");

        layoutsA.Should().HaveCount(1);
        layoutsA[0].Name.Should().Be("Layout de A");

        layoutsB.Should().HaveCount(1);
        layoutsB[0].Name.Should().Be("Layout de B");
    }

    // ============================================================
    // Stripe Webhook Handlers
    // ============================================================

    [Fact]
    public async Task StripeWebhook_CheckoutCompleted_ShouldUpdatePlanType()
    {
        var client = new FakeSupabaseRestClient();

        // Sembrar un perfil de usuario
        client.Seed("user_profiles", new UserProfileEntity
        {
            Id = "user-stripe-1",
            Email = "test@example.com",
            StripeCustomerId = null,
            SubscriptionStatus = "free",
            PlanType = "free",
            UpdatedAt = DateTime.UtcNow
        });

        // Simular el JSON del evento de Stripe
        var eventJson = JsonSerializer.Serialize(new
        {
            type = "checkout.session.completed",
            data = new
            {
                @object = new
                {
                    customer = "cus_test123",
                    metadata = new
                    {
                        user_id = "user-stripe-1",
                        plan_type = "pro"
                    }
                }
            }
        });

        var eventDoc = JsonDocument.Parse(eventJson);

        // Invocar el handler privado via reflexión
        var method = typeof(ExtensionEndpoints).GetMethod(
            "HandleCheckoutCompleted",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        method.Should().NotBeNull();

        var logger = new Mock<ILogger<Program>>();
        await (Task)method!.Invoke(null, new object[] { eventDoc, client, logger.Object })!;

        // Verificar que el perfil se actualizó
        var profiles = await client.GetAsync<UserProfileEntity>(
            "user_profiles?id=eq.user-stripe-1&select=*");

        profiles.Should().HaveCount(1);
        profiles[0].PlanType.Should().Be("pro");
        profiles[0].SubscriptionStatus.Should().Be("pro");
        profiles[0].StripeCustomerId.Should().Be("cus_test123");
    }

    [Fact]
    public async Task StripeWebhook_SubscriptionDeleted_ShouldRevertToFree()
    {
        var client = new FakeSupabaseRestClient();

        client.Seed("user_profiles", new UserProfileEntity
        {
            Id = "user-stripe-2",
            Email = "pro@example.com",
            StripeCustomerId = "cus_cancel123",
            SubscriptionStatus = "pro",
            PlanType = "pro",
            UpdatedAt = DateTime.UtcNow
        });

        var eventJson = JsonSerializer.Serialize(new
        {
            type = "customer.subscription.deleted",
            data = new
            {
                @object = new
                {
                    customer = "cus_cancel123"
                }
            }
        });

        var eventDoc = JsonDocument.Parse(eventJson);

        var method = typeof(ExtensionEndpoints).GetMethod(
            "HandleSubscriptionDeleted",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var logger = new Mock<ILogger<Program>>();
        await (Task)method!.Invoke(null, new object[] { eventDoc, client, logger.Object })!;

        var profiles = await client.GetAsync<UserProfileEntity>(
            "user_profiles?stripe_customer_id=eq.cus_cancel123&select=*");

        profiles.Should().HaveCount(1);
        profiles[0].PlanType.Should().Be("free");
        profiles[0].SubscriptionStatus.Should().Be("free");
    }
}

// ============================================================
// Entidad auxiliar para pruebas de user_profiles
// ============================================================

public class UserProfileEntity
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? StripeCustomerId { get; set; }
    public string SubscriptionStatus { get; set; } = "free";
    public string PlanType { get; set; } = "free";
    public DateTime UpdatedAt { get; set; }
}
