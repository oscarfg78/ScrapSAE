using Microsoft.Extensions.Logging;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;
using ScrapSAE.Infrastructure.Scraping;
using System.Text.Json;

// Configurar logging
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

var logger = loggerFactory.CreateLogger<Program>();

logger.LogInformation("=== INICIANDO PRUEBA DE SCRAPER DE FESTO CON URLS DIRECTAS ===");

// Cargar configuración unificada de Festo
var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "festo_config_unified.json");
if (!File.Exists(configPath))
{
    // Probar en el directorio actual
    configPath = Path.Combine(Directory.GetCurrentDirectory(), "festo_config_unified.json");
}

if (!File.Exists(configPath))
{
    logger.LogError("No se encontró el archivo de configuración: {Path}", configPath);
    return;
}

var configJson = await File.ReadAllTextAsync(configPath);
var jsonOptions = new JsonSerializerOptions 
{ 
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};
var config = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(configJson, jsonOptions);

if (config == null)
{
    logger.LogError("No se pudo deserializar la configuración");
    return;
}

// Extraer las URLs de categorías
var categoryUrls = new List<string>();
if (config.ContainsKey("categoryUrls"))
{
    categoryUrls = JsonSerializer.Deserialize<List<string>>(config["categoryUrls"].GetRawText()) ?? new List<string>();
}

logger.LogInformation("URLs de categorías a procesar: {Count}", categoryUrls.Count);

// Crear servicio de scraping con todas las dependencias
var scrapingLogger = loggerFactory.CreateLogger<PlaywrightScrapingService>();
var scrapeControl = new ScrapSAE.Infrastructure.Scraping.NoOpScrapeControlService();

// Crear instancias nulas para las dependencias opcionales en pruebas
var scrapingService = new PlaywrightScrapingService(
    scrapingLogger,
    browserSharing: null!,
    signalService: null!,
    scrapeControl: scrapeControl,
    analyzer: null!,
    aiProcessor: null!,
    syncLogService: null!,
    telemetryService: null!);

var allProducts = new List<ScrapedProduct>();

try
{
    logger.LogInformation("\n--- Iniciando scraping ---");
    
    foreach (var categoryUrl in categoryUrls.Take(1)) // Solo la primera categoría para prueba
    {
        logger.LogInformation("\n=== Procesando categoría: {Url} ===", categoryUrl);
        
        // Crear un SiteProfile temporal para esta categoría
        var festoSite = new SiteProfile
        {
            Id = Guid.NewGuid(),
            Name = "Festo México",
            BaseUrl = categoryUrl,
            LoginUrl = "https://www.festo.com/mx/es/",
            IsActive = true,
            RequiresLogin = true,
            CredentialsEncrypted = JsonSerializer.Serialize(new 
            { 
                username = "fred.flores@osmafremx.com",
                password = "Otrosmafremx2302"
            }),
            MaxProductsPerScrape = 5, // Limitar a 5 productos por categoría para prueba
            Selectors = configJson,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        
        var products = await scrapingService.ScrapeAsync(festoSite, CancellationToken.None);
        var productList = products.ToList();
        
        logger.LogInformation("Productos extraídos de esta categoría: {Count}", productList.Count);
        allProducts.AddRange(productList);
    }
    
    logger.LogInformation("\n=== RESULTADOS TOTALES ===");
    logger.LogInformation("Total de productos extraídos: {Count}", allProducts.Count);
    
    if (allProducts.Count > 0)
    {
        logger.LogInformation("\n--- Productos extraídos ---");
        foreach (var product in allProducts)
        {
            logger.LogInformation("\nProducto:");
            logger.LogInformation("  SKU: {Sku}", product.SkuSource ?? "N/A");
            logger.LogInformation("  Título: {Title}", product.Title ?? "N/A");
            logger.LogInformation("  Precio: {Price}", product.Price?.ToString("C") ?? "N/A");
            logger.LogInformation("  URL: {Url}", product.Attributes.GetValueOrDefault("product_url", "N/A"));
            
            if (product.Attributes.ContainsKey("family_title"))
            {
                logger.LogInformation("  Familia: {Family}", product.Attributes["family_title"]);
            }
        }
        
        // Guardar resultados en JSON
        var outputPath = Path.Combine(Directory.GetCurrentDirectory(), "festo_scraping_results_direct.json");
        var outputJsonOptions = new JsonSerializerOptions { WriteIndented = true };
        var resultsJson = JsonSerializer.Serialize(allProducts, outputJsonOptions);
        await File.WriteAllTextAsync(outputPath, resultsJson);
        logger.LogInformation("\n✅ Resultados guardados en: {Path}", outputPath);
    }
    else
    {
        logger.LogWarning("⚠️ No se extrajeron productos. Revisa los logs para más detalles.");
    }
}
catch (Exception ex)
{
    logger.LogError(ex, "❌ Error durante el scraping");
}
finally
{
    await scrapingService.DisposeAsync();
    logger.LogInformation("\n=== PRUEBA FINALIZADA ===");
}
