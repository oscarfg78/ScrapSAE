using Xunit;
using Microsoft.Extensions.Logging;
using Moq;
using ScrapSAE.Infrastructure.Scraping;
using ScrapSAE.Core.DTOs;

namespace ScrapSAE.Infrastructure.Tests;

/// <summary>
/// Pruebas unitarias simplificadas para EnhancedDataExtractor
/// </summary>
public class EnhancedDataExtractorTests
{
    private readonly Mock<ILogger> _mockLogger;
    private readonly EnhancedDataExtractor _extractor;

    public EnhancedDataExtractorTests()
    {
        _mockLogger = new Mock<ILogger>();
        _extractor = new EnhancedDataExtractor(_mockLogger.Object);
    }

    [Fact]
    public void Constructor_WithLogger_InitializesSuccessfully()
    {
        // Arrange & Act
        var extractor = new EnhancedDataExtractor(_mockLogger.Object);

        // Assert
        Assert.NotNull(extractor);
    }

    [Fact]
    public void EnhancedDataExtractor_CanBeInstantiated()
    {
        // Arrange & Act
        var logger = new Mock<ILogger>();
        var extractor = new EnhancedDataExtractor(logger.Object);

        // Assert
        Assert.NotNull(extractor);
    }
}
