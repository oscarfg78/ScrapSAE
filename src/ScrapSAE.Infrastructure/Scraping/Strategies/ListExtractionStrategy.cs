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
        object pageObj,
        SiteProfile site,
        Guid executionId,
        CancellationToken cancellationToken = default)
    {
        var page = (IPage)pageObj;
        var products = new List<ScrapedProduct>();
        
        try
        {
            _logger.LogInformation("[ListStrategy] Intentando extracción de lista en {Url}", page.Url);
            
            // Buscar el contenedor de la lista de productos
            var listSelector = GetDualSelector(site, "productContainer");
            if (listSelector == null)
            {
                _logger.LogWarning("[ListStrategy] No se encontró selector de lista de productos (productContainer)");
                return products;
            }
            
            var listContainer = await QuerySelectorResilientAsync(page, listSelector);
            if (listContainer == null)
            {
                _logger.LogWarning("[ListStrategy] No se encontró contenedor de lista en el DOM");
                return products;
            }
            
            // Buscar todos los elementos de producto en la lista
            var itemSelector = GetDualSelector(site, "productCard");
            if (itemSelector == null)
            {
                _logger.LogWarning("[ListStrategy] No se encontró selector de items de producto (productCard)");
                return products;
            }
            
            var productElements = await QuerySelectorAllResilientAsync(listContainer, itemSelector);
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
            var sku = await ExtractTextFromElementAsync(element, "sku", site);
            var title = await ExtractTextFromElementAsync(element, "name", site);
            var price = await ExtractTextFromElementAsync(element, "price", site);
            
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
            var linkSelector = GetDualSelector(site, "detailLink"); // Puede que el wizard no lo genere aún
            
            var linkElement = linkSelector != null 
                ? await QuerySelectorResilientAsync(element, linkSelector) 
                : await element.QuerySelectorAsync("a"); // Fallback al primer enlace dentro de la tarjeta
                
            if (linkElement != null)
            {
                var href = await linkElement.GetAttributeAsync("href");
                if (!string.IsNullOrEmpty(href))
                {
                    product.SourceUrl = href.StartsWith("http") ? href : new Uri(new Uri(sourceUrl), href).ToString();
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
            var selector = GetDualSelector(site, selectorKey);
            if (selector != null)
            {
                var childElement = await QuerySelectorResilientAsync(element, selector);
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

    private DualSelector? GetDualSelector(SiteProfile site, string key)
    {
        if (site.Selectors == null) return null;
        
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(site.Selectors);
            var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(json);
            if (dict != null && dict.TryGetValue(key, out var element))
            {
                if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    return System.Text.Json.JsonSerializer.Deserialize<DualSelector>(element.GetRawText(), new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                else if (element.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var strVal = element.GetString();
                    if (!string.IsNullOrWhiteSpace(strVal) && strVal.TrimStart().StartsWith("{"))
                    {
                        try 
                        {
                            var parsed = System.Text.Json.JsonSerializer.Deserialize<DualSelector>(strVal, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                            if (parsed != null) return parsed;
                        }
                        catch { }
                    }
                    return new DualSelector { Css = strVal };
                }
            }
        }
        catch { }
        return null;
    }

    private async Task<IElementHandle?> QuerySelectorResilientAsync(IPage page, DualSelector selector)
    {
        IElementHandle? element = null;
        if (!string.IsNullOrWhiteSpace(selector.Css))
        {
            try { element = await page.QuerySelectorAsync(selector.Css); } catch { }
        }
        if (element == null && !string.IsNullOrWhiteSpace(selector.XPath))
        {
            var xpath = selector.XPath.StartsWith("xpath=") ? selector.XPath : $"xpath={selector.XPath}";
            try { element = await page.QuerySelectorAsync(xpath); } catch { }
        }
        return element;
    }

    private async Task<IElementHandle?> QuerySelectorResilientAsync(IElementHandle parent, DualSelector selector)
    {
        IElementHandle? element = null;
        if (!string.IsNullOrWhiteSpace(selector.Css))
        {
            try { element = await parent.QuerySelectorAsync(selector.Css); } catch { }
        }
        if (element == null && !string.IsNullOrWhiteSpace(selector.XPath))
        {
            var xpath = selector.XPath.StartsWith("xpath=") ? selector.XPath : $"xpath={selector.XPath}";
            try { element = await parent.QuerySelectorAsync(xpath); } catch { }
        }
        return element;
    }

    private async Task<IReadOnlyList<IElementHandle>> QuerySelectorAllResilientAsync(IElementHandle parent, DualSelector selector)
    {
        IReadOnlyList<IElementHandle>? elements = null;
        if (!string.IsNullOrWhiteSpace(selector.Css))
        {
            try { elements = await parent.QuerySelectorAllAsync(selector.Css); } catch { }
        }
        if ((elements == null || elements.Count == 0) && !string.IsNullOrWhiteSpace(selector.XPath))
        {
            var xpath = selector.XPath.StartsWith("xpath=") ? selector.XPath : $"xpath={selector.XPath}";
            try { elements = await parent.QuerySelectorAllAsync(xpath); } catch { }
        }
        return elements ?? new List<IElementHandle>();
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
