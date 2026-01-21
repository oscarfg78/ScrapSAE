using FluentAssertions;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Enums;

namespace ScrapSAE.Core.Tests;

public class EntitiesTests
{
    [Fact]
    public void StagingProduct_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var product = new StagingProduct();
        
        // Assert
        product.Status.Should().Be("pending");
        product.Attempts.Should().Be(0);
    }

    [Fact]
    public void SiteProfile_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var site = new SiteProfile();
        
        // Assert
        site.IsActive.Should().BeTrue();
        site.RequiresLogin.Should().BeFalse();
        site.Name.Should().BeEmpty();
        site.BaseUrl.Should().BeEmpty();
    }

    [Fact]
    public void ProductStatus_ShouldHaveAllExpectedValues()
    {
        // Assert
        Enum.GetValues<ProductStatus>().Should().HaveCount(5);
        Enum.IsDefined(ProductStatus.Pending).Should().BeTrue();
        Enum.IsDefined(ProductStatus.Validated).Should().BeTrue();
        Enum.IsDefined(ProductStatus.Synced).Should().BeTrue();
        Enum.IsDefined(ProductStatus.Error).Should().BeTrue();
        Enum.IsDefined(ProductStatus.Discontinued).Should().BeTrue();
    }

    [Fact]
    public void OperationType_ShouldHaveAllExpectedValues()
    {
        // Assert
        Enum.GetValues<OperationType>().Should().HaveCount(4);
        Enum.IsDefined(OperationType.Scrape).Should().BeTrue();
        Enum.IsDefined(OperationType.AIProcess).Should().BeTrue();
        Enum.IsDefined(OperationType.SAESync).Should().BeTrue();
        Enum.IsDefined(OperationType.Webhook).Should().BeTrue();
    }
}

public class DTOsTests
{
    [Fact]
    public void ScrapedProduct_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var product = new ScrapedProduct();
        
        // Assert
        product.Attributes.Should().NotBeNull();
        product.Attributes.Should().BeEmpty();
        product.ScrapedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ProcessedProduct_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var product = new ProcessedProduct();
        
        // Assert
        product.Name.Should().BeEmpty();
        product.Description.Should().BeEmpty();
        product.Features.Should().NotBeNull();
        product.Specifications.Should().NotBeNull();
    }

    [Fact]
    public void OperationResult_Ok_ShouldSetCorrectProperties()
    {
        // Arrange & Act
        var result = OperationResult<string>.Ok("test data", 100);
        
        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Be("test data");
        result.DurationMs.Should().Be(100);
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void OperationResult_Fail_ShouldSetCorrectProperties()
    {
        // Arrange & Act
        var result = OperationResult<string>.Fail("error message");
        
        // Assert
        result.Success.Should().BeFalse();
        result.Data.Should().BeNull();
        result.ErrorMessage.Should().Be("error message");
    }

    [Fact]
    public void SiteSelectors_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var selectors = new SiteSelectors();
        
        // Assert
        selectors.MaxPages.Should().Be(10);
        selectors.UsesInfiniteScroll.Should().BeFalse();
    }

    [Fact]
    public void ProductWebhookPayload_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var payload = new ProductWebhookPayload();
        
        // Assert
        payload.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        payload.Sku.Should().BeEmpty();
        payload.Name.Should().BeEmpty();
    }
}

public class SAEEntitiesTests
{
    [Fact]
    public void ProductSAE_ShouldHaveAllExpectedProperties()
    {
        // Arrange & Act
        var product = new ProductSAE
        {
            CVE_ART = "SKU001",
            DESCR = "Test Product",
            LIN_PROD = "LINE1",
            EXIST = 100,
            PREC_X_MAY = 50.00m,
            PREC_X_MEN = 60.00m
        };
        
        // Assert
        product.CVE_ART.Should().Be("SKU001");
        product.DESCR.Should().Be("Test Product");
        product.LIN_PROD.Should().Be("LINE1");
        product.EXIST.Should().Be(100);
        product.PREC_X_MAY.Should().Be(50.00m);
        product.PREC_X_MEN.Should().Be(60.00m);
    }

    [Fact]
    public void ProductCreate_ShouldBeValid()
    {
        // Arrange & Act
        var product = new ProductCreate
        {
            CVE_ART = "NEW001",
            DESCR = "New Product",
            LIN_PROD = "LINE1",
            EXIST = 50,
            PREC_X_MAY = 100.00m,
            PREC_X_MEN = 120.00m,
            ULT_COSTO = 80.00m
        };
        
        // Assert
        product.Should().NotBeNull();
        product.CVE_ART.Should().NotBeEmpty();
        product.DESCR.Should().NotBeEmpty();
    }

    [Fact]
    public void ProductUpdate_ShouldBeValid()
    {
        // Arrange & Act
        var update = new ProductUpdate
        {
            CVE_ART = "SKU001",
            DESCR = "Updated Description",
            EXIST = 200,
            PREC_X_MAY = 55.00m,
            PREC_X_MEN = 65.00m
        };
        
        // Assert
        update.Should().NotBeNull();
        update.CVE_ART.Should().NotBeEmpty();
    }
}
