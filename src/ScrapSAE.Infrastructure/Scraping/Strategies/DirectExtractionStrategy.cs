using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Interfaces;

namespace ScrapSAE.Infrastructure.Scraping.Strategies;

/// <summary>
/// Estrategia para páginas que ya son de detalle de producto
/// </summary>
public class DirectExtractionStrategy : IScrapingStrategy
{
    private readonly ILogger<DirectExtractionStrategy> _logger;
    private readonly ITelemetryService _telemetryService;

    public string StrategyName => "Direct";

    public DirectExtractionStrategy(
        ILogger<DirectExtractionStrategy> logger,
        ITelemetryService telemetryService)
    {
        _logger = logger;
        _telemetryService = telemetryService;
    }

    public async Task<List<ScrapedProduct>> ExecuteAsync(
        object pageObj,
        SiteProfile site,
        Guid executionId,
        CancellationToken cancellationToken = default)
    {
        var page = (IPage)pageObj;
        var products = new List<ScrapedProduct>();
        
        try
        {
            _logger.LogInformation("[DirectStrategy] Intentando extracción directa en {Url}", page.Url);
            
            // Intentar extraer un producto directamente de la página actual
            var product = await ExtractProductFromCurrentPageAsync(page, site, executionId);
            
            if (product != null)
            {
                products.Add(product);
                _logger.LogInformation("[DirectStrategy] Producto extraído exitosamente: {Sku}", product.SkuSource);
                await _telemetryService.RecordSuccessAsync(executionId, "Extracción directa exitosa", page.Url);
            }
            else
            {
                _logger.LogWarning("[DirectStrategy] No se pudo extraer producto de la página actual");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DirectStrategy] Error en extracción directa");
        }
        
        return products;
    }

    private async Task<ScrapedProduct?> ExtractProductFromCurrentPageAsync(
        IPage page,
        SiteProfile site,
        Guid executionId)
    {
        try
        {
            // Extraer datos básicos del producto
            var sku = await ExtractTextAsync(page, "productSku", site);
            var title = await ExtractTextAsync(page, "productName", site);
            var price = await ExtractTextAsync(page, "productPrice", site);
            
            // Validar que al menos tengamos SKU y título
            if (string.IsNullOrEmpty(sku) || string.IsNullOrEmpty(title))
            {
                return null;
            }
            
            var product = new ScrapedProduct
            {
                SkuSource = sku,
                Title = title,
                Price = ParsePrice(price),
                SourceUrl = page.Url,
                ScrapedAt = DateTime.UtcNow
            };
            
            // Intentar extraer campos opcionales
            product.Description = await ExtractTextAsync(page, "productDescription", site);
            product.ImageUrl = await ExtractAttributeAsync(page, "productImage", "src", site);
            
            return product;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DirectStrategy] Error al extraer producto");
            return null;
        }
    }

    private async Task<string?> ExtractTextAsync(IPage page, string selectorKey, SiteProfile site)
    {
        try
        {
            if (site.Selectors is Dictionary<string, object> selectors &&
                selectors.ContainsKey(selectorKey))
            {
                var selector = selectors[selectorKey]?.ToString();
                if (!string.IsNullOrEmpty(selector))
                {
                    var element = await page.QuerySelectorAsync(selector);
                    if (element != null)
                    {
                        return await element.TextContentAsync();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[DirectStrategy] Error al extraer texto con selector {Key}", selectorKey);
        }
        
        return null;
    }

    private async Task<string?> ExtractAttributeAsync(IPage page, string selectorKey, string attribute, SiteProfile site)
    {
        try
        {
            if (site.Selectors is Dictionary<string, object> selectors &&
                selectors.ContainsKey(selectorKey))
            {
                var selector = selectors[selectorKey]?.ToString();
                if (!string.IsNullOrEmpty(selector))
                {
                    var element = await page.QuerySelectorAsync(selector);
                    if (element != null)
                    {
                        return await element.GetAttributeAsync(attribute);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[DirectStrategy] Error al extraer atributo con selector {Key}", selectorKey);
        }
        
        return null;
    }

    private decimal? ParsePrice(string? priceText)
    {
        if (string.IsNullOrEmpty(priceText))
            return null;
        
        // Eliminar símbolos de moneda y comas
        var cleanPrice = priceText.Replace("$", "").Replace(",", "").Trim();
        
        if (decimal.TryParse(cleanPrice, out var price))
        {
            return price;
        }
        
        return null;
    }
}
