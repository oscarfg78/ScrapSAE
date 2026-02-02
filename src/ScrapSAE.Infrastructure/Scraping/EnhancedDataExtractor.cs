using Microsoft.Playwright;
using Microsoft.Extensions.Logging;
using ScrapSAE.Core.DTOs;
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
        var attachments = new List<ProductAttachment>();

        try
        {
            // Selector específico si está configurado
            string linkSelector = selectors.AttachmentLinkSelector ?? "a[href*='.pdf'], a[href*='.zip'], a[href*='.docx'], a[href*='download'], a[href*='manual'], a[href*='datasheet']";
            
            var links = await page.QuerySelectorAllAsync(linkSelector);
            
            foreach (var link in links)
            {
                var href = await link.GetAttributeAsync("href");
                var text = await link.InnerTextAsync();
                
                if (string.IsNullOrWhiteSpace(href))
                    continue;

                var fullUrl = NormalizeUrl(href, page.Url);
                
                // Determinar tipo de archivo
                var fileType = DetermineFileType(fullUrl);
                
                // Filtrar solo archivos relevantes
                if (IsRelevantAttachment(fullUrl, text))
                {
                    attachments.Add(new ProductAttachment
                    {
                        FileName = text.Trim(),
                        FileUrl = fullUrl,
                        FileType = fileType
                    });
                }
            }

            _logger.LogInformation("Extracted {Count} attachments", attachments.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error extracting attachments");
        }

        return attachments;
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
        if (lowerUrl.Contains(".pdf"))
            return "pdf";
        if (lowerUrl.Contains(".zip"))
            return "zip";
        if (lowerUrl.Contains(".docx") || lowerUrl.Contains(".doc"))
            return "doc";
        if (lowerUrl.Contains(".xlsx") || lowerUrl.Contains(".xls"))
            return "excel";
        return null;
    }

    private bool IsRelevantAttachment(string url, string text)
    {
        var lowerUrl = url.ToLower();
        var lowerText = text.ToLower();

        // Incluir PDFs, ZIPs, documentos
        if (lowerUrl.Contains(".pdf") || lowerUrl.Contains(".zip") || 
            lowerUrl.Contains(".docx") || lowerUrl.Contains(".doc"))
            return true;

        // Incluir si el texto sugiere que es un manual o ficha técnica
        if (lowerText.Contains("manual") || lowerText.Contains("datasheet") ||
            lowerText.Contains("ficha") || lowerText.Contains("catálogo") ||
            lowerText.Contains("especificaciones") || lowerText.Contains("technical"))
            return true;

        return false;
    }
}
