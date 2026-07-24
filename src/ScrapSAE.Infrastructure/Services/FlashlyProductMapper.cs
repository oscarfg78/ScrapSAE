using System.Text.Json;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;

namespace ScrapSAE.Infrastructure.Services;

internal static class FlashlyProductMapper
{
    public static FlashlyProductSyncDto ToFlashlyDto(StagingProduct product)
    {
        var brandOverride = product.Site?.BrandOverride;
        var sourceSku = product.SkuSource?.Trim() ?? string.Empty;
        var name = string.Empty;
        var description = string.Empty;
        var purchasePrice = 0m;
        var currency = "MXN";
        var categories = new List<string>();
        string? productUrl = product.SourceUrl;
        var imageUrls = new List<string>();
        string? supplierName = null;
        string? specificationsJson = null;

        if (!string.IsNullOrWhiteSpace(product.AIProcessedJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(product.AIProcessedJson);
                var root = doc.RootElement;

                sourceSku = FirstNonEmpty(
                    ReadString(root, "sourceSku", "source_sku", "skuSource", "sku_source"),
                    ReadString(root, "sku", "Sku"),
                    sourceSku) ?? sourceSku;

                name = FirstNonEmpty(ReadString(root, "name", "Name"), name) ?? name;
                description = FirstNonEmpty(ReadString(root, "description", "Description"), description) ?? description;
                purchasePrice = ReadDecimal(root, "purchasePrice", "purchase_price", "price", "Price");
                currency = FirstNonEmpty(ReadString(root, "currency", "Currency"), currency) ?? currency;
                categories = ReadStringArray(root, "categories", "Categories");
                productUrl = FirstNonEmpty(ReadString(root, "productUrl", "product_url", "url", "sourceUrl", "source_url"), productUrl);
                imageUrls = ReadStringArray(root, "imageUrls", "image_urls", "images", "Images", "imageUrls");
                supplierName = FirstNonEmpty(ReadString(root, "supplierName", "supplier_name", "supplier", "brand", "Brand"), supplierName);
                if (!string.IsNullOrWhiteSpace(brandOverride))
                {
                    supplierName = brandOverride;
                }
                var rawSpecs = ReadRawJson(root, "specifications", "Specifications");
                specificationsJson = ProcessSpecifications(rawSpecs, brandOverride);
            }
            catch
            {
                // Invalid AIProcessedJson should not block sync/export.
            }
        }

        if (productUrl != null && productUrl.Contains("idsupply.com.mx", StringComparison.OrdinalIgnoreCase))
        {
            supplierName = null;
        }

        return new FlashlyProductSyncDto
        {
            SourceSku = sourceSku,
            Name = name,
            Description = description,
            PurchasePrice = purchasePrice,
            Currency = currency,
            Categories = categories,
            ProductUrl = productUrl,
            ImageUrls = imageUrls,
            SupplierName = supplierName,
            SpecificationsJson = specificationsJson
        };
    }

    private static string? ProcessSpecifications(string? rawJson, string? brandOverride)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return rawJson;

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                var dict = new Dictionary<string, object?>();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Name.Equals("source_url", StringComparison.OrdinalIgnoreCase) ||
                        prop.Name.Equals("supplier name", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(brandOverride) &&
                        prop.Name.Equals("brand", StringComparison.OrdinalIgnoreCase))
                    {
                        dict[prop.Name] = brandOverride;
                        continue;
                    }

                    dict[prop.Name] = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.Clone();
                }

                if (!string.IsNullOrWhiteSpace(brandOverride) && !dict.Keys.Any(k => k.Equals("brand", StringComparison.OrdinalIgnoreCase)))
                {
                    dict["brand"] = brandOverride;
                }

                return JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = false });
            }
        }
        catch
        {
            // Ignored
        }

        return rawJson;
    }

    private static string? ReadString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGetPropertyCaseInsensitive(element, name, out var value))
            {
                if (value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }

                if (value.ValueKind == JsonValueKind.Number || value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
                {
                    return value.ToString();
                }
            }
        }

        return null;
    }

    private static decimal ReadDecimal(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGetPropertyCaseInsensitive(element, name, out var value))
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var decimalValue))
                {
                    return decimalValue;
                }

                if (value.ValueKind == JsonValueKind.String &&
                    decimal.TryParse(value.GetString(), out var parsed))
                {
                    return parsed;
                }
            }
        }

        return 0m;
    }

    private static List<string> ReadStringArray(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGetPropertyCaseInsensitive(element, name, out var value))
            {
                if (value.ValueKind == JsonValueKind.Array)
                {
                    return value.EnumerateArray()
                        .Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.ToString())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x!)
                        .ToList();
                }

                if (value.ValueKind == JsonValueKind.String)
                {
                    var raw = value.GetString();
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        return new List<string>();
                    }

                    return raw.Split('|', ';', ',')
                        .Select(x => x.Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToList();
                }
            }
        }

        return new List<string>();
    }

    private static string? ReadRawJson(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGetPropertyCaseInsensitive(element, name, out var value))
            {
                return value.GetRawText();
            }
        }

        return null;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
