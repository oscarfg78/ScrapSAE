using System.Reflection;
using FluentAssertions;
using ScrapSAE.Core.Interfaces;
using ScrapSAE.Infrastructure.Services;
using Xunit;

namespace ScrapSAE.Infrastructure.Tests.Services;

public class ConcurrentScrapingEngineArchTests
{
    [Fact]
    public void ConcurrentScrapingEngine_Constructor_ShouldNotDependOn_ISelectorDiscoveryService()
    {
        // Architectural Constraint Verification:
        // ISelectorDiscoveryService (AI service) is ONLY permitted during initial setup (Steps 1-3).
        // The batch execution engine MUST NOT take ISelectorDiscoveryService as a dependency.
        
        var ctors = typeof(ConcurrentScrapingEngine).GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        var paramTypes = ctors.SelectMany(c => c.GetParameters()).Select(p => p.ParameterType).ToList();

        paramTypes.Should().NotContain(typeof(ISelectorDiscoveryService),
            "the batch execution engine must be strictly deterministic and must not depend on AI services during execution");
    }
}
