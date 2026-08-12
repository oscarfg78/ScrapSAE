using Microsoft.Playwright;
using ScrapSAE.Core.DTOs;

namespace ScrapSAE.Infrastructure.Services;

/// <summary>
/// Motor de búsqueda y extracción de datos de una sola fuente objetivo.
/// Soporta dos modos: DOM Input (escribe en campo de búsqueda) y Query Param (URL template).
///
/// TOLERANCIA A FALLOS: Cada operación está envuelta en try/catch individual.
/// Un fallo en cualquier etapa devuelve TargetScrapeResult.NotFound sin propagar excepción.
/// </summary>
public class DualTargetSearchEngine
{
    // Timeouts configurables
    private const int PageLoadTimeoutMs     = 15_000;
    private const int SelectorTimeoutMs     = 10_000;
    private const int DetailLoadTimeoutMs   = 20_000;
    private const int ExtractTimeoutMs      = 8_000;

    /// <summary>
    /// Ejecuta búsqueda y extracción de detalle para un SKU en la fuente configurada.
    /// </summary>
    public async Task<TargetScrapeResult> SearchAndExtractAsync(
        IPage page,
        TargetSearchConfig config,
        string sku,
        CancellationToken cancellationToken = default)
    {
        var label = config.Label;

        if (config.SearchMode == SearchMode.DirectDetail)
        {
            var template = string.IsNullOrWhiteSpace(config.SearchUrlTemplate)
                ? config.BaseSearchUrl
                : config.SearchUrlTemplate;

            var directUrl = template.Replace("[sku]", Uri.EscapeDataString(sku), StringComparison.OrdinalIgnoreCase)
                                    .Replace("{sku}", Uri.EscapeDataString(sku), StringComparison.OrdinalIgnoreCase);

            try
            {
                await page.GotoAsync(directUrl, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = DetailLoadTimeoutMs
                });
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return TargetScrapeResult.NotFound(label, sku, SkipReason.DetailNavigationFailed, ex.Message);
            }
        }
        else
        {
            // ── Fase 1: Navegación / Búsqueda ─────────────────────────────────────
            string? firstCardHref;
            try
            {
                firstCardHref = await NavigateAndFindFirstCardAsync(page, config, sku, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return TargetScrapeResult.NotFound(label, sku, SkipReason.NoSearchResults, ex.Message);
            }

            if (firstCardHref == null)
                return TargetScrapeResult.NotFound(label, sku, SkipReason.NoSearchResults);

            // ── Fase 2: Navegación a detalle ──────────────────────────────────────
            try
            {
                var detailUrl = ResolveUrl(config.BaseSearchUrl, firstCardHref);
                await page.GotoAsync(detailUrl, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = DetailLoadTimeoutMs
                });
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return TargetScrapeResult.NotFound(label, sku, SkipReason.DetailNavigationFailed, ex.Message);
            }
        }

        var currentUrl = page.Url;

        // ── Fase 3: Extracción de campos ──────────────────────────────────────
        var retailPrice = await TryExtractPriceAsync(page, config.Selectors.RetailPriceSelector);
        var imageUrls   = await TryExtractImagesAsync(page, config.Selectors.ImageGallerySelector);
        var title       = await TryExtractTextAsync(page, config.Selectors.TitleSelector);
        var description = await TryExtractDescriptionAsync(page, config.Selectors.DescriptionSelector);
        var attributes  = await TryExtractAttributesAsync(page, config.Selectors.AttributesSelector);

        if (!string.IsNullOrWhiteSpace(config.Selectors.CategorySelector))
        {
            var category = await TryExtractTextAsync(page, config.Selectors.CategorySelector);
            if (!string.IsNullOrWhiteSpace(category))
            {
                attributes["Categoria"] = category;
            }
        }

        bool hasAnyData = retailPrice != null || 
                          imageUrls.Count > 0 || 
                          !string.IsNullOrWhiteSpace(title) || 
                          !string.IsNullOrWhiteSpace(description) || 
                          attributes.Count > 0;

        if (!hasAnyData)
        {
            return TargetScrapeResult.NotFound(label, sku, SkipReason.NoSearchResults, "Los selectores no extrajeron ninguna información útil.");
        }

        if (config.Selectors.IncludeSourceUrlInAttributes)
        {
            attributes["UrlOrigen"] = currentUrl;
        }

        return new TargetScrapeResult
        {
            TargetLabel        = label,
            Sku                = sku,
            Status             = ScrapingResultStatus.Found,
            RetailPrice        = retailPrice,
            ImageUrls          = imageUrls,
            Title              = title,
            Description        = description,
            SourceDetailUrl    = currentUrl,
            OptionalAttributes = attributes
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Navegación
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<string?> NavigateAndFindFirstCardAsync(
        IPage page,
        TargetSearchConfig config,
        string sku,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (config.SearchMode == SearchMode.QueryParam)
        {
            await NavigateQueryParamAsync(page, config, sku);
        }
        else
        {
            await NavigateDomInputAsync(page, config, sku);
        }

        var selectors = config.Selectors;

        // Determinar qué selector esperar
        var waitSelector = selectors.RequireFirstResultCard && !string.IsNullOrWhiteSpace(selectors.FirstResultCardSelector)
            ? selectors.FirstResultCardSelector
            : selectors.DetailLinkSelector;

        if (string.IsNullOrWhiteSpace(waitSelector))
            return null; // No hay forma de encontrar el resultado

        await page.WaitForSelectorAsync(waitSelector, new PageWaitForSelectorOptions
        {
            State   = WaitForSelectorState.Visible,
            Timeout = SelectorTimeoutMs
        });

        // Obtener href del link de detalle (puede estar en el card o ser el card mismo)
        var linkSelector = string.IsNullOrWhiteSpace(selectors.DetailLinkSelector)
            ? waitSelector
            : selectors.DetailLinkSelector;

        var element = await page.QuerySelectorAsync(linkSelector);
        if (element == null) return null;

        return await element.GetAttributeAsync("href");
    }

    private async Task NavigateQueryParamAsync(IPage page, TargetSearchConfig config, string sku)
    {
        var url = config.SearchUrlTemplate.Replace("{sku}", Uri.EscapeDataString(sku));
        if (string.IsNullOrWhiteSpace(url))
            url = $"{config.BaseSearchUrl.TrimEnd('/')}?q={Uri.EscapeDataString(sku)}";

        await page.GotoAsync(url, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = PageLoadTimeoutMs
        });
    }

    private async Task NavigateDomInputAsync(IPage page, TargetSearchConfig config, string sku)
    {
        var selectors = config.Selectors;

        // 1. Cargar página base de búsqueda
        await page.GotoAsync(config.BaseSearchUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = PageLoadTimeoutMs
        });

        // 2. Esperar campo de búsqueda
        if (string.IsNullOrWhiteSpace(selectors.SearchInputSelector))
            throw new InvalidOperationException("SearchInputSelector no configurado para modo DOM Input.");

        await page.WaitForSelectorAsync(selectors.SearchInputSelector, new PageWaitForSelectorOptions
        {
            Timeout = SelectorTimeoutMs
        });

        // 3. Escribir SKU
        await page.FillAsync(selectors.SearchInputSelector, sku);
        await Task.Delay(300); // pequeño delay humanizado

        // 4. Submit (click botón o Enter)
        if (!string.IsNullOrWhiteSpace(selectors.SearchSubmitSelector))
        {
            await page.ClickAsync(selectors.SearchSubmitSelector);
        }
        else
        {
            await page.Keyboard.PressAsync("Enter");
        }

        // 5. Esperar carga de resultados
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new PageWaitForLoadStateOptions
        {
            Timeout = PageLoadTimeoutMs
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Extracción de campos
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<decimal?> TryExtractPriceAsync(IPage page, string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector)) return null;
        try
        {
            var el = await page.QuerySelectorAsync(selector);
            if (el == null) return null;
            var text = await el.InnerTextAsync();
            return PriceParser.TryParse(text);
        }
        catch { return null; }
    }

    private async Task<List<string>> TryExtractImagesAsync(IPage page, string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector)) return new();
        try
        {
            var elements = await page.QuerySelectorAllAsync(selector);
            var urls = new List<string>();
            foreach (var el in elements)
            {
                var src = await el.GetAttributeAsync("src")
                       ?? await el.GetAttributeAsync("data-src")
                       ?? await el.GetAttributeAsync("data-lazy-src");
                if (!string.IsNullOrWhiteSpace(src))
                {
                    if (Uri.TryCreate(new Uri(page.Url), src, out var fullUri))
                        urls.Add(fullUri.AbsoluteUri);
                    else
                        urls.Add(src);
                }
            }
            return urls;
        }
        catch { return new(); }
    }

    private async Task<string?> TryExtractTextAsync(IPage page, string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector)) return null;
        try
        {
            var el = await page.QuerySelectorAsync(selector);
            return el == null ? null : (await el.InnerTextAsync()).Trim();
        }
        catch { return null; }
    }

    private async Task<string?> TryExtractDescriptionAsync(IPage page, string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector)) return null;

        try
        {
            var element = await page.QuerySelectorAsync(selector);
            if (element == null) return null;

            var tagName = await element.EvaluateAsync<string>("el => el.tagName.toLowerCase()");

            if (tagName == "ul" || tagName == "ol")
            {
                var items = await element.QuerySelectorAllAsync("li");
                var listValues = new List<string>();
                foreach (var li in items)
                {
                    var text = (await li.InnerTextAsync()).Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                        listValues.Add(text);
                }
                return listValues.Count > 0 ? string.Join(", ", listValues) : null;
            }
            else
            {
                var rawText = (await element.InnerTextAsync()).Trim();
                if (string.IsNullOrWhiteSpace(rawText)) return null;

                var lines = rawText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                   .Select(l => l.Trim())
                                   .Where(l => !string.IsNullOrWhiteSpace(l));
                return string.Join(", ", lines);
            }
        }
        catch { return null; }
    }

    private async Task<Dictionary<string, string>> TryExtractAttributesAsync(IPage page, string? selector)
    {
        var result = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(selector)) return result;

        try
        {
            var element = await page.QuerySelectorAsync(selector);
            if (element == null) return result;

            var tagName = await element.EvaluateAsync<string>("el => el.tagName.toLowerCase()");

            if (tagName == "table")
            {
                var rows = await element.QuerySelectorAllAsync("tr");
                int rowIdx = 1;
                foreach (var tr in rows)
                {
                    var th = await tr.QuerySelectorAsync("th");
                    var tdList = await tr.QuerySelectorAllAsync("td");

                    if (th != null && tdList.Count > 0)
                    {
                        var key = (await th.InnerTextAsync()).Trim();
                        var val = (await tdList[0].InnerTextAsync()).Trim();
                        if (!string.IsNullOrWhiteSpace(key))
                            result[key] = val;
                    }
                    else if (tdList.Count >= 2)
                    {
                        var key = (await tdList[0].InnerTextAsync()).Trim();
                        var val = (await tdList[1].InnerTextAsync()).Trim();
                        if (!string.IsNullOrWhiteSpace(key))
                            result[key] = val;
                    }
                    else if (tdList.Count == 1)
                    {
                        var val = (await tdList[0].InnerTextAsync()).Trim();
                        if (!string.IsNullOrWhiteSpace(val))
                            result[$"Atributo_{rowIdx}"] = val;
                    }
                    rowIdx++;
                }
            }
            else
            {
                var items = await element.QuerySelectorAllAsync("li");
                if (items.Count == 0)
                    items = await element.QuerySelectorAllAsync("tr, div, p");

                int itemIdx = 1;
                foreach (var item in items)
                {
                    var text = (await item.InnerTextAsync()).Trim();
                    if (string.IsNullOrWhiteSpace(text)) continue;

                    if (text.Contains(":"))
                    {
                        var parts = text.Split(':', 2);
                        var key = parts[0].Trim();
                        var val = parts[1].Trim();
                        if (!string.IsNullOrWhiteSpace(key))
                            result[key] = val;
                    }
                    else
                    {
                        result[$"Atributo_{itemIdx}"] = text;
                    }
                    itemIdx++;
                }
            }
        }
        catch { }

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static string ResolveUrl(string baseUrl, string href)
    {
        if (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            href.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return href;

        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) &&
            Uri.TryCreate(baseUri, href, out var resolved))
            return resolved.ToString();

        return href;
    }
}
