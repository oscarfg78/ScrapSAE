using Xunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ScrapSAE.Infrastructure.Data;
using ScrapSAE.Core.DTOs;

namespace ScrapSAE.Infrastructure.Tests;

/// <summary>
/// Pruebas unitarias para FlashlyIntegrationService
/// </summary>
public class FlashlyIntegrationServiceTests
{
    [Fact]
    public void Constructor_WithValidConfiguration_InitializesSuccessfully()
    {
        // Arrange
        var configuration = CreateConfiguration(enabled: true, apiBaseUrl: "https://api.flashly.com", apiKey: "test-key");
        var httpClientFactory = new TestHttpClientFactory();
        var logger = CreateLogger();

        // Act
        var service = new FlashlyIntegrationService(configuration, httpClientFactory, logger);

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public async Task SendProductAsync_WhenDisabled_ReturnsDisabledResponse()
    {
        // Arrange
        var configuration = CreateConfiguration(enabled: false);
        var httpClientFactory = new TestHttpClientFactory();
        var logger = CreateLogger();
        
        var service = new FlashlyIntegrationService(configuration, httpClientFactory, logger);
        var product = CreateTestProduct();

        // Act
        var result = await service.SendProductAsync(product);

        // Assert
        Assert.False(result.Success);
        Assert.True(result.IsDisabled);
        Assert.Contains("disabled", result.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendProductAsync_WithMissingConfiguration_ReturnsError()
    {
        // Arrange
        var configuration = CreateConfiguration(enabled: true, apiBaseUrl: null, apiKey: null);
        var httpClientFactory = new TestHttpClientFactory();
        var logger = CreateLogger();
        
        var service = new FlashlyIntegrationService(configuration, httpClientFactory, logger);
        var product = CreateTestProduct();

        // Act
        var result = await service.SendProductAsync(product);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("not configured", result.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FindProductBySkuAsync_WhenDisabled_ReturnsNull()
    {
        // Arrange
        var configuration = CreateConfiguration(enabled: false);
        var httpClientFactory = new TestHttpClientFactory();
        var logger = CreateLogger();
        
        var service = new FlashlyIntegrationService(configuration, httpClientFactory, logger);

        // Act
        var result = await service.FindProductBySkuAsync("TEST-001");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task FindProductBySkuAsync_WithEmptySku_ReturnsNull()
    {
        // Arrange
        var configuration = CreateConfiguration(enabled: true, apiBaseUrl: "https://api.flashly.com", apiKey: "test-key");
        var httpClientFactory = new TestHttpClientFactory();
        var logger = CreateLogger();
        
        var service = new FlashlyIntegrationService(configuration, httpClientFactory, logger);

        // Act
        var result = await service.FindProductBySkuAsync("");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void FlashlyProductResponse_CreateSuccess_CreatesSuccessResponse()
    {
        // Arrange
        var product = new FlashlyProduct { Id = "123", Name = "Test" };

        // Act
        var response = FlashlyProductResponse.CreateSuccess(product);

        // Assert
        Assert.True(response.Success);
        Assert.NotNull(response.Product);
        Assert.Equal("123", response.Product.Id);
        Assert.Null(response.ErrorMessage);
        Assert.False(response.IsDisabled);
    }

    [Fact]
    public void FlashlyProductResponse_CreateError_CreatesErrorResponse()
    {
        // Arrange
        var errorMessage = "Test error message";

        // Act
        var response = FlashlyProductResponse.CreateError(errorMessage);

        // Assert
        Assert.False(response.Success);
        Assert.Null(response.Product);
        Assert.Equal(errorMessage, response.ErrorMessage);
        Assert.False(response.IsDisabled);
    }

    [Fact]
    public void FlashlyProductResponse_CreateDisabled_CreatesDisabledResponse()
    {
        // Act
        var response = FlashlyProductResponse.CreateDisabled();

        // Assert
        Assert.False(response.Success);
        Assert.Null(response.Product);
        Assert.True(response.IsDisabled);
        Assert.NotNull(response.ErrorMessage);
    }

    [Fact]
    public void ProcessedProduct_CanBeCreatedWithAllFields()
    {
        // Act
        var product = CreateTestProduct();

        // Assert
        Assert.NotNull(product);
        Assert.Equal("TEST-001", product.Sku);
        Assert.Equal("Test Product", product.Name);
        Assert.Equal("MXN", product.Currency);
        Assert.Equal(50, product.Stock);
        Assert.Single(product.Images);
        Assert.Single(product.Categories);
        Assert.Equal(2, product.Features.Count);
        Assert.Equal(2, product.Specifications.Count);
        Assert.Single(product.Attachments);
    }

    // Helper methods

    private IConfiguration CreateConfiguration(bool enabled, string? apiBaseUrl = null, string? apiKey = null)
    {
        var configData = new Dictionary<string, string?>
        {
            ["Flashly:Enabled"] = enabled.ToString(),
            ["Flashly:ApiBaseUrl"] = apiBaseUrl,
            ["Flashly:ApiKey"] = apiKey,
            ["Flashly:TenantId"] = "test-tenant"
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();
    }

    private ILogger<FlashlyIntegrationService> CreateLogger()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        return loggerFactory.CreateLogger<FlashlyIntegrationService>();
    }

    private ProcessedProduct CreateTestProduct()
    {
        return new ProcessedProduct
        {
            Sku = "TEST-001",
            Name = "Test Product",
            Description = "Test Description",
            Brand = "TestBrand",
            Model = "Model-X",
            Price = 99.99m,
            Currency = "MXN",
            Stock = 50,
            Images = new List<string> { "https://example.com/image1.jpg" },
            Categories = new List<string> { "Category1" },
            Features = new List<string> { "Feature1", "Feature2" },
            Specifications = new Dictionary<string, string>
            {
                { "Weight", "1kg" },
                { "Color", "Blue" }
            },
            Attachments = new List<ProductAttachment>
            {
                new ProductAttachment
                {
                    FileName = "Manual.pdf",
                    FileUrl = "https://example.com/manual.pdf",
                    FileType = "pdf"
                }
            },
            ConfidenceScore = 0.95m
        };
    }
}

/// <summary>
/// Factory de prueba para HttpClient
/// </summary>
public class TestHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
    {
        return new HttpClient();
    }
}
