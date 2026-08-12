using FluentAssertions;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Infrastructure.Services;
using Xunit;

namespace ScrapSAE.Infrastructure.Tests.Services;

public class ProductDataConsolidatorTests
{
    private readonly ProductDataConsolidator _consolidator = new();

    private readonly ExcelProductRecord _sampleRow = new()
    {
        RowIndex = 0,
        Sku = "SKU-100",
        CostoProveedor = 150.00m,
        OptionalAttributes = new Dictionary<string, string> { { "Size", "Large" } }
    };

    [Fact]
    public void Consolidate_WhenBothTargetsFound_ShouldRespectPriorityConfig()
    {
        var r1 = new TargetScrapeResult
        {
            TargetLabel = "Target 1",
            Sku = "SKU-100",
            Status = ScrapingResultStatus.Found,
            RetailPrice = 250.00m,
            ImageUrls = new List<string> { "http://t1.com/img1.jpg" },
            Title = "Product T1",
            SourceDetailUrl = "http://t1.com/detail/100"
        };

        var r2 = new TargetScrapeResult
        {
            TargetLabel = "Target 2",
            Sku = "SKU-100",
            Status = ScrapingResultStatus.Found,
            RetailPrice = 270.00m,
            ImageUrls = new List<string> { "http://t2.com/img2.jpg" },
            Title = "Product T2",
            SourceDetailUrl = "http://t2.com/detail/100"
        };

        var priority = new SourcePriorityConfig
        {
            PriceSource = DataSource.Target2,
            ImageSource = DataSource.Target1
        };

        var consolidated = _consolidator.Consolidate(_sampleRow, r1, r2, priority);

        consolidated.Sku.Should().Be("SKU-100");
        consolidated.SupplierCost.Should().Be(150.00m);
        consolidated.RetailPrice.Should().Be(270.00m); // Target 2 price
        consolidated.ImageUrls.Should().ContainSingle().Which.Should().Be("http://t1.com/img1.jpg"); // Target 1 image
        consolidated.Status.Should().Be(ConsolidatedStatus.Matched);
        consolidated.SourceDetailUrls.Should().HaveCount(2);
    }

    [Fact]
    public void Consolidate_WhenDesignatedPriceSourceNotFound_ShouldFallbackToSecondarySource()
    {
        var r1 = TargetScrapeResult.NotFound("Target 1", "SKU-100", SkipReason.NoSearchResults);

        var r2 = new TargetScrapeResult
        {
            TargetLabel = "Target 2",
            Sku = "SKU-100",
            Status = ScrapingResultStatus.Found,
            RetailPrice = 300.00m,
            ImageUrls = new List<string> { "http://t2.com/img.jpg" }
        };

        var priority = new SourcePriorityConfig
        {
            PriceSource = DataSource.Target1, // Target 1 is primary but returned NotFound
            ImageSource = DataSource.Target2
        };

        var consolidated = _consolidator.Consolidate(_sampleRow, r1, r2, priority);

        consolidated.RetailPrice.Should().Be(300.00m); // Fallback to T2
        consolidated.Status.Should().Be(ConsolidatedStatus.Matched);
    }

    [Fact]
    public void Consolidate_WhenBothTargetsNotFound_ShouldEmitNotMatchedWithSupplierCostPreserved()
    {
        var r1 = TargetScrapeResult.NotFound("Target 1", "SKU-100", SkipReason.NoSearchResults);
        var r2 = TargetScrapeResult.NotFound("Target 2", "SKU-100", SkipReason.NoSearchResults);

        var priority = new SourcePriorityConfig();

        var consolidated = _consolidator.Consolidate(_sampleRow, r1, r2, priority);

        consolidated.Sku.Should().Be("SKU-100");
        consolidated.SupplierCost.Should().Be(150.00m); // SupplierCost is ALWAYS preserved
        consolidated.RetailPrice.Should().Be(150.00m); // Equal to SupplierCost when no web price or margin configured
        consolidated.WarningMessage.Should().NotBeNullOrEmpty();
        consolidated.Status.Should().Be(ConsolidatedStatus.NotMatched);
    }
}
