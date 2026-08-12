using System.Text.Json.Serialization;

namespace ScrapSAE.Core.DTOs;

/// <summary>
/// DTO representing a single product formatted for Flashly's /api/v1/products/sync JSON schema.
/// </summary>
public class FlashlyProductSyncPayload
{
    [JsonPropertyName("source_sku")]
    public string SourceSku { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("purchase_price")]
    public decimal PurchasePrice { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "MXN";

    [JsonPropertyName("categories")]
    public List<string> Categories { get; set; } = new();

    [JsonPropertyName("product_url")]
    public string? ProductUrl { get; set; }

    [JsonPropertyName("image_urls")]
    public List<string> ImageUrls { get; set; } = new();

    [JsonPropertyName("supplier_name")]
    public string? SupplierName { get; set; }

    [JsonPropertyName("specifications_json")]
    public string? SpecificationsJson { get; set; }
}

/// <summary>
/// Container object for sending multiple product payloads to Flashly sync endpoint.
/// </summary>
public class FlashlyProductSyncBatchRequest
{
    [JsonPropertyName("products")]
    public List<FlashlyProductSyncPayload> Products { get; set; } = new();
}
