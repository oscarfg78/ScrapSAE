using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Interfaces;

namespace ScrapSAE.Infrastructure.Scraping.Strategies;

/// <summary>
/// Estrategia nativa para tiendas Shopify. Consume /products.json paginado.
/// </summary>
public class ShopifyApiStrategy : IProviderScraperStrategy
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ShopifyApiStrategy> _logger;
    private readonly ISyncLogService _syncLogService;

    public ShopifyApiStrategy(
        IHttpClientFactory httpClientFactory,
        ILogger<ShopifyApiStrategy> logger,
        ISyncLogService syncLogService)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _syncLogService = syncLogService;
    }

    public async Task<IEnumerable<ScrapedProduct>> ScrapeAsync(SiteProfile site, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Shopify] Iniciando scraping vía API para {SiteName} ({BaseUrl})", site.Name, site.BaseUrl);
        var client = _httpClientFactory.CreateClient("ShopifyClient");
        var baseUrl = site.BaseUrl;
        if (!baseUrl.EndsWith("/"))
        {
            baseUrl += "/";
        }
        client.BaseAddress = new Uri(baseUrl);
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

        var allProducts = new List<ScrapedProduct>();
        int page = 1;
        int limit = 250;
        bool hasMore = true;

        var startTime = DateTime.UtcNow;

        try
        {
            while (hasMore && !cancellationToken.IsCancellationRequested)
            {
                var url = $"products.json?limit={limit}&page={page}";
                _logger.LogInformation("[Shopify] Pidiendo {Url}", url);

                string json;
                try
                {
                    var response = await client.GetAsync(url, cancellationToken);
                    response.EnsureSuccessStatusCode();
                    json = await response.Content.ReadAsStringAsync(cancellationToken);
                }
                catch (HttpRequestException httpEx)
                {
                    _logger.LogWarning(httpEx, "[Shopify] Error HTTP obteniendo página {Page}: {Status}. Terminando paginación.", page, httpEx.StatusCode);
                    break;
                }

                var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("products", out var productsElement) ||
                    productsElement.ValueKind != JsonValueKind.Array)
                {
                    _logger.LogWarning("[Shopify] No se encontró el arreglo 'products' en la respuesta JSON");
                    break;
                }

                if (productsElement.GetArrayLength() == 0)
                {
                    hasMore = false;
                    break;
                }

                foreach (var prod in productsElement.EnumerateArray())
                {
                    var product = MapShopifyProduct(prod, site.BaseUrl);
                    if (product != null)
                    {
                        allProducts.Add(product);
                    }
                }

                if (site.MaxProductsPerScrape > 0 && allProducts.Count >= site.MaxProductsPerScrape)
                {
                    _logger.LogInformation("[Shopify] Límite de {Max} alcanzado.", site.MaxProductsPerScrape);
                    break;
                }

                page++;
            }

            var syncLog = new SyncLog
            {
                Id = Guid.NewGuid(),
                SiteId = site.Id,
                OperationType = "scrape",
                Status = "success",
                Message = $"Se encontraron {allProducts.Count} productos.",
                DurationMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds,
                CreatedAt = DateTime.UtcNow
            };
            await _syncLogService.LogOperationAsync(syncLog);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Shopify] Error obteniendo /products.json");
            var errorLog = new SyncLog
            {
                Id = Guid.NewGuid(),
                SiteId = site.Id,
                OperationType = "scrape",
                Status = "error",
                Message = ex.Message,
                DurationMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds,
                CreatedAt = DateTime.UtcNow
            };
            await _syncLogService.LogOperationAsync(errorLog);
            throw;
        }

        return allProducts;
    }

    public Task<List<ScrapedProduct>> ScrapeDirectUrlsAsync(List<string> urls, SiteProfile site, DirectUrlScrapeOptions? options = null, CancellationToken cancellationToken = default)
    {
        // For direct URLs on Shopify, you'd append .json to the product URL, e.g. /products/my-product.json
        // Or just fall back to generic strategy.
        throw new NotImplementedException("Shopify direct URL extraction not implemented yet.");
    }

    private ScrapedProduct? MapShopifyProduct(JsonElement prod, string baseUrl)
    {
        try
        {
            var title = prod.GetProperty("title").GetString();
            var handle = prod.GetProperty("handle").GetString();
            var vendor = prod.TryGetProperty("vendor", out var v) ? v.GetString() : null;
            var productType = prod.TryGetProperty("product_type", out var pt) ? pt.GetString() : null;
            
            var variants = prod.GetProperty("variants");
            if (variants.GetArrayLength() == 0) return null;

            var firstVariant = variants[0];
            var sku = firstVariant.TryGetProperty("sku", out var s) ? s.GetString() : null;
            var priceStr = firstVariant.TryGetProperty("price", out var p) ? p.GetString() : "0";

            if (string.IsNullOrWhiteSpace(sku))
            {
                sku = handle; // Fallback al handle si no hay sku
            }

            var images = prod.TryGetProperty("images", out var imgs) ? imgs : default;
            var imageUrl = "";
            if (images.ValueKind == JsonValueKind.Array && images.GetArrayLength() > 0)
            {
                imageUrl = images[0].TryGetProperty("src", out var src) ? src.GetString() : "";
            }

            decimal.TryParse(priceStr, out decimal price);

            var sp = new ScrapedProduct
            {
                SkuSource = sku,
                Title = title,
                Description = $"{vendor} - {productType}",
                Price = price,
                SourceUrl = $"{baseUrl.TrimEnd('/')}/products/{handle}",
                ImageUrl = imageUrl,
                ScrapedAt = DateTime.UtcNow
            };

            return sp;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
