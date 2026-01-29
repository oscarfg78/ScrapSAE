using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Interfaces;

namespace ScrapSAE.Infrastructure.Scraping.Strategies;

/// <summary>
/// Estrategia para páginas de resultados de búsqueda tradicionales
/// </summary>
public class ListExtractionStrategy : IScrapingStrategy
{
    private readonly ILogger<ListExtractionStrategy> _logger;
    private readonly ITelemetryService _telemetryService;

    public string StrategyName => "List";

    public ListExtractionStrategy(
        ILogger<ListExtractionStrategy> logger,
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
            _logger.LogInformation("[ListStrategy] Intentando extracción de lista en {Url}", page.Url);
            
            // Buscar el contenedor de la lista de productos
            var listSelector = GetSelector(site, "productList");
            if (string.IsNullOrEmpty(listSelector))
            {
                _logger.LogWarning("[ListStrategy] No se encontró selector de lista de productos");
                return products;
            }
            
            var listContainer = await page.QuerySelectorAsync(listSelector);
            if (listContainer == null)
            {
                _logger.LogWarning("[ListStrategy] No se encontró contenedor de lista");
                return products;
            }
            
            // Buscar todos los elementos de producto en la lista
            var itemSelector = GetSelector(site, "productItem");
            if (string.IsNullOrEmpty(itemSelector))
            {
                _logger.LogWarning("[ListStrategy] No se encontró selector de items de producto");
                return products;
            }
            
            var productElements = await listContainer.QuerySelectorAllAsync(itemSelector);
            _logger.LogInformation("[ListStrategy] Encontrados {Count} productos en la lista", productElements.Count);
            
            // Extraer información de cada producto
            foreach (var productElement in productElements)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;
                
                var product = await ExtractProductFromElementAsync(productElement, site, page.Url);
                if (product != null)
                {
                    products.Add(product);
                }
            }
            
            if (products.Any())
            {
                await _telemetryService.RecordSuccessAsync(
                    executionId,
                    $"Extracción de lista exitosa: {products.Count} productos",
                    page.Url
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ListStrategy] Error en extracción de lista");
        }
        
        return products;
    }

    private async Task<ScrapedProduct?> ExtractProductFromElementAsync(
        IElementHandle element,
        SiteProfile site,
        string sourceUrl)
    {
        try
        {
            // Extraer datos básicos
            var sku = await ExtractTextFromElementAsync(element, "productSku", site);
            var title = await ExtractTextFromElementAsync(element, "productName", site);
            var price = await ExtractTextFromElementAsync(element, "productPrice", site);
            
            // Validar que al menos tengamos título
            if (string.IsNullOrEmpty(title))
            {
                return null;
            }
            
            var product = new ScrapedProduct
            {
                SkuSource = sku,
                Title = title,
                Price = ParsePrice(price),
                SourceUrl = sourceUrl,
                ScrapedAt = DateTime.UtcNow
            };
            
            // Intentar extraer URL de detalle
            var linkSelector = GetSelector(site, "productLink");
            if (!string.IsNullOrEmpty(linkSelector))
            {
                var linkElement = await element.QuerySelectorAsync(linkSelector);
                if (linkElement != null)
                {
                    var href = await linkElement.GetAttributeAsync("href");
                    if (!string.IsNullOrEmpty(href))
                    {
                        product.SourceUrl = href.StartsWith("http") ? href : new Uri(new Uri(sourceUrl), href).ToString();
                    }
                }
            }
            
            return product;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[ListStrategy] Error al extraer producto de elemento");
            return null;
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
            _logger.LogDebug(ex, "[ListStrategy] Error al extraer texto con selector {Key}", selectorKey);
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
