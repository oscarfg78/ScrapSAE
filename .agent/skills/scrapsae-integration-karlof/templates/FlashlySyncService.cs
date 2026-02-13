// ============================================
// FlashlySyncService - ScrapSAE
// Servicio para sincronizar productos con Flashly
// ============================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ScrapSAE.Infrastructure.Services
{
    public class FlashlySyncService : IFlashlySyncService
    {
        private readonly HttpClient _httpClient;
        private readonly FlashlyApiConfig _config;
        private readonly ILogger<FlashlySyncService> _logger;

        public FlashlySyncService(
            HttpClient httpClient,
            IOptions<FlashlyApiConfig> config,
            ILogger<FlashlySyncService> logger)
        {
            _httpClient = httpClient;
            _config = config.Value;
            _logger = logger;

            // Configurar headers por defecto
            _httpClient.BaseAddress = new Uri(_config.BaseUrl);
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", _config.ApiKey);
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public async Task<FlashlySyncResult> SyncProductsAsync(
            IEnumerable<StagingProduct> products)
        {
            try
            {
                _logger.LogInformation(
                    "Iniciando sincronización de {Count} productos con Flashly", 
                    products.Count());

                // Transformar productos al formato de Flashly
                var flashlyProducts = products.Select(p => new FlashlyProductDto
                {
                    SourceSku = p.SkuSource,
                    Name = ExtractFromJson(p.AiProcessedJson, "name"),
                    Description = ExtractFromJson(p.AiProcessedJson, "description"),
                    PurchasePrice = ExtractDecimalFromJson(p.AiProcessedJson, "price"),
                    Currency = "MXN",
                    Categories = ExtractArrayFromJson(p.AiProcessedJson, "categories"),
                    ProductUrl = ExtractFromJson(p.AiProcessedJson, "url"),
                    ImageUrls = ExtractArrayFromJson(p.AiProcessedJson, "images"),
                    SupplierName = ExtractFromJson(p.AiProcessedJson, "supplier"),
                    SpecificationsJson = ExtractFromJson(p.AiProcessedJson, "specifications")
                }).ToList();

                // Crear el request body
                var requestBody = new
                {
                    products = flashlyProducts
                };

                var jsonContent = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                // Enviar request a Flashly
                var response = await _httpClient.PostAsync("/api/v1/products/sync", content);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<FlashlySyncResponse>(
                        responseContent,
                        new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                        });

                    _logger.LogInformation(
                        "Sincronización exitosa: {Created} creados, {Updated} actualizados, {Errors} errores",
                        result?.Results?.Created ?? 0,
                        result?.Results?.Updated ?? 0,
                        result?.Results?.Errors?.Count ?? 0);

                    return new FlashlySyncResult
                    {
                        Success = true,
                        Created = result?.Results?.Created ?? 0,
                        Updated = result?.Results?.Updated ?? 0,
                        Errors = result?.Results?.Errors ?? new List<FlashlySyncError>(),
                        Message = result?.Message
                    };
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError(
                        "Error en sincronización con Flashly: {StatusCode} - {Error}",
                        response.StatusCode,
                        errorContent);

                    return new FlashlySyncResult
                    {
                        Success = false,
                        Message = $"Error HTTP {response.StatusCode}: {errorContent}"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Excepción durante la sincronización con Flashly");
                return new FlashlySyncResult
                {
                    Success = false,
                    Message = $"Excepción: {ex.Message}"
                };
            }
        }

        // Métodos auxiliares para extraer datos del JSON procesado por IA
        private string ExtractFromJson(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return null;

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty(key, out var element))
                {
                    return element.GetString();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error extrayendo '{Key}' del JSON", key);
            }

            return null;
        }

        private decimal ExtractDecimalFromJson(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return 0;

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty(key, out var element))
                {
                    if (element.TryGetDecimal(out var value))
                        return value;
                    
                    // Intentar parsear como string
                    var strValue = element.GetString();
                    if (decimal.TryParse(strValue, out var parsedValue))
                        return parsedValue;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error extrayendo decimal '{Key}' del JSON", key);
            }

            return 0;
        }

        private List<string> ExtractArrayFromJson(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return new List<string>();

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty(key, out var element))
                {
                    if (element.ValueKind == JsonValueKind.Array)
                    {
                        return element.EnumerateArray()
                            .Select(e => e.GetString())
                            .Where(s => !string.IsNullOrEmpty(s))
                            .ToList();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error extrayendo array '{Key}' del JSON", key);
            }

            return new List<string>();
        }
    }

    // ============================================
    // DTOs y Modelos
    // ============================================

    public class FlashlyProductDto
    {
        public string SourceSku { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public decimal PurchasePrice { get; set; }
        public string Currency { get; set; }
        public List<string> Categories { get; set; }
        public string ProductUrl { get; set; }
        public List<string> ImageUrls { get; set; }
        public string SupplierName { get; set; }
        public string SpecificationsJson { get; set; }
    }

    public class FlashlySyncResponse
    {
        public string Status { get; set; }
        public string Message { get; set; }
        public FlashlySyncResults Results { get; set; }
    }

    public class FlashlySyncResults
    {
        public int Created { get; set; }
        public int Updated { get; set; }
        public List<FlashlySyncError> Errors { get; set; }
    }

    public class FlashlySyncError
    {
        public string SourceSku { get; set; }
        public string Error { get; set; }
    }

    public class FlashlySyncResult
    {
        public bool Success { get; set; }
        public int Created { get; set; }
        public int Updated { get; set; }
        public List<FlashlySyncError> Errors { get; set; }
        public string Message { get; set; }
    }

    // ============================================
    // Configuración
    // ============================================

    public class FlashlyApiConfig
    {
        public string BaseUrl { get; set; }
        public string ApiKey { get; set; }
    }

    // ============================================
    // Interface
    // ============================================

    public interface IFlashlySyncService
    {
        Task<FlashlySyncResult> SyncProductsAsync(IEnumerable<StagingProduct> products);
    }

    // ============================================
    // Modelo de StagingProduct (referencia)
    // ============================================

    public class StagingProduct
    {
        public Guid Id { get; set; }
        public Guid SiteId { get; set; }
        public string SkuSource { get; set; }
        public string SkuSae { get; set; }
        public string RawData { get; set; }
        public string AiProcessedJson { get; set; }
        public string Status { get; set; }
        public bool ExcludeFromSae { get; set; }
        public string ValidationNotes { get; set; }
        public int Attempts { get; set; }
        public DateTime? LastSeenAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
