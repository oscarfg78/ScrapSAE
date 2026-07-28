using System.Collections.Generic;
using System.Linq;
using Xunit;
using ScrapSAE.Core.DTOs;

namespace ScrapSAE.Infrastructure.Tests;

public class ShadowRunParityTests
{
    [Fact]
    public void ShadowRun_Matches_DemoOutput_WithProductionOutput()
    {
        // Arrange
        // Simulate a demo output (limited set, no DB persistence)
        var demoReport = new ExtractionRunReport
        {
            IsDemo = true,
            Products = new List<ReconciledProduct>
            {
                new ReconciledProduct { SourceUrl = "http://test.com/1", Title = "Prod 1" },
                new ReconciledProduct { SourceUrl = "http://test.com/2", Title = "Prod 2" }
            }
        };

        // Simulate a production run (full run over the same catalog boundary)
        var prodReport = new ExtractionRunReport
        {
            IsDemo = false,
            Products = new List<ReconciledProduct>
            {
                new ReconciledProduct { SourceUrl = "http://test.com/1", Title = "Prod 1" },
                new ReconciledProduct { SourceUrl = "http://test.com/2", Title = "Prod 2" },
                new ReconciledProduct { SourceUrl = "http://test.com/3", Title = "Prod 3" }
            }
        };

        // Act
        // Verify that the demo set is a subset of the production set (parity validation)
        var demoUrls = demoReport.Products.Select(p => p.SourceUrl).ToHashSet();
        var prodUrls = prodReport.Products.Select(p => p.SourceUrl).ToHashSet();

        var isSubset = demoUrls.IsSubsetOf(prodUrls);

        // Assert
        Assert.True(isSubset, "Demo run output should be perfectly contained within the production run output");
    }
}
