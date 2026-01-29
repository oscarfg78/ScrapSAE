using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Interfaces;

namespace ScrapSAE.Infrastructure.Scraping.Strategies;

/// <summary>
/// Estrategia para sitios como Festo con tablas de variantes
/// </summary>
public class FamiliesExtractionStrategy : IScrapingStrategy
{
    private readonly ILogger<FamiliesExtractionStrategy> _logger;
    private readonly ITelemetryService _telemetryService;

    public string StrategyName => "Families";

    public FamiliesExtractionStrategy(
        ILogger<FamiliesExtractionStrategy> logger,
        ITelemetryService telemetryService)
    {
        _logger = logger;
        _telemetryService = telemetryService;
    }

    public async Task<List<ScrapedProduct>> ExecuteAsync(
        IPage page,
        SiteProfile site,
        Guid executionId,
        CancellationToken cancellationToken = default)
    {
        var products = new List<ScrapedProduct>();
        
        try
        {
            _logger.LogInformation("[FamiliesStrategy] Intentando extracción de familias en {Url}", page.Url);
            
            // Hacer scroll para cargar las familias de productos
            await ScrollToLoadContentAsync(page);
            
            // Buscar enlaces a familias de productos
            var familyLinkSelector = GetSelector(site, "familyLink");
            if (string.IsNullOrEmpty(familyLinkSelector))
            {
                _logger.LogWarning("[FamiliesStrategy] No se encontró selector de enlaces de familia");
                return products;
            }
            
            var familyLinks = await page.QuerySelectorAllAsync(familyLinkSelector);
            _logger.LogInformation("[FamiliesStrategy] Encontrados {Count} enlaces de familia", familyLinks.Count);
            
            // Extraer URLs de las familias
            var familyUrls = new List<string>();
            foreach (var link in familyLinks)
            {
                var href = await link.GetAttributeAsync("href");
                if (!string.IsNullOrEmpty(href))
                {
                    var fullUrl = href.StartsWith("http") ? href : new Uri(new Uri(page.Url), href).ToString();
                    familyUrls.Add(fullUrl);
                }
            }
            
            // Navegar a cada familia y extraer productos
            foreach (var familyUrl in familyUrls)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;
                
                var familyProducts = await ExtractProductsFromFamilyAsync(page, familyUrl, site, executionId);
                products.AddRange(familyProducts);
                
                // Pausa entre familias para simular comportamiento humano
                await Task.Delay(Random.Shared.Next(2000, 4000), cancellationToken);
            }
            
            if (products.Any())
            {
                await _telemetryService.RecordSuccessAsync(
                    executionId,
                    $"Extracción de familias exitosa: {products.Count} productos de {familyUrls.Count} familias",
                    page.Url
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FamiliesStrategy] Error en extracción de familias");
        }
        
        return products;
    }

    private async Task<List<ScrapedProduct>> ExtractProductsFromFamilyAsync(
        IPage page,
        string familyUrl,
        SiteProfile site,
        Guid executionId)
    {
        var products = new List<ScrapedProduct>();
        
        try
        {
            _logger.LogInformation("[FamiliesStrategy] Navegando a familia: {Url}", familyUrl);
            await page.GotoAsync(familyUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            
            // Buscar la tabla de variantes
            var variantTableSelector = GetSelector(site, "variantTable");
            if (string.IsNullOrEmpty(variantTableSelector))
            {
                _logger.LogWarning("[FamiliesStrategy] No se encontró selector de tabla de variantes");
                return products;
            }
            
            var table = await page.QuerySelectorAsync(variantTableSelector);
            if (table == null)
            {
                _logger.LogWarning("[FamiliesStrategy] No se encontró tabla de variantes en {Url}", familyUrl);
                return products;
            }
            
            // Buscar todas las filas de productos
            var rowSelector = GetSelector(site, "variantRow");
            if (string.IsNullOrEmpty(rowSelector))
            {
                _logger.LogWarning("[FamiliesStrategy] No se encontró selector de filas de variante");
                return products;
            }
            
            var rows = await table.QuerySelectorAllAsync(rowSelector);
            _logger.LogInformation("[FamiliesStrategy] Encontradas {Count} variantes en {Url}", rows.Count, familyUrl);
            
            // Extraer información de cada variante
            foreach (var row in rows)
            {
                var product = await ExtractProductFromRowAsync(row, site, familyUrl);
                if (product != null)
                {
                    products.Add(product);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FamiliesStrategy] Error al extraer productos de familia {Url}", familyUrl);
        }
        
        return products;
    }

    private async Task<ScrapedProduct?> ExtractProductFromRowAsync(
        IElementHandle row,
        SiteProfile site,
        string sourceUrl)
    {
        try
        {
            // Extraer datos de la fila
            var sku = await ExtractTextFromElementAsync(row, "variantSku", site);
            var title = await ExtractTextFromElementAsync(row, "variantName", site);
            var price = await ExtractTextFromElementAsync(row, "variantPrice", site);
            
            // Validar que al menos tengamos SKU
            if (string.IsNullOrEmpty(sku))
            {
                return null;
            }
            
            var product = new ScrapedProduct
            {
                SkuSource = sku,
                Title = title ?? sku,
                Price = ParsePrice(price),
                SourceUrl = sourceUrl,
                ScrapedAt = DateTime.UtcNow
            };
            
            return product;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[FamiliesStrategy] Error al extraer producto de fila");
            return null;
        }
    }

    private async Task ScrollToLoadContentAsync(IPage page)
    {
        try
        {
            // Hacer scroll gradual para cargar contenido dinámico
            for (int i = 0; i < 3; i++)
            {
                await page.EvaluateAsync("window.scrollBy(0, window.innerHeight / 2)");
                await Task.Delay(500);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[FamiliesStrategy] Error al hacer scroll");
        }
    }

    private async Task<string?> ExtractTextFromElementAsync(
        IElementHandle element,
        string selectorKey,
        SiteProfile site)
    {
        try
        {
            var selector = GetSelector(site, selectorKey);
            if (!string.IsNullOrEmpty(selector))
            {
                var childElement = await element.QuerySelectorAsync(selector);
                if (childElement != null)
                {
                    return await childElement.TextContentAsync();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[FamiliesStrategy] Error al extraer texto con selector {Key}", selectorKey);
        }
        
        return null;
    }

    private string? GetSelector(SiteProfile site, string key)
    {
        if (site.Selectors is Dictionary<string, object> selectors &&
            selectors.ContainsKey(key))
        {
            return selectors[key]?.ToString();
        }
        return null;
    }

    private decimal? ParsePrice(string? priceText)
    {
        if (string.IsNullOrEmpty(priceText))
            return null;
        
        var cleanPrice = priceText.Replace("$", "").Replace(",", "").Trim();
        
        if (decimal.TryParse(cleanPrice, out var price))
        {
            return price;
        }
        
        return null;
    }
}
