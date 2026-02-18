using Microsoft.Playwright;
using Microsoft.Extensions.Logging;
using ScrapSAE.Core.DTOs;
using System.Net;
using System.Text.RegularExpressions;

namespace ScrapSAE.Infrastructure.Scraping;

/// <summary>
/// Helper class para extraer datos enriquecidos de productos (múltiples imágenes, archivos adjuntos, stock)
/// </summary>
public class EnhancedDataExtractor
{
    private readonly ILogger _logger;

    public EnhancedDataExtractor(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Extrae todas las URLs de imágenes de un producto desde la galería
    /// </summary>
    public async Task<List<string>> ExtractImageGalleryAsync(IPage page, SiteSelectors selectors)
    {
        var images = new List<string>();

        try
        {
            // Intentar con selector de galería si está configurado
            if (!string.IsNullOrWhiteSpace(selectors.ImageGallerySelector))
            {
                var galleryContainer = await page.QuerySelectorAsync(selectors.ImageGallerySelector);
                if (galleryContainer != null)
                {
                    var itemSelector = selectors.ImageGalleryItemSelector ?? "img";
                    var imageElements = await galleryContainer.QuerySelectorAllAsync(itemSelector);
                    
                    foreach (var imgElement in imageElements)
                    {
                        var src = await imgElement.GetAttributeAsync("src");
                        var dataSrc = await imgElement.GetAttributeAsync("data-src");
                        var dataOriginal = await imgElement.GetAttributeAsync("data-original");
                        
                        var imageUrl = src ?? dataSrc ?? dataOriginal;
                        if (!string.IsNullOrWhiteSpace(imageUrl) && IsValidImageUrl(imageUrl))
                        {
                            images.Add(NormalizeUrl(imageUrl, page.Url));
                        }
                    }
                }
            }

            // Fallback intermedio: buscar selectores de galerías típicos (Festo y similares).
            if (images.Count == 0)
            {
                var gallerySelectors = new[]
                {
                    "img[class*='gallery-image']",
                    "[class*='image-gallery'] img",
                    "[class*='article-detail-page-header-gallery'] img",
                    "img[srcset*='media/catalog']"
                };

                foreach (var selector in gallerySelectors)
                {
                    var imageElements = await page.QuerySelectorAllAsync(selector);
                    foreach (var imgElement in imageElements)
                    {
                        var src = await imgElement.GetAttributeAsync("src");
                        var srcSet = await imgElement.GetAttributeAsync("srcset");
                        var dataSrc = await imgElement.GetAttributeAsync("data-src");
                        var imageUrl = src ?? ExtractFirstSrcFromSrcSet(srcSet) ?? dataSrc;

                        if (!string.IsNullOrWhiteSpace(imageUrl) && IsValidImageUrl(imageUrl))
                        {
                            images.Add(NormalizeUrl(imageUrl, page.Url));
                        }
                    }

                    if (images.Count > 0)
                    {
                        break;
                    }
                }
            }

            // Fallback: buscar todas las imágenes en la página que parezcan ser del producto
            if (images.Count == 0)
            {
                var allImages = await page.QuerySelectorAllAsync("img");
                foreach (var img in allImages)
                {
                    var src = await img.GetAttributeAsync("src");
                    var alt = await img.GetAttributeAsync("alt");
                    
                    if (!string.IsNullOrWhiteSpace(src) && IsValidImageUrl(src))
                    {
                        // Filtrar imágenes que probablemente sean del producto (no logos, iconos, etc.)
                        if (IsLikelyProductImage(src, alt))
                        {
                            images.Add(NormalizeUrl(src, page.Url));
                        }
                    }
                }
            }

            // Eliminar duplicados y ordenar
            images = images.Distinct().ToList();
            
            _logger.LogInformation("Extracted {Count} images from gallery", images.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error extracting image gallery");
        }

        return images;
    }

    /// <summary>
    /// Extrae información de stock/inventario de la página
    /// </summary>
    public async Task<int?> ExtractStockAsync(IPage page, SiteSelectors selectors)
    {
        try
        {
            // Intentar con selector específico si está configurado
            if (!string.IsNullOrWhiteSpace(selectors.StockSelector))
            {
                var stockElement = await page.QuerySelectorAsync(selectors.StockSelector);
                if (stockElement != null)
                {
                    var stockText = await stockElement.InnerTextAsync();
                    return ExtractNumberFromText(stockText);
                }
            }

            // Fallback: buscar patrones comunes de stock en el HTML
            var bodyText = await page.InnerTextAsync("body");
            
            // Patrones comunes en español e inglés
            var patterns = new[]
            {
                @"(?:stock|inventario|disponible|available|en\s+existencia)[\s:]*(\d+)",
                @"(\d+)\s+(?:unidades?|units?|piezas?|pieces?)\s+(?:disponibles?|available)",
                @"(?:quedan?|remaining)[\s:]*(\d+)",
                @"(?:cantidad|quantity)[\s:]*(\d+)"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(bodyText, pattern, RegexOptions.IgnoreCase);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var stock))
                {
                    _logger.LogInformation("Extracted stock: {Stock} using pattern: {Pattern}", stock, pattern);
                    return stock;
                }
            }

            // Si no encontramos número, buscar indicadores booleanos
            if (Regex.IsMatch(bodyText, @"en\s+stock|disponible|available|in\s+stock", RegexOptions.IgnoreCase))
            {
                _logger.LogInformation("Product appears to be in stock (no specific quantity found)");
                return 1; // Indicar que hay stock sin cantidad específica
            }

            if (Regex.IsMatch(bodyText, @"agotado|out\s+of\s+stock|sin\s+stock|no\s+disponible", RegexOptions.IgnoreCase))
            {
                _logger.LogInformation("Product appears to be out of stock");
                return 0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error extracting stock information");
        }

        return null;
    }

    /// <summary>
    /// Extrae archivos adjuntos (PDFs, manuales, fichas técnicas)
    /// </summary>
    public async Task<List<ProductAttachment>> ExtractAttachmentsAsync(IPage page, SiteSelectors selectors)
    {
        var attachmentsByUrl = new Dictionary<string, ProductAttachment>(StringComparer.OrdinalIgnoreCase);

        try
        {
            await TryActivateDownloadsTabAsync(page);

            // Metodo 1: enlaces visibles en DOM (selector configurable + fallback robusto).
            string linkSelector = selectors.AttachmentLinkSelector ??
                "a[href*='.pdf'], a[href*='.zip'], a[href*='.docx'], a[href*='.doc'], " +
                "a[href*='download'], a[href*='manual'], a[href*='datasheet'], a[href*='support']";
            await CollectAttachmentsFromAnchorsAsync(page, linkSelector, attachmentsByUrl);

            // Metodo 2: atributos data-* usados por componentes React/SPA.
            await CollectAttachmentsFromDataAttributesAsync(page, attachmentsByUrl);

            // Metodo 3: búsqueda en HTML/script embebido (cuando los links no se renderizan como <a>).
            await CollectAttachmentsFromHtmlAsync(page, attachmentsByUrl);

            _logger.LogInformation("Extracted {Count} attachments with hybrid flow", attachmentsByUrl.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error extracting attachments");
        }

        return attachmentsByUrl.Values
            .OrderBy(a => a.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Extrae la moneda del precio
    /// </summary>
    public async Task<string?> ExtractCurrencyAsync(IPage page)
    {
        try
        {
            var bodyText = await page.InnerTextAsync("body");
            
            // Buscar símbolos de moneda comunes
            if (bodyText.Contains("$") || bodyText.Contains("MXN"))
                return "MXN";
            if (bodyText.Contains("USD") || bodyText.Contains("US$"))
                return "USD";
            if (bodyText.Contains("€") || bodyText.Contains("EUR"))
                return "EUR";
            if (bodyText.Contains("£") || bodyText.Contains("GBP"))
                return "GBP";

            // Inferir por dominio
            var url = page.Url.ToLower();
            if (url.Contains(".mx"))
                return "MXN";
            if (url.Contains(".com"))
                return "USD";
            if (url.Contains(".eu") || url.Contains(".de") || url.Contains(".fr"))
                return "EUR";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error extracting currency");
        }

        return null;
    }

    // Métodos auxiliares privados

    private bool IsValidImageUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        var lowerUrl = url.ToLower();
        return lowerUrl.Contains(".jpg") || lowerUrl.Contains(".jpeg") || 
               lowerUrl.Contains(".png") || lowerUrl.Contains(".webp") ||
               lowerUrl.Contains(".gif") || lowerUrl.Contains("/image/");
    }

    private bool IsLikelyProductImage(string src, string? alt)
    {
        var lowerSrc = src.ToLower();
        var lowerAlt = alt?.ToLower() ?? "";

        // Excluir logos, iconos, banners
        if (lowerSrc.Contains("logo") || lowerSrc.Contains("icon") || 
            lowerSrc.Contains("banner") || lowerSrc.Contains("sprite") ||
            lowerSrc.Contains("placeholder") || lowerSrc.Contains("avatar"))
            return false;

        // Incluir si contiene palabras relacionadas con productos
        if (lowerSrc.Contains("product") || lowerSrc.Contains("item") ||
            lowerAlt.Contains("product") || lowerAlt.Contains("item"))
            return true;

        // Incluir si la imagen es suficientemente grande (heurística basada en URL)
        if (Regex.IsMatch(lowerSrc, @"\d{3,}x\d{3,}"))
            return true;

        return true; // Por defecto incluir
    }

    private string NormalizeUrl(string url, string baseUrl)
    {
        if (url.StartsWith("http://") || url.StartsWith("https://"))
            return url;

        if (url.StartsWith("//"))
            return "https:" + url;

        if (url.StartsWith("/"))
        {
            var uri = new Uri(baseUrl);
            return $"{uri.Scheme}://{uri.Host}{url}";
        }

        return new Uri(new Uri(baseUrl), url).ToString();
    }

    private int? ExtractNumberFromText(string text)
    {
        var match = Regex.Match(text, @"\d+");
        if (match.Success && int.TryParse(match.Value, out var number))
            return number;
        return null;
    }

    private string? DetermineFileType(string url)
    {
        var lowerUrl = url.ToLower();
        if (lowerUrl.Contains(".pdf") || lowerUrl.Contains("datasheet"))
            return "application/pdf";
        if (lowerUrl.Contains(".zip"))
            return "application/zip";
        if (lowerUrl.Contains(".docx"))
            return "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
        if (lowerUrl.Contains(".doc"))
            return "application/msword";
        if (lowerUrl.Contains(".xlsx"))
            return "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        if (lowerUrl.Contains(".xls"))
            return "application/vnd.ms-excel";
        return null;
    }

    private bool IsRelevantAttachment(string url, string text)
    {
        var lowerUrl = url.ToLower();
        var lowerText = text.ToLower();

        // Incluir PDFs, ZIPs, documentos
        if (lowerUrl.Contains(".pdf") || lowerUrl.Contains(".zip") || 
            lowerUrl.Contains(".docx") || lowerUrl.Contains(".doc") ||
            lowerUrl.Contains("datasheet") || lowerUrl.Contains("download-document"))
            return true;

        // Incluir si el texto sugiere que es un manual o ficha técnica
        if (lowerText.Contains("manual") || lowerText.Contains("datasheet") ||
            lowerText.Contains("ficha") || lowerText.Contains("catálogo") ||
            lowerText.Contains("especificaciones") || lowerText.Contains("technical") ||
            lowerText.Contains("hoja de datos"))
            return true;

        return false;
    }

    private async Task TryActivateDownloadsTabAsync(IPage page)
    {
        try
        {
            var tabCandidates = new[]
            {
                "li.js-support-portal-button",
                "[data-onsite-click-event-data*='support downloads']",
                "[class*='product-details-tabs__list-item']"
            };

            foreach (var selector in tabCandidates)
            {
                var tabs = await page.QuerySelectorAllAsync(selector);
                foreach (var tab in tabs)
                {
                    var text = (await tab.InnerTextAsync())?.Trim() ?? string.Empty;
                    if (!text.Contains("descarga", StringComparison.OrdinalIgnoreCase) &&
                        !text.Contains("download", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    try
                    {
                        await tab.ClickAsync();
                        await Task.Delay(500);
                        return;
                    }
                    catch
                    {
                        // Continuar con el siguiente candidato.
                    }
                }
            }
        }
        catch
        {
            // Esta mejora es best-effort.
        }
    }

    private async Task CollectAttachmentsFromAnchorsAsync(
        IPage page,
        string linkSelector,
        IDictionary<string, ProductAttachment> output)
    {
        var links = await page.QuerySelectorAllAsync(linkSelector);
        foreach (var link in links)
        {
            var href = await link.GetAttributeAsync("href");
            var text = await link.InnerTextAsync();
            TryAddAttachmentCandidate(output, href, text, page.Url);
        }

        // Barrido amplio en todos los anchors por si cambian clases/selectores.
        var allLinks = await page.QuerySelectorAllAsync("a[href]");
        foreach (var link in allLinks)
        {
            var href = await link.GetAttributeAsync("href");
            var text = await link.InnerTextAsync();
            TryAddAttachmentCandidate(output, href, text, page.Url);
        }
    }

    private async Task CollectAttachmentsFromDataAttributesAsync(
        IPage page,
        IDictionary<string, ProductAttachment> output)
    {
        var candidates = await page.EvaluateAsync<List<string>>(
            @"() => {
                const attrs = ['data-iframe-src', 'data-file-url', 'data-download-url', 'data-href'];
                const result = [];
                const nodes = Array.from(document.querySelectorAll('*'));
                for (const node of nodes) {
                    for (const attr of attrs) {
                        const value = node.getAttribute(attr);
                        if (value) result.push(value);
                    }
                }
                return result;
            }");

        foreach (var candidate in candidates)
        {
            TryAddAttachmentCandidate(output, candidate, null, page.Url);
        }
    }

    private async Task CollectAttachmentsFromHtmlAsync(
        IPage page,
        IDictionary<string, ProductAttachment> output)
    {
        var html = await page.ContentAsync();
        if (string.IsNullOrWhiteSpace(html))
        {
            return;
        }

        // URLs absolutas y relativas (incluyendo rutas sin extensión directa como /download-document/datasheet/197391).
        var patterns = new[]
        {
            @"https?:\/\/[^\s""'<>]+(?:\.pdf|\.zip|\.docx?|\.xlsx?|download-document\/[^\s""'<>]+|datasheet\/\d+)[^\s""'<>]*",
            @"\/[^\s""'<>]+(?:\.pdf|\.zip|\.docx?|\.xlsx?|download-document\/[^\s""'<>]+|datasheet\/\d+)[^\s""'<>]*",
            @"https?:\\\/\\\/[^\s""'<>]+(?:\.pdf|\.zip|\.docx?|\.xlsx?|download-document\\\/[^\s""'<>]+|datasheet\\\/\d+)[^\s""'<>]*"
        };

        foreach (var pattern in patterns)
        {
            foreach (Match match in Regex.Matches(html, pattern, RegexOptions.IgnoreCase))
            {
                if (!match.Success)
                {
                    continue;
                }

                var rawUrl = match.Value
                    .Replace("\\/", "/")
                    .Replace("\\u002F", "/", StringComparison.OrdinalIgnoreCase);
                rawUrl = WebUtility.HtmlDecode(rawUrl);
                TryAddAttachmentCandidate(output, rawUrl, null, page.Url);
            }
        }
    }

    private void TryAddAttachmentCandidate(
        IDictionary<string, ProductAttachment> output,
        string? rawUrl,
        string? rawText,
        string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return;
        }

        var normalizedInput = rawUrl.Trim();
        if (normalizedInput.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
            normalizedInput.StartsWith("#", StringComparison.Ordinal))
        {
            return;
        }

        string fullUrl;
        try
        {
            fullUrl = NormalizeUrl(normalizedInput, baseUrl);
        }
        catch
        {
            return;
        }

        var text = rawText?.Trim() ?? string.Empty;
        if (!IsRelevantAttachment(fullUrl, text))
        {
            return;
        }

        if (output.ContainsKey(fullUrl))
        {
            return;
        }

        output[fullUrl] = new ProductAttachment
        {
            FileName = string.IsNullOrWhiteSpace(text) ? Path.GetFileName(fullUrl) : text,
            FileUrl = fullUrl,
            FileType = DetermineFileType(fullUrl)
        };
    }

    private static string? ExtractFirstSrcFromSrcSet(string? srcSet)
    {
        if (string.IsNullOrWhiteSpace(srcSet))
        {
            return null;
        }

        var firstEntry = srcSet.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstEntry))
        {
            return null;
        }

        var urlToken = firstEntry.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(urlToken) ? null : urlToken;
    }
}
