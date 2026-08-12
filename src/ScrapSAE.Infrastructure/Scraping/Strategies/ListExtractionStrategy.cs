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

public class ListExtractionStrategy : IScrapingStrategy
{
    private readonly ILogger<ListExtractionStrategy> _logger;
    private readonly ITelemetryService _telemetryService;

    public string StrategyName => "List";

    public ListExtractionStrategy(
        ILogger<ListExtractionStrategy> logger,
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
            _logger.LogInformation("[ListStrategy] Intentando extracción de lista en {Url}", page.Url);
            context?.LogTracker?.AddLog("ListStrategy", details: $"Iniciando extracción de lista en {page.Url}");

            var itemSelector = SelectorCombinator.GetDualSelector(site, "productCard");
            var containerSelector = SelectorCombinator.GetDualSelector(site, "productContainer");

            IReadOnlyList<IElementHandle> productElements = Array.Empty<IElementHandle>();

            var waitTimeout = (itemSelector?.Css != null && itemSelector.Css.Contains("snize")) ||
                              (containerSelector?.Css != null && containerSelector.Css.Contains("snize")) ? 15000 : 12000;

            // 1. Try querying productCard selector directly on the page first
            if (itemSelector != null)
            {
                if (!string.IsNullOrWhiteSpace(itemSelector.Css))
                {
                    try { await page.WaitForSelectorAsync(itemSelector.Css, new PageWaitForSelectorOptions { Timeout = waitTimeout }); } catch { }
                }
                productElements = await SelectorCombinator.QuerySelectorAllResilientAsync(page, itemSelector, context?.LogTracker);
            }

            // 2. If not found, try container element and search inside it
            if (productElements.Count == 0 && containerSelector != null)
            {
                if (!string.IsNullOrWhiteSpace(containerSelector.Css))
                {
                    try { await page.WaitForSelectorAsync(containerSelector.Css, new PageWaitForSelectorOptions { Timeout = waitTimeout }); } catch { }
                }

                var container = await SelectorCombinator.QuerySelectorResilientAsync(page, containerSelector, context?.LogTracker);
                if (container != null)
                {
                    if (itemSelector != null)
                    {
                        productElements = await SelectorCombinator.QuerySelectorAllResilientAsync(container, itemSelector, context?.LogTracker);
                    }

                    if (productElements.Count == 0)
                    {
                        productElements = await SelectorCombinator.QuerySelectorAllResilientAsync(page, containerSelector, context?.LogTracker);
                    }
                }
            }

            // 3. Fallback to generic product card selectors on page if still empty
            if (productElements.Count == 0)
            {
                var genericCardSelector = new DualSelector { Css = ".grid-item, .product-card, .squama-item, article, li[class*='product']" };
                productElements = await SelectorCombinator.QuerySelectorAllResilientAsync(page, genericCardSelector, context?.LogTracker);
            }

            _logger.LogInformation("[ListStrategy] Encontrados {Count} elementos de producto", productElements.Count);
            context?.LogTracker?.AddLog("ListStrategy", details: $"Encontrados {productElements.Count} elementos de producto");

            foreach (var productElement in productElements)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var product = await ExtractProductFromElementAsync(productElement, site, page.Url, context);
                if (SelectorCombinator.IsValidProduct(product))
                {
                    products.Add(product!);
                }
            }

            // Enrichment phase for missing descriptions or image URLs
            foreach (var prod in products.Take(5))
            {
                if ((string.IsNullOrEmpty(prod.Description) || string.IsNullOrEmpty(prod.ImageUrl)) &&
                    !string.IsNullOrEmpty(prod.SourceUrl) && prod.SourceUrl != page.Url)
                {
                    try
                    {
                        context?.LogTracker?.AddLog("ListStrategy", details: $"Enriqueciendo detalle para {prod.Title}: {prod.SourceUrl}");
                        var newPage = await page.Context.NewPageAsync();
                        await newPage.GotoAsync(prod.SourceUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 10000 });

                        var directStrategyLogger = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance.CreateLogger<DirectExtractionStrategy>();
                        var directStrategy = new DirectExtractionStrategy(directStrategyLogger, _telemetryService);
                        var detailProduct = await directStrategy.ExecuteAsync(newPage, site, executionId, context, cancellationToken);

                        if (detailProduct != null && detailProduct.Count > 0)
                        {
                            var enriched = detailProduct.First();
                            prod.Description = string.IsNullOrEmpty(prod.Description) ? enriched.Description : prod.Description;
                            prod.ImageUrl = string.IsNullOrEmpty(prod.ImageUrl) ? enriched.ImageUrl : prod.ImageUrl;
                            prod.SkuSource = string.IsNullOrEmpty(prod.SkuSource) ? enriched.SkuSource : prod.SkuSource;
                        }
                        await newPage.CloseAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[ListStrategy] Error enriqueciendo detalle para {Url}", prod.SourceUrl);
                    }
                }
            }

            if (products.Any())
            {
                await _telemetryService.RecordSuccessAsync(executionId, "Extracción de lista exitosa", page.Url);
            }
            else
            {
                await _telemetryService.RecordFailureAsync(new DiagnosticPackage
                {
                    ExecutionId = executionId,
                    Url = page.Url,
                    FailureType = "ListExtractionFailed"
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ListStrategy] Error al ejecutar estrategia de lista");
            context?.LogTracker?.AddLog("ListStrategy", error: $"Excepción en ListStrategy: {ex.Message}");
            await _telemetryService.RecordFailureAsync(new DiagnosticPackage
            {
                ExecutionId = executionId,
                Url = page.Url,
                FailureType = "ListExtractionException"
            });
        }

        return products;
    }

    private async Task<ScrapedProduct?> ExtractProductFromElementAsync(
        IElementHandle element,
        SiteProfile site,
        string sourceUrl,
        ScrapeExecutionContext? context = null)
    {
        try
        {
            var title = await ExtractTextFromElementAsync(element, "name", site, context);
            var sku = await ExtractSkuFromElementAsync(element, site, context);
            var price = await ExtractPriceFromElementAsync(element, site, context);
            var image = await ExtractImageFromElementAsync(element, site, sourceUrl, context);
            var desc = await ExtractTextFromElementAsync(element, "characteristics", site, context);

            var product = new ScrapedProduct
            {
                Title = title,
                SkuSource = sku,
                Price = price,
                ImageUrl = image,
                Description = desc
            };

            if (string.IsNullOrWhiteSpace(product.Title))
            {
                var titleSel = new DualSelector { Css = "h1, h2, h3, .title, .name, .item-title, a" };
                var titleEl = await SelectorCombinator.QuerySelectorResilientAsync(element, titleSel);
                if (titleEl != null)
                {
                    product.Title = (await titleEl.TextContentAsync())?.Trim();
                }
            }

            var linkSelector = SelectorCombinator.GetDualSelector(site, "detailLink") ?? new DualSelector { Css = "a" };
            var linkElement = await SelectorCombinator.QuerySelectorResilientAsync(element, linkSelector);
            if (linkElement != null)
            {
                var href = await linkElement.GetAttributeAsync("href");
                if (!string.IsNullOrEmpty(href))
                {
                    product.SourceUrl = href.StartsWith("http") ? href : new Uri(new Uri(sourceUrl), href).ToString();
                }
            }
            else
            {
                product.SourceUrl = sourceUrl;
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
        SiteProfile site,
        ScrapeExecutionContext? context = null)
    {
        try
        {
            var selector = SelectorCombinator.GetDualSelector(site, selectorKey);
            if (selector != null)
            {
                var childElement = await SelectorCombinator.QuerySelectorResilientAsync(element, selector);
                if (childElement != null)
                {
                    var text = await childElement.TextContentAsync();
                    return text?.Trim();
                }
            }
        }
        catch { }
        return null;
    }

    private async Task<string?> ExtractSkuFromElementAsync(
        IElementHandle element,
        SiteProfile site,
        ScrapeExecutionContext? context = null)
    {
        try
        {
            var text = await ExtractTextFromElementAsync(element, "sku", site, context);
            if (!string.IsNullOrWhiteSpace(text)) return text;

            var selector = SelectorCombinator.GetDualSelector(site, "sku");
            var targetEl = selector != null ? (await SelectorCombinator.QuerySelectorResilientAsync(element, selector) ?? element) : element;

            foreach (var attrName in new[] { "data-product-id", "data-sku", "data-id", "id", "value" })
            {
                try
                {
                    var val = await targetEl.GetAttributeAsync(attrName);
                    if (!string.IsNullOrWhiteSpace(val) && val.Length > 1 && !val.Equals("true", StringComparison.OrdinalIgnoreCase))
                    {
                        return val.Trim();
                    }
                }
                catch { }
            }

            if (targetEl != element)
            {
                foreach (var attrName in new[] { "data-product-id", "data-sku", "data-id", "id" })
                {
                    try
                    {
                        var val = await element.GetAttributeAsync(attrName);
                        if (!string.IsNullOrWhiteSpace(val) && val.Length > 1) return val.Trim();
                    }
                    catch { }
                }
            }
        }
        catch { }
        return null;
    }

    private async Task<decimal?> ExtractPriceFromElementAsync(
        IElementHandle element,
        SiteProfile site,
        ScrapeExecutionContext? context = null)
    {
        try
        {
            var priceText = await ExtractTextFromElementAsync(element, "price", site, context);
            if (string.IsNullOrWhiteSpace(priceText))
            {
                var priceSel = new DualSelector { Css = ".price, .price--final, .price-item, [data-product-price], .money, span[class*='price']" };
                var priceEl = await SelectorCombinator.QuerySelectorResilientAsync(element, priceSel);
                if (priceEl != null)
                {
                    priceText = await priceEl.TextContentAsync();
                }
            }

            if (!string.IsNullOrEmpty(priceText))
            {
                return CleanPriceText(priceText);
            }
        }
        catch { }
        return null;
    }

    private async Task<string?> ExtractImageFromElementAsync(
        IElementHandle element,
        SiteProfile site,
        string sourceUrl,
        ScrapeExecutionContext? context = null)
    {
        try
        {
            var selector = SelectorCombinator.GetDualSelector(site, "image") ?? new DualSelector { Css = "img" };
            var imgElements = new List<IElementHandle>();

            var primary = await SelectorCombinator.QuerySelectorResilientAsync(element, selector);
            if (primary != null) imgElements.Add(primary);

            var allImgs = await element.QuerySelectorAllAsync("modal-opener img, slider-component img, .media img, [class*='media'] img, picture img, source, img, [data-src]");
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
                           ?? await imgElement.GetAttributeAsync("data-lazy-src")
                           ?? await imgElement.GetAttributeAsync("data-src-large");
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
                        candidate = new Uri(new Uri(sourceUrl), candidate).ToString();
                    }

                    if (Uri.TryCreate(candidate, UriKind.Absolute, out var parsedUri) &&
                        (parsedUri.Scheme == Uri.UriSchemeHttp || parsedUri.Scheme == Uri.UriSchemeHttps))
                    {
                        return candidate;
                    }
                }
            }

            var style = await element.GetAttributeAsync("style");
            if (!string.IsNullOrWhiteSpace(style) && style.Contains("url("))
            {
                var match = System.Text.RegularExpressions.Regex.Match(style, @"url\(['""]?(.*?)['""]?\)");
                if (match.Success)
                {
                    var bgUrl = match.Groups[1].Value.Trim();
                    if (bgUrl.StartsWith("//")) bgUrl = "https:" + bgUrl;
                    else if (bgUrl.StartsWith("/")) bgUrl = new Uri(new Uri(sourceUrl), bgUrl).ToString();
                    return bgUrl;
                }
            }
        }
        catch { }
        return null;
    }

    private decimal? CleanPriceText(string priceText)
    {
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
