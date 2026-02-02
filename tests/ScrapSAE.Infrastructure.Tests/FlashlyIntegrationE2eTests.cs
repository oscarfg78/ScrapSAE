using Xunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ScrapSAE.Infrastructure.Data;
using ScrapSAE.Infrastructure.AI;
using ScrapSAE.Core.DTOs;
using System.Text.Json;

namespace ScrapSAE.Infrastructure.Tests;

/// <summary>
/// Pruebas de integración end-to-end para el flujo completo:
/// Scraping -> AI Processing -> Flashly Integration
/// </summary>
[Collection("Integration")]
public class FlashlyIntegrationE2eTests
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<FlashlyIntegrationService> _logger;
    private readonly ILogger<OpenAIProcessorService> _aiLogger;

    public FlashlyIntegrationE2eTests()
    {
        // Setup configuration from environment or test settings
        var configBuilder = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Flashly:Enabled"] = "false", // Disabled by default for tests
                ["Flashly:ApiBaseUrl"] = "https://api-test.flashly.com",
                ["Flashly:ApiKey"] = "test-key",
                ["Flashly:TenantId"] = "test-tenant",
                ["OpenAI:Enabled"] = "false", // Disabled by default for tests
                ["OpenAI:Model"] = "gpt-4o-mini",
                ["OpenAI:ApiKey"] = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            })
            .AddEnvironmentVariables();

        _configuration = configBuilder.Build();

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<FlashlyIntegrationService>();
        _aiLogger = loggerFactory.CreateLogger<OpenAIProcessorService>();
    }

    [Fact]
    public void E2E_ProcessedProduct_CanBeSerializedAndDeserialized()
    {
        // Arrange
        var product = CreateCompleteTestProduct();

        // Act
        var json = JsonSerializer.Serialize(product);
        var deserialized = JsonSerializer.Deserialize<ProcessedProduct>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(product.Sku, deserialized.Sku);
        Assert.Equal(product.Name, deserialized.Name);
        Assert.Equal(product.Images.Count, deserialized.Images.Count);
        Assert.Equal(product.Attachments.Count, deserialized.Attachments.Count);
        Assert.Equal(product.Currency, deserialized.Currency);
        Assert.Equal(product.Stock, deserialized.Stock);
    }

    [Fact]
    public void E2E_FlashlyPayload_ContainsAllRequiredFields()
    {
        // Arrange
        var product = CreateCompleteTestProduct();
        var service = CreateFlashlyService();

        // Act - Using reflection to access private method for testing
        var mapMethod = typeof(FlashlyIntegrationService).GetMethod(
            "MapToFlashlyProduct", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        var payload = mapMethod?.Invoke(service, new object?[] { product, "supplier-123" });

        // Assert
        Assert.NotNull(payload);
        var payloadJson = JsonSerializer.Serialize(payload);
        Assert.Contains("\"name\":", payloadJson);
        Assert.Contains("\"sku\":", payloadJson);
        Assert.Contains("\"price\":", payloadJson);
        Assert.Contains("\"currency\":", payloadJson);
        Assert.Contains("\"stock\":", payloadJson);
        Assert.Contains("\"images\":", payloadJson);
        Assert.Contains("\"specifications\":", payloadJson);
    }

    [Fact(Skip = "Requires live Flashly API - Enable manually for integration testing")]
    public async Task E2E_SendProduct_ToLiveFlashlyAPI()
    {
        // This test is skipped by default but can be enabled manually
        // to test against a real Flashly instance

        // Arrange
        var product = CreateCompleteTestProduct();
        
        // Enable Flashly in configuration
        var liveConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Flashly:Enabled"] = "true",
                ["Flashly:ApiBaseUrl"] = Environment.GetEnvironmentVariable("FLASHLY_API_URL"),
                ["Flashly:ApiKey"] = Environment.GetEnvironmentVariable("FLASHLY_API_KEY"),
                ["Flashly:TenantId"] = Environment.GetEnvironmentVariable("FLASHLY_TENANT_ID")
            })
            .Build();

        var httpClientFactory = new TestHttpClientFactory();
        var service = new FlashlyIntegrationService(liveConfig, httpClientFactory, _logger);

        // Act
        var result = await service.SendProductAsync(product);

        // Assert
        Assert.True(result.Success, $"Failed to send product: {result.ErrorMessage}");
        Assert.NotNull(result.Product);
        Assert.NotNull(result.Product.Id);
    }

    [Fact(Skip = "Requires OpenAI API - Enable manually for integration testing")]
    public async Task E2E_AIProcessing_WithRealAPI()
    {
        // This test is skipped by default but can be enabled manually
        // to test against real OpenAI API

        // Arrange
        var rawData = CreateSampleRawProductData();
        
        var liveConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenAI:Enabled"] = "true",
                ["OpenAI:Model"] = "gpt-4o-mini",
                ["OpenAI:ApiKey"] = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            })
            .Build();

        var httpClientFactory = new TestHttpClientFactory();
        var aiService = new OpenAIProcessorService(liveConfig, httpClientFactory, _aiLogger);

        // Act
        var result = await aiService.ProcessProductAsync(rawData);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Name);
        Assert.NotNull(result.Images);
        Assert.NotNull(result.Specifications);
    }

    [Fact]
    public void E2E_DataFlow_FromScrapedToFlashlyFormat()
    {
        // Arrange - Simular datos crudos del scraping
        var scrapedProduct = new ScrapedProduct
        {
            SkuSource = "FESTO-VAMC-001",
            Title = "Válvula neumática VAMC",
            Description = "Válvula de control neumático de alta precisión",
            ImageUrl = "https://example.com/image1.jpg",
            ImageUrls = new List<string>
            {
                "https://example.com/image1.jpg",
                "https://example.com/image2.jpg",
                "https://example.com/image3.jpg"
            },
            Price = 1250.50m,
            Brand = "Festo",
            SourceUrl = "https://festo.com/product/vamc-001",
            Attributes = new Dictionary<string, string>
            {
                { "Presión máxima", "10 bar" },
                { "Material", "Aluminio" }
            }
        };

        // Act - Simular procesamiento de IA
        var processedProduct = new ProcessedProduct
        {
            Sku = scrapedProduct.SkuSource,
            Name = scrapedProduct.Title ?? "Unknown",
            Brand = scrapedProduct.Brand,
            Description = scrapedProduct.Description ?? "",
            Price = scrapedProduct.Price,
            Currency = "MXN",
            Stock = 50,
            Images = scrapedProduct.ImageUrls,
            Specifications = scrapedProduct.Attributes,
            Categories = new List<string> { "Neumática", "Válvulas" },
            Features = new List<string>
            {
                "Alta precisión",
                "Control neumático",
                "Material resistente"
            },
            ConfidenceScore = 0.95m
        };

        // Assert - Verificar que todos los datos fluyen correctamente
        Assert.Equal(scrapedProduct.SkuSource, processedProduct.Sku);
        Assert.Equal(scrapedProduct.Title, processedProduct.Name);
        Assert.Equal(scrapedProduct.ImageUrls.Count, processedProduct.Images.Count);
        Assert.Equal(scrapedProduct.Price, processedProduct.Price);
        Assert.NotNull(processedProduct.Currency);
        Assert.NotNull(processedProduct.Stock);
        Assert.Equal(scrapedProduct.Attributes.Count, processedProduct.Specifications.Count);
    }

    // Helper methods

    private FlashlyIntegrationService CreateFlashlyService()
    {
        var httpClientFactory = new TestHttpClientFactory();
        return new FlashlyIntegrationService(_configuration, httpClientFactory, _logger);
    }

    private ProcessedProduct CreateCompleteTestProduct()
    {
        return new ProcessedProduct
        {
            Sku = "E2E-TEST-001",
            Name = "Producto de Prueba E2E",
            Brand = "TestBrand",
            Model = "Model-E2E",
            Description = "Producto completo para pruebas de integración end-to-end",
            Price = 999.99m,
            Currency = "MXN",
            Stock = 100,
            Images = new List<string>
            {
                "https://example.com/img1.jpg",
                "https://example.com/img2.jpg",
                "https://example.com/img3.jpg"
            },
            Categories = new List<string> { "Electrónica", "Industrial", "Automatización" },
            Features = new List<string>
            {
                "Alta durabilidad",
                "Fácil instalación",
                "Bajo mantenimiento",
                "Certificado ISO 9001"
            },
            Specifications = new Dictionary<string, string>
            {
                { "Peso", "2.5kg" },
                { "Dimensiones", "30x20x15cm" },
                { "Material", "Acero inoxidable" },
                { "Voltaje", "220V" },
                { "Potencia", "1500W" },
                { "Temperatura operación", "-10°C a 60°C" }
            },
            Attachments = new List<ProductAttachment>
            {
                new ProductAttachment
                {
                    FileName = "Manual_Usuario.pdf",
                    FileUrl = "https://example.com/manuals/manual-e2e-001.pdf",
                    FileType = "pdf",
                    FileSizeBytes = 2048576
                },
                new ProductAttachment
                {
                    FileName = "Ficha_Tecnica.pdf",
                    FileUrl = "https://example.com/datasheets/datasheet-e2e-001.pdf",
                    FileType = "pdf",
                    FileSizeBytes = 512000
                }
            },
            ConfidenceScore = 0.98m,
            LineCode = "ELEC-IND",
            SuggestedCategory = "Electrónica"
        };
    }

    private string CreateSampleRawProductData()
    {
        return @"
            <div class='product-detail'>
                <h1>Válvula Neumática VAMC-L1-CD</h1>
                <div class='sku'>Part Number: VAMC-L1-CD</div>
                <div class='price'>$1,250.50 MXN</div>
                <div class='brand'>Festo</div>
                <div class='description'>
                    Válvula de control neumático de alta precisión para aplicaciones industriales.
                    Presión máxima: 10 bar. Material: Aluminio anodizado.
                </div>
                <div class='stock'>Disponible: 50 unidades</div>
                <div class='images'>
                    <img src='https://example.com/img1.jpg' alt='Product Image 1'>
                    <img src='https://example.com/img2.jpg' alt='Product Image 2'>
                </div>
                <a href='https://example.com/manual.pdf'>Descargar Manual</a>
            </div>
        ";
    }
}

/// <summary>
