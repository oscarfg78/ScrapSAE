using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;

namespace ScrapSAE.Infrastructure.Services;

public static class FlashlyProductMapper
{
    public static FlashlyProductSyncPayload ToFlashlyPayload(StagingProduct product)
    {
        var sourceSku = (product.SkuSource ?? string.Empty).Trim();
        var name = string.Empty;
        var description = string.Empty;
        var purchasePrice = 0m;
        var currency = "MXN";
        var categories = new List<string>();
        string? productUrl = string.IsNullOrWhiteSpace(product.SourceUrl) ? null : product.SourceUrl.Trim();
        var imageUrls = new List<string>();
        string? supplierName = product.Site?.BrandOverride ?? product.Brand;
        string? specificationsJson = null;

        var specsDict = new Dictionary<string, object?>();

        // 1. Parse AIProcessedJson
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

                name = FirstNonEmpty(
                    ReadString(root, "name", "Name", "title", "Title", "nombre", "titulo"),
                    name) ?? string.Empty;

                description = FirstNonEmpty(
                    ReadString(root, "description", "Description", "descripcion", "detalle"),
                    description) ?? string.Empty;

                purchasePrice = ReadDecimal(root, "purchasePrice", "purchase_price", "price", "Price");
                currency = FirstNonEmpty(ReadString(root, "currency", "Currency"), currency) ?? currency;

                categories = ReadStringArray(root, "categories", "Categories", "category_path", "categoryPath");
                if (categories.Count == 0)
                {
                    var cat = ReadString(root, "category", "Category", "categoria", "Categoria");
                    if (!string.IsNullOrWhiteSpace(cat)) categories.Add(cat.Trim());
                }

                productUrl = FirstNonEmpty(
                    ReadString(root, "productUrl", "product_url", "url", "Url", "sourceUrl", "source_url"),
                    productUrl);

                imageUrls = ReadStringArray(root, "imageUrls", "image_urls", "images", "Images", "primaryImageUrls");
                if (imageUrls.Count == 0)
                {
                    var singleImg = ReadString(root, "imageUrl", "image_url", "ImageUrl", "primaryImageUrl", "thumbnailUrl");
                    if (!string.IsNullOrWhiteSpace(singleImg)) imageUrls.Add(singleImg.Trim());
                }

                supplierName = FirstNonEmpty(
                    supplierName,
                    ReadString(root, "supplierName", "supplier_name", "supplier", "brand", "Brand"));

                ExtractSpecificationsIntoDict(specsDict, root);
            }
            catch { }
        }

        // 2. Parse RawData fallback
        if (!string.IsNullOrWhiteSpace(product.RawData))
        {
            try
            {
                using var doc = JsonDocument.Parse(product.RawData);
                var root = doc.RootElement;

                if (string.IsNullOrWhiteSpace(name))
                    name = FirstNonEmpty(ReadString(root, "title", "Title", "name", "Name", "nombre"), string.Empty) ?? string.Empty;

                if (string.IsNullOrWhiteSpace(description))
                    description = FirstNonEmpty(ReadString(root, "description", "Description"), string.Empty) ?? string.Empty;

                if (purchasePrice == 0m)
                    purchasePrice = ReadDecimal(root, "supplierCost", "SupplierCost", "cost", "Costo", "price", "Price");

                if (imageUrls.Count == 0)
                {
                    imageUrls = ReadStringArray(root, "imageUrls", "image_urls", "images", "Images");
                    if (imageUrls.Count == 0)
                    {
                        var singleImg = ReadString(root, "imageUrl", "image_url", "ImageUrl", "firstImageUrl");
                        if (!string.IsNullOrWhiteSpace(singleImg)) imageUrls.Add(singleImg.Trim());
                    }
                }

                if (string.IsNullOrWhiteSpace(productUrl))
                    productUrl = ReadString(root, "sourceUrl", "source_url", "productUrl", "product_url", "url");

                ExtractSpecificationsIntoDict(specsDict, root);
            }
            catch { }
        }

        // Fallbacks
        if (string.IsNullOrWhiteSpace(name)) name = sourceSku;
        if (string.IsNullOrWhiteSpace(description)) description = name;
        if (specsDict.Count > 0)
        {
            specificationsJson = JsonSerializer.Serialize(specsDict, new JsonSerializerOptions { WriteIndented = false });
        }

        return new FlashlyProductSyncPayload
        {
            SourceSku = sourceSku,
            Name = name,
            Description = description,
            PurchasePrice = purchasePrice,
            Currency = currency,
            Categories = categories,
            ProductUrl = string.IsNullOrWhiteSpace(productUrl) ? null : productUrl,
            ImageUrls = imageUrls,
            SupplierName = supplierName,
            SpecificationsJson = specificationsJson
        };
    }

    public static FlashlyProductSyncPayload ToFlashlyPayload(ConsolidatedProductResult result, string? supplierName = null)
    {
        var sourceSku = (result.Sku ?? string.Empty).Trim();
        var specsDict = new Dictionary<string, object?>();

        if (result.OptionalAttributes != null)
        {
            foreach (var kvp in result.OptionalAttributes)
            {
                if (!string.IsNullOrWhiteSpace(kvp.Key))
                {
                    specsDict[kvp.Key] = kvp.Value;
                }
            }
        }

        string? name = result.Title;
        if (string.IsNullOrWhiteSpace(name) && result.OptionalAttributes != null)
        {
            foreach (var key in new[] { "Nombre", "Title", "Name", "Titulo", "NombreProducto", "Producto" })
            {
                if (result.OptionalAttributes.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val))
                {
                    name = val;
                    break;
                }
            }
        }
        if (string.IsNullOrWhiteSpace(name)) name = sourceSku;

        string? description = result.Description;
        if (string.IsNullOrWhiteSpace(description) && result.OptionalAttributes != null)
        {
            foreach (var key in new[] { "Description", "Descripcion", "Detalle" })
            {
                if (result.OptionalAttributes.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val))
                {
                    description = val;
                    break;
                }
            }
        }
        if (string.IsNullOrWhiteSpace(description)) description = name;

        var categories = new List<string>();
        if (result.OptionalAttributes != null && result.OptionalAttributes.TryGetValue("Categoria", out var cat) && !string.IsNullOrWhiteSpace(cat))
        {
            categories.Add(cat.Trim());
        }

        var productUrl = result.SourceDetailUrls?.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(productUrl) && result.OptionalAttributes != null && result.OptionalAttributes.TryGetValue("UrlOrigen", out var url))
        {
            productUrl = url;
        }

        var imageUrls = result.ImageUrls ?? new List<string>();

        string? finalSupplier = supplierName;
        if (string.IsNullOrWhiteSpace(finalSupplier) && result.OptionalAttributes != null)
        {
            foreach (var key in new[] { "Marca", "Brand", "Proveedor", "Supplier" })
            {
                if (result.OptionalAttributes.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val))
                {
                    finalSupplier = val;
                    break;
                }
            }
        }

        string? specsJson = specsDict.Count > 0
            ? JsonSerializer.Serialize(specsDict, new JsonSerializerOptions { WriteIndented = false })
            : null;

        return new FlashlyProductSyncPayload
        {
            SourceSku = sourceSku,
            Name = name,
            Description = description,
            PurchasePrice = result.SupplierCost,
            Currency = "MXN",
            Categories = categories,
            ProductUrl = string.IsNullOrWhiteSpace(productUrl) ? null : productUrl.Trim(),
            ImageUrls = imageUrls,
            SupplierName = finalSupplier,
            SpecificationsJson = specsJson
        };
    }

    public static FlashlyProductSyncDto ToFlashlyDto(StagingProduct product)
    {
        var payload = ToFlashlyPayload(product);
        return new FlashlyProductSyncDto
        {
            SourceSku = payload.SourceSku,
            Name = payload.Name,
            Description = payload.Description,
            PurchasePrice = payload.PurchasePrice,
            Currency = payload.Currency,
            Categories = payload.Categories,
            ProductUrl = payload.ProductUrl,
            ImageUrls = payload.ImageUrls,
            SupplierName = payload.SupplierName,
            SpecificationsJson = payload.SpecificationsJson
        };
    }

    private static void ExtractSpecificationsIntoDict(Dictionary<string, object?> dict, JsonElement root)
    {
        if (TryGetPropertyCaseInsensitive(root, "specifications", out var specsProp) ||
            TryGetPropertyCaseInsensitive(root, "Attributes", out specsProp) ||
            TryGetPropertyCaseInsensitive(root, "attributes", out specsProp))
        {
            if (specsProp.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in specsProp.EnumerateObject())
                {
                    if (!dict.ContainsKey(prop.Name))
                    {
                        dict[prop.Name] = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.Clone();
                    }
                }
            }
            else if (specsProp.ValueKind == JsonValueKind.String)
            {
                try
                {
                    using var subDoc = JsonDocument.Parse(specsProp.GetString()!);
                    if (subDoc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in subDoc.RootElement.EnumerateObject())
                        {
                            if (!dict.ContainsKey(prop.Name))
                            {
                                dict[prop.Name] = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.Clone();
                            }
                        }
                    }
                }
                catch { }
            }
        }
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
