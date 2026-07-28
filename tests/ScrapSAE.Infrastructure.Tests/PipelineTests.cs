using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Interfaces;
using ScrapSAE.Infrastructure.Scraping.Pipeline;

namespace ScrapSAE.Infrastructure.Tests;

public class PipelineTests
{
    private class MockContributor : IContributor
    {
        private readonly ContributorStatus _status;
        private readonly List<ProductObservation> _observations;

        public ContributorDescriptor Descriptor { get; }

        public MockContributor(string id, string type, ContributorStatus status, List<ProductObservation> observations)
        {
            Descriptor = new ContributorDescriptor { Id = id, Type = type };
            _status = status;
            _observations = observations;
        }

        public Task<ContributorResult> ExecuteAsync(ExtractionExecutionRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ContributorResult
            {
                ContributorId = Descriptor.Id,
                Status = _status,
                Observations = _observations
            });
        }
    }

    [Fact]
    public async Task ExecutionPlanner_ResolvesFallbackPolicy_WhenPrimaryFails()
    {
        // Arrange
        var reconciliationEngine = new ReconciliationEngine();
        var planner = new ExecutionPlanner(reconciliationEngine);

        var primary = new MockContributor("primary", "Primary", ContributorStatus.NoData, new List<ProductObservation>());
        var legacy = new MockContributor("legacy", "LegacyFallback", ContributorStatus.Success, new List<ProductObservation>
        {
            new ProductObservation { Field = "Title", RawValue = "Legacy Product", SourceUrl = "http://test.com/1" }
        });

        var request = new ExtractionExecutionRequest { RunId = Guid.NewGuid().ToString() };

        // Act
        var report = await planner.ExecutePipelineAsync(request, new[] { primary, legacy }, CancellationToken.None);

        // Assert
        Assert.Single(report.Products);
        Assert.Equal("Legacy Product", report.Products.First().Title);
    }

    [Fact]
    public void ReconciliationEngine_IdentityResolution_UsesSourceUrl()
    {
        // Arrange
        var engine = new ReconciliationEngine();
        var observations = new List<ProductObservation>
        {
            new ProductObservation { Field = "Title", RawValue = "Product A", SourceUrl = "http://test.com/A" },
            new ProductObservation { Field = "Price", RawValue = "100", SourceUrl = "http://test.com/A" },
            new ProductObservation { Field = "Title", RawValue = "Product B", SourceUrl = "http://test.com/B" }
        };

        // Act
        var products = engine.Reconcile(observations);

        // Assert
        Assert.Equal(2, products.Count);
        Assert.Equal(100, products.Single(p => p.Title == "Product A").Price);
    }
}
