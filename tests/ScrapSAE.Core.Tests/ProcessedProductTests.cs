using Xunit;
using ScrapSAE.Core.DTOs;

namespace ScrapSAE.Core.Tests;

/// <summary>
/// Pruebas unitarias para ProcessedProduct y ProductAttachment
/// </summary>
public class ProcessedProductTests
{
    [Fact]
    public void ProcessedProduct_ShouldInitializeWithDefaultValues()
    {
        // Arrange & Act
        var product = new ProcessedProduct();

        // Assert
        Assert.NotNull(product.Name);
        Assert.Empty(product.Name);
        Assert.NotNull(product.Description);
        Assert.Empty(product.Description);
        Assert.NotNull(product.Features);
        Assert.Empty(product.Features);
        Assert.NotNull(product.Specifications);
        Assert.Empty(product.Specifications);
        Assert.NotNull(product.Categories);
        Assert.Empty(product.Categories);
        Assert.NotNull(product.Images);
        Assert.Empty(product.Images);
        Assert.NotNull(product.Attachments);
        Assert.Empty(product.Attachments);
    }

    [Fact]
    public void ProcessedProduct_ShouldAcceptMultipleImages()
    {
        // Arrange
        var product = new ProcessedProduct
        {
            Name = "Test Product",
            Images = new List<string>
            {
                "https://example.com/image1.jpg",
                "https://example.com/image2.jpg",
                "https://example.com/image3.jpg"
            }
        };

        // Assert
        Assert.Equal(3, product.Images.Count);
        Assert.Contains("https://example.com/image1.jpg", product.Images);
    }

    [Fact]
    public void ProcessedProduct_ShouldAcceptMultipleCategories()
    {
        // Arrange
        var product = new ProcessedProduct
        {
            Name = "Test Product",
            Categories = new List<string> { "Category1", "Category2", "Category3" }
        };

        // Assert
        Assert.Equal(3, product.Categories.Count);
        Assert.Contains("Category1", product.Categories);
    }

    [Fact]
    public void ProcessedProduct_ShouldStoreCurrencyAndStock()
    {
        // Arrange
        var product = new ProcessedProduct
        {
            Name = "Test Product",
            Price = 99.99m,
            Currency = "MXN",
            Stock = 50
        };

        // Assert
        Assert.Equal("MXN", product.Currency);
        Assert.Equal(50, product.Stock);
        Assert.Equal(99.99m, product.Price);
    }

    [Fact]
    public void ProcessedProduct_ShouldAcceptAttachments()
    {
        // Arrange
        var product = new ProcessedProduct
        {
            Name = "Test Product",
            Attachments = new List<ProductAttachment>
            {
                new ProductAttachment
                {
                    FileName = "Manual.pdf",
                    FileUrl = "https://example.com/manual.pdf",
                    FileType = "pdf",
                    FileSizeBytes = 1024000
                },
                new ProductAttachment
                {
                    FileName = "Datasheet.pdf",
                    FileUrl = "https://example.com/datasheet.pdf",
                    FileType = "pdf"
                }
            }
        };

        // Assert
        Assert.Equal(2, product.Attachments.Count);
        Assert.Equal("Manual.pdf", product.Attachments[0].FileName);
        Assert.Equal(1024000, product.Attachments[0].FileSizeBytes);
    }

    [Fact]
    public void ProductAttachment_ShouldInitializeWithDefaultValues()
    {
        // Arrange & Act
        var attachment = new ProductAttachment();

        // Assert
        Assert.NotNull(attachment.FileName);
        Assert.Empty(attachment.FileName);
        Assert.NotNull(attachment.FileUrl);
        Assert.Empty(attachment.FileUrl);
        Assert.Null(attachment.FileType);
        Assert.Null(attachment.FileSizeBytes);
    }

    [Fact]
    public void ProductAttachment_ShouldStoreAllProperties()
    {
        // Arrange
        var attachment = new ProductAttachment
        {
            FileName = "Technical_Spec.pdf",
            FileUrl = "https://cdn.example.com/files/tech-spec.pdf",
            FileType = "pdf",
            FileSizeBytes = 2048576
        };

        // Assert
        Assert.Equal("Technical_Spec.pdf", attachment.FileName);
        Assert.Equal("https://cdn.example.com/files/tech-spec.pdf", attachment.FileUrl);
        Assert.Equal("pdf", attachment.FileType);
        Assert.Equal(2048576, attachment.FileSizeBytes);
    }

    [Fact]
    public void ProcessedProduct_ShouldSupportCompleteProductData()
    {
        // Arrange
        var product = new ProcessedProduct
        {
            Sku = "TEST-001",
            Name = "Complete Test Product",
            Brand = "TestBrand",
            Model = "Model-X",
            Description = "A complete product with all fields",
            Price = 199.99m,
            Currency = "USD",
            Stock = 100,
            Features = new List<string> { "Feature 1", "Feature 2", "Feature 3" },
            Specifications = new Dictionary<string, string>
            {
                { "Weight", "2.5kg" },
                { "Dimensions", "30x20x10cm" },
                { "Material", "Stainless Steel" }
            },
            Categories = new List<string> { "Electronics", "Industrial" },
            Images = new List<string>
            {
                "https://example.com/img1.jpg",
                "https://example.com/img2.jpg"
            },
            Attachments = new List<ProductAttachment>
            {
                new ProductAttachment
                {
                    FileName = "User_Manual.pdf",
                    FileUrl = "https://example.com/manual.pdf",
                    FileType = "pdf"
                }
            },
            ConfidenceScore = 0.95m,
            LineCode = "LINE-A",
            SuggestedCategory = "Electronics"
        };

        // Assert
        Assert.Equal("TEST-001", product.Sku);
        Assert.Equal("Complete Test Product", product.Name);
        Assert.Equal("TestBrand", product.Brand);
        Assert.Equal("Model-X", product.Model);
        Assert.Equal(199.99m, product.Price);
        Assert.Equal("USD", product.Currency);
        Assert.Equal(100, product.Stock);
        Assert.Equal(3, product.Features.Count);
        Assert.Equal(3, product.Specifications.Count);
        Assert.Equal(2, product.Categories.Count);
        Assert.Equal(2, product.Images.Count);
        Assert.Single(product.Attachments);
        Assert.Equal(0.95m, product.ConfidenceScore);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public void ProcessedProduct_ShouldAcceptNullOrZeroStock(int? stock)
    {
        // Arrange
        var product = new ProcessedProduct
        {
            Name = "Test Product",
            Stock = stock
        };

        // Assert
        Assert.Equal(stock, product.Stock);
    }

    [Theory]
    [InlineData("MXN")]
    [InlineData("USD")]
    [InlineData("EUR")]
    [InlineData("GBP")]
    public void ProcessedProduct_ShouldAcceptVariousCurrencies(string currency)
    {
        // Arrange
        var product = new ProcessedProduct
        {
            Name = "Test Product",
            Currency = currency
        };

        // Assert
        Assert.Equal(currency, product.Currency);
    }
}
