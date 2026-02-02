using Xunit;
using Microsoft.Extensions.Logging;
using Moq;
using ScrapSAE.Infrastructure.Scraping;

namespace ScrapSAE.Infrastructure.Tests;

/// <summary>
/// Pruebas unitarias simplificadas para EnhancedDataExtractor
/// (Las pruebas con Playwright requieren un navegador real y se ejecutan como E2E)
/// </summary>
public class EnhancedDataExtractorSimpleTests
{
    [Fact]
    public void Constructor_WithLogger_InitializesSuccessfully()
    {
        // Arrange
        var mockLogger = new Mock<ILogger>();

        // Act
        var extractor = new EnhancedDataExtractor(mockLogger.Object);

        // Assert
        Assert.NotNull(extractor);
    }

    [Fact]
    public void EnhancedDataExtractor_CanBeInstantiated()
    {
        // Arrange
        var mockLogger = new Mock<ILogger>();

        // Act
        var extractor = new EnhancedDataExtractor(mockLogger.Object);

        // Assert
        Assert.NotNull(extractor);
    }
}
