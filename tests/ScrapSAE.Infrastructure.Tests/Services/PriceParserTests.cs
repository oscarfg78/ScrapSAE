using FluentAssertions;
using ScrapSAE.Infrastructure.Services;
using Xunit;

namespace ScrapSAE.Infrastructure.Tests.Services;

public class PriceParserTests
{
    [Theory]
    [InlineData("$1,234.56", 1234.56)]
    [InlineData("MXN $1.234,56", 1234.56)]
    [InlineData("USD 99.90", 99.90)]
    [InlineData("1 234.50 €", 1234.50)]
    [InlineData(" 450.00 ", 450.00)]
    [InlineData("$ 0.99", 0.99)]
    [InlineData("1500", 1500.00)]
    public void TryParse_ValidPriceStrings_ShouldParseCorrectly(string input, decimal expected)
    {
        var result = PriceParser.TryParse(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("N/A")]
    [InlineData("Agotado")]
    [InlineData("---")]
    public void TryParse_InvalidOrNullStrings_ShouldReturnNull(string? input)
    {
        var result = PriceParser.TryParse(input);
        result.Should().BeNull();
    }
}
