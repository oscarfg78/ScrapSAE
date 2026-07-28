using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _telemetryService = telemetryService ?? throw new ArgumentNullException(nameof(telemetryService));
    }

    public async Task<List<ScrapedProduct>> ExecuteAsync(
        object pageObj,
        SiteProfile site,
        Guid executionId,
        ScrapeExecutionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var page = (IPage)pageObj;
        var products = new List<ScrapedProduct>();
        
        try
        {
            _logger.LogInformation("[DirectStrategy] Intentando extracción directa en {Url}", page.Url);
            
            var product = await ExtractProductFromCurrentPageAsync(page, site, executionId, context);
            
            if (SelectorCombinator.IsValidDirectProduct(product))
            {
                products.Add(product!);
                _logger.LogInformation("[DirectStrategy] Producto extraído exitosamente: {Sku}", product!.SkuSource ?? product.Title);
                context?.LogTracker?.AddLog("DirectExtraction", details: $"Producto extraído exitosamente: {product.SkuSource ?? product.Title}", count: 1);
                await _telemetryService.RecordSuccessAsync(executionId, "Extracción directa exitosa", page.Url);
            }
            else
            {
                _logger.LogWarning("[DirectStrategy] No se pudo extraer producto válido de la página actual");
                context?.LogTracker?.AddLog("DirectExtraction", error: "No se pudo extraer producto de la página actual", count: 0);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DirectStrategy] Error en extracción directa");
            await _telemetryService.RecordFailureAsync(new DiagnosticPackage
            {
                ExecutionId = executionId,
                Url = page.Url,
                FailureType = "DirectExtractionFailed"
            });
        }
        
        return products;
    }

    private async Task<ScrapedProduct?> ExtractProductFromCurrentPageAsync(
        IPage page,
        SiteProfile site,
        Guid executionId,
        ScrapeExecutionContext? context = null)
    {
        try
        {
            var sku = await ExtractSkuAsync(page, site, context);
            var title = await ExtractTextAsync(page, "name", site, context);
            var priceStr = await ExtractTextAsync(page, "price", site, context);
            
            if (string.IsNullOrEmpty(title))
            {
                var h1 = await page.QuerySelectorAsync("h1, .product-title, .product-name");
                if (h1 != null)
                {
                    title = (await h1.TextContentAsync())?.Trim();
                }
            }

            if (string.IsNullOrEmpty(sku) && string.IsNullOrEmpty(title))
            {
                return null;
            }
            
            var rawHtml = string.Empty;
            try { rawHtml = await page.ContentAsync(); } catch { }

            var product = new ScrapedProduct
            {
                SkuSource = sku,
                Title = title,
                Price = ParsePrice(priceStr),
                RawHtml = rawHtml,
                SourceUrl = page.Url,
                ScrapedAt = DateTime.UtcNow
            };
            
            var desc = await ExtractTextAsync(page, "characteristics", site, context);
            if (string.IsNullOrWhiteSpace(desc))
            {
                var descSel = new DualSelector { Css = ".product-description, .product__description, .description, #tab-content-description, [id*='description'], .rte, [data-product-description]" };
                var descEl = await SelectorCombinator.QuerySelectorResilientAsync(page, descSel);
                if (descEl != null)
                {
                    desc = (await descEl.TextContentAsync())?.Trim();
                }
            }
            product.Description = desc;
            product.ImageUrl = await ExtractImageAsync(page, site, context);
            
            return product;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DirectStrategy] Error al extraer producto");
            return null;
        }
    }

    private async Task<string?> ExtractTextAsync(IPage page, string selectorKey, SiteProfile site, ScrapeExecutionContext? context = null)
    {
        try
        {
            var selector = SelectorCombinator.GetDualSelector(site, selectorKey);
            if (selector != null)
            {
                var element = await SelectorCombinator.QuerySelectorResilientAsync(page, selector, context?.LogTracker);
                if (element != null)
                {
                    var text = await element.TextContentAsync();
                    return text?.Trim();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[DirectStrategy] Error al extraer texto con selector {Key}", selectorKey);
        }
        
        return null;
    }

    private async Task<string?> ExtractSkuAsync(IPage page, SiteProfile site, ScrapeExecutionContext? context = null)
    {
        try
        {
            var text = await ExtractTextAsync(page, "sku", site, context);
            if (!string.IsNullOrWhiteSpace(text)) return text;

            var selector = SelectorCombinator.GetDualSelector(site, "sku");
            if (selector != null)
            {
                var element = await SelectorCombinator.QuerySelectorResilientAsync(page, selector, context?.LogTracker);
                if (element != null)
                {
                    foreach (var attr in new[] { "data-product-id", "data-sku", "data-id", "id", "value" })
                    {
                        var val = await element.GetAttributeAsync(attr);
                        if (!string.IsNullOrWhiteSpace(val) && val.Length > 1 && !val.Equals("true", StringComparison.OrdinalIgnoreCase))
                        {
                            return val.Trim();
                        }
                    }
                }
            }
        }
        catch { }
        return null;
    }

    private async Task<string?> ExtractImageAsync(IPage page, SiteProfile site, ScrapeExecutionContext? context = null)
    {
        try
        {
            var selector = SelectorCombinator.GetDualSelector(site, "image") ?? new DualSelector { Css = "img" };
            var imgElements = new List<IElementHandle>();

            var primary = await SelectorCombinator.QuerySelectorResilientAsync(page, selector, context?.LogTracker);
            if (primary != null) imgElements.Add(primary);

            var allImgs = await page.QuerySelectorAllAsync(".product-single__photo img, .product__media img, .product-image img, img");
            if (allImgs != null)
            {
                foreach (var img in allImgs)
                {
                    if (!imgElements.Contains(img)) imgElements.Add(img);
                }
            }

            foreach (var imgElement in imgElements)
            {
                var src = await imgElement.GetAttributeAsync("src");
                var dataSrc = await imgElement.GetAttributeAsync("data-src") 
                           ?? await imgElement.GetAttributeAsync("data-original")
                           ?? await imgElement.GetAttributeAsync("data-lazy-src");
                var srcset = await imgElement.GetAttributeAsync("srcset") 
                            ?? await imgElement.GetAttributeAsync("data-srcset");

                string? candidate = null;
                if (!string.IsNullOrWhiteSpace(dataSrc) && !dataSrc.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    candidate = dataSrc;
                }
                else if (!string.IsNullOrWhiteSpace(src) && !src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    candidate = src;
                }
                else if (!string.IsNullOrWhiteSpace(srcset))
                {
                    var entries = srcset.Split(',')
                        .Select(e => e.Trim().Split(' ').FirstOrDefault())
                        .Where(url => !string.IsNullOrWhiteSpace(url) && !url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    
                    candidate = entries.LastOrDefault() ?? entries.FirstOrDefault();
                }

                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    candidate = candidate.Trim();
                    if (candidate.StartsWith("//"))
                    {
                        candidate = "https:" + candidate;
                    }
                    else if (candidate.StartsWith("/"))
                    {
                        candidate = new Uri(new Uri(page.Url), candidate).ToString();
                    }

                    if (Uri.TryCreate(candidate, UriKind.Absolute, out var parsedUri) &&
                        (parsedUri.Scheme == Uri.UriSchemeHttp || parsedUri.Scheme == Uri.UriSchemeHttps))
                    {
                        return candidate;
                    }
                }
            }
        }
        catch { }
        return null;
    }

    private decimal? ParsePrice(string? priceText)
    {
        if (string.IsNullOrWhiteSpace(priceText)) return null;
        
        try
        {
            var cleaned = System.Text.RegularExpressions.Regex.Replace(priceText, @"[^\d,.]", "");
            cleaned = cleaned.Replace(",", "");
            if (decimal.TryParse(cleaned, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var price))
            {
                return price;
            }
        }
        catch { }
        
        return null;
    }
}
