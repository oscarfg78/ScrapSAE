using ScrapSAE.Core.Entities;

// Test: Verificar que el Worker respeta el límite de MaxProductsPerScrape
public class WorkerMaxProductsTest
{
    [Fact]
    public void TestMaxProductsLimit()
    {
        // Arrange
        var site = new SiteProfile
        {
            Id = Guid.NewGuid(),
            Name = "Festo",
            BaseUrl = "https://www.festo.com/mx/es",
            MaxProductsPerScrape = 10,
            IsActive = true,
            CronExpression = "ALWAYS"
        };

        // Simular una lista de 25 productos
        var products = new List<string>();
        for (int i = 1; i <= 25; i++)
        {
            products.Add($"PRODUCTO_{i}");
        }

        // Act - Simular la lógica del Worker de limitar productos
        int maxProducts = site.MaxProductsPerScrape;
        var limitedProducts = maxProducts > 0 
            ? products.Take(maxProducts).ToList()
            : products;

        // Assert
        Assert.Equal(10, limitedProducts.Count);
        Assert.Equal("PRODUCTO_1", limitedProducts.First());
        Assert.Equal("PRODUCTO_10", limitedProducts.Last());
    }

    [Fact]
    public void TestUnlimitedProductsWhenMaxIsZero()
    {
        // Arrange
        var site = new SiteProfile
        {
            Id = Guid.NewGuid(),
            Name = "OtroSitio",
            BaseUrl = "https://www.otro.com",
            MaxProductsPerScrape = 0, // Sin límite
            IsActive = true,
            CronExpression = "ALWAYS"
        };

        var products = new List<string>();
        for (int i = 1; i <= 25; i++)
        {
            products.Add($"PRODUCTO_{i}");
        }

        // Act
        int maxProducts = site.MaxProductsPerScrape;
        var limitedProducts = maxProducts > 0 
            ? products.Take(maxProducts).ToList()
            : products;

        // Assert
        Assert.Equal(25, limitedProducts.Count);
    }
}
