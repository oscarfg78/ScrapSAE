using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ScrapSAE.Core.DTOs;

namespace ScrapSAE.Infrastructure.Data;

/// <summary>
/// Servicio para integración con la API de Flashly (plataforma de e-commerce)
/// </summary>
public class FlashlyIntegrationService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<FlashlyIntegrationService> _logger;
    private readonly string? _apiBaseUrl;
    private readonly string? _apiKey;
    private readonly string? _tenantId;
    private readonly bool _enabled;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public FlashlyIntegrationService(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<FlashlyIntegrationService> logger)
    {
        _logger = logger;
        _apiBaseUrl = configuration["Flashly:ApiBaseUrl"];
        _apiKey = configuration["Flashly:ApiKey"];
        _tenantId = configuration["Flashly:TenantId"];
        _enabled = configuration.GetValue("Flashly:Enabled", false);

        _httpClient = httpClientFactory.CreateClient("Flashly");
        if (!string.IsNullOrWhiteSpace(_apiBaseUrl))
        {
            _httpClient.BaseAddress = new Uri(_apiBaseUrl);
        }
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Envía un producto procesado a Flashly
    /// </summary>
    public async Task<FlashlyProductResponse> SendProductAsync(
        ProcessedProduct product, 
        string? supplierId = null,
        CancellationToken cancellationToken = default)
    {
        if (!_enabled)
        {
            _logger.LogWarning("Flashly integration is disabled");
            return FlashlyProductResponse.CreateDisabled();
        }

        if (string.IsNullOrWhiteSpace(_apiBaseUrl) || string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogError("Flashly API configuration is missing");
            return FlashlyProductResponse.CreateError("Flashly API not configured");
        }

        try
        {
            var payload = MapToFlashlyProduct(product, supplierId);
            var json = JsonSerializer.Serialize(payload, JsonOptions);

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/products");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogInformation("Sending product to Flashly: SKU={Sku}, Name={Name}", product.Sku, product.Name);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Product sent successfully to Flashly: {Sku}", product.Sku);
                var flashlyProduct = JsonSerializer.Deserialize<FlashlyProduct>(responseBody, JsonOptions);
                return FlashlyProductResponse.CreateSuccess(flashlyProduct);
            }
            else
            {
                _logger.LogError("Failed to send product to Flashly: {Status} {Body}", response.StatusCode, responseBody);
                return FlashlyProductResponse.CreateError($"HTTP {response.StatusCode}: {responseBody}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending product to Flashly: {Sku}", product.Sku);
            return FlashlyProductResponse.CreateError(ex.Message);
        }
    }

    /// <summary>
    /// Actualiza un producto existente en Flashly
    /// </summary>
    public async Task<FlashlyProductResponse> UpdateProductAsync(
        string flashlyProductId,
        ProcessedProduct product,
        CancellationToken cancellationToken = default)
    {
        if (!_enabled)
        {
            return FlashlyProductResponse.CreateDisabled();
        }

        try
        {
            var payload = MapToFlashlyProduct(product, null);
            var json = JsonSerializer.Serialize(payload, JsonOptions);

            using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/products/{flashlyProductId}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogInformation("Updating product in Flashly: ID={Id}, SKU={Sku}", flashlyProductId, product.Sku);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Product updated successfully in Flashly: {Id}", flashlyProductId);
                var flashlyProduct = JsonSerializer.Deserialize<FlashlyProduct>(responseBody, JsonOptions);
                return FlashlyProductResponse.CreateSuccess(flashlyProduct);
            }
            else
            {
                _logger.LogError("Failed to update product in Flashly: {Status} {Body}", response.StatusCode, responseBody);
                return FlashlyProductResponse.CreateError($"HTTP {response.StatusCode}: {responseBody}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating product in Flashly: {Id}", flashlyProductId);
            return FlashlyProductResponse.CreateError(ex.Message);
        }
    }

    /// <summary>
    /// Busca un producto en Flashly por SKU
    /// </summary>
    public async Task<FlashlyProduct?> FindProductBySkuAsync(string sku, CancellationToken cancellationToken = default)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(sku))
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/products?search={Uri.EscapeDataString(sku)}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<FlashlyProductListResponse>(responseBody, JsonOptions);

            return result?.Products?.FirstOrDefault(p => 
                string.Equals(p.Sku, sku, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error searching product by SKU in Flashly: {Sku}", sku);
            return null;
        }
    }

    /// <summary>
    /// Mapea un ProcessedProduct de ScrapSAE al formato esperado por Flashly
    /// </summary>
    private object MapToFlashlyProduct(ProcessedProduct product, string? supplierId)
    {
        // Construir el objeto specifications combinando datos estructurados y no mapeados
        var specifications = new Dictionary<string, object>();
        foreach (var spec in product.Specifications)
        {
            specifications[spec.Key] = spec.Value;
        }
        
        // Agregar campos adicionales que no tienen mapeo directo
        if (!string.IsNullOrWhiteSpace(product.Model))
            specifications["model"] = product.Model;
        
        if (!string.IsNullOrWhiteSpace(product.LineCode))
            specifications["lineCode"] = product.LineCode;
        
        if (product.Features.Any())
            specifications["features"] = product.Features;

        if (product.ConfidenceScore.HasValue)
            specifications["aiConfidenceScore"] = product.ConfidenceScore.Value;

        // Mapear categorías (por ahora como strings, luego se deberán mapear a UUIDs)
        var categories = product.Categories.Any() ? product.Categories : 
                        (!string.IsNullOrWhiteSpace(product.SuggestedCategory) ? new List<string> { product.SuggestedCategory } : new List<string>());

        return new
        {
            name = product.Name,
            sku = product.Sku,
            description = product.Description ?? string.Empty,
            price = product.Price ?? 0m,
            currency = product.Currency ?? "MXN",
            stock = product.Stock ?? 0,
            in_stock = (product.Stock ?? 0) > 0,
            is_active = true,
            price_visible = true,
            allow_direct_sale = true,
            supplier_id = supplierId, // Se debe mapear la marca a un supplier_id
            images = product.Images.ToArray(),
            specifications = specifications,
            categories = categories.ToArray(), // Se deberán mapear a UUIDs de categorías
            files = product.Attachments.Select(a => new
            {
                url = a.FileUrl,
                name = a.FileName,
                type = a.FileType,
                size = a.FileSizeBytes
            }).ToArray()
        };
    }
}

/// <summary>
/// Respuesta de la operación de envío a Flashly
/// </summary>
public class FlashlyProductResponse
{
    public bool Success { get; set; }
    public FlashlyProduct? Product { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsDisabled { get; set; }

    public static FlashlyProductResponse CreateSuccess(FlashlyProduct? product) => new()
    {
        Success = true,
        Product = product
    };

    public static FlashlyProductResponse CreateError(string message) => new()
    {
        Success = false,
        ErrorMessage = message
    };

    public static FlashlyProductResponse CreateDisabled() => new()
    {
        Success = false,
        IsDisabled = true,
        ErrorMessage = "Flashly integration is disabled"
    };
}

/// <summary>
/// Producto en Flashly (respuesta de la API)
/// </summary>
public class FlashlyProduct
{
    public string? Id { get; set; }
    public string? TenantId { get; set; }
    public string? Name { get; set; }
    public string? Sku { get; set; }
    public string? Description { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public int? Stock { get; set; }
    public bool? InStock { get; set; }
    public bool? IsActive { get; set; }
    public string[]? Images { get; set; }
    public object? Specifications { get; set; }
}

/// <summary>
/// Respuesta de lista de productos de Flashly
/// </summary>
public class FlashlyProductListResponse
{
    public List<FlashlyProduct>? Products { get; set; }
    public int? Total { get; set; }
    public int? Page { get; set; }
    public int? Limit { get; set; }
}
