using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Input;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;
using ScrapSAE.Desktop.Infrastructure;
using ScrapSAE.Desktop.ViewModels;

namespace ScrapSAE.Desktop.Models;

public class StagingProductUi : ViewModelBase
{
    private readonly StagingProduct _product;
    private ProcessedProduct? _processed;
    private Dictionary<string, string>? _fallbackAttributes;
    private bool _isParsed;
    private string? _overrideImageUrl;
    private bool _isSelected;

    public StagingProductUi(StagingProduct product)
    {
        _product = product;
        ChangeImageCommand = new RelayCommand<string>(url => PrimaryImageUrl = url);
        OpenFileCommand = new RelayCommand<string>(url =>
        {
            if (!string.IsNullOrEmpty(url))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                catch
                {
                    // Ignore open-file errors.
                }
            }
        });
    }

    public ICommand ChangeImageCommand { get; }
    public ICommand OpenFileCommand { get; }

    public StagingProduct Product => _product;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public string Title => GetProcessed()?.Name ?? GetFallbackValue("Title") ?? _product.SkuSource ?? "Sin titulo";

    public string ProductName
    {
        get => Title;
        set
        {
            UpsertAiString("Name", value);
            UpsertAiString("Title", value);
            InvalidateParsed();
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged();
        }
    }

    public string Sku => GetProcessed()?.Sku ?? _product.SkuSource ?? "";

    public string SourceUrl
    {
        get => _product.SourceUrl ?? string.Empty;
        set
        {
            _product.SourceUrl = value;
            OnPropertyChanged();
        }
    }

    public string PrimaryImageUrl
    {
        get => _overrideImageUrl
               ?? Images.FirstOrDefault()
               ?? ReadSingleImageUrl(_product.AIProcessedJson)
               ?? ReadSingleImageUrl(_product.RawData)
               ?? "";
        set => SetField(ref _overrideImageUrl, value);
    }

    public string ImageUrl => PrimaryImageUrl;

    public List<string> Images
    {
        get
        {
            var merged = new List<string>();
            AppendUnique(merged, GetProcessed()?.Images);
            AppendUnique(merged, ReadImageLinksFromJson(_product.AIProcessedJson));
            AppendUnique(merged, ReadImageLinksFromJson(_product.RawData));
            AppendUnique(merged, new[]
            {
                ReadSingleImageUrl(_product.AIProcessedJson),
                ReadSingleImageUrl(_product.RawData)
            });

            return merged;
        }
    }

    public string ImageLinksText
    {
        get
        {
            var links = Images;
            return links.Count == 0 ? string.Empty : string.Join(" | ", links);
        }
    }

    public string Currency => GetProcessed()?.Currency ?? "MXN";

    public int? Stock => GetProcessed()?.Stock;

    public List<ProductAttachment> Attachments => GetProcessed()?.Attachments ?? new List<ProductAttachment>();

    public List<string> Categories => GetProcessed()?.Categories ?? new List<string>();

    public string Description => GetProcessed()?.Description ?? GetFallbackValue("Description") ?? "";

    public decimal? Price => GetProcessed()?.Price ?? TryGetFallbackPrice();

    public decimal? EditablePrice
    {
        get => Price;
        set
        {
            UpsertAiDecimal("Price", value);
            InvalidateParsed();
            OnPropertyChanged(nameof(Price));
            OnPropertyChanged();
        }
    }

    public string Status => _product.Status;

    public string FlashlySyncStatus => _product.FlashlySyncStatus;

    public DateTime? FlashlySyncedAt => _product.FlashlySyncedAt;

    public bool IsApartado
    {
        get => _product.IsApartado;
        set
        {
            _product.IsApartado = value;
            OnPropertyChanged();
        }
    }

    public List<KeyValuePair<string, string>> AllSpecifications
    {
        get
        {
            var specs = new List<KeyValuePair<string, string>>();
            var processed = GetProcessed();
            if (processed != null && processed.Specifications != null)
            {
                foreach (var spec in processed.Specifications)
                {
                    specs.Add(new KeyValuePair<string, string>(spec.Key, spec.Value));
                }
            }

            if (specs.Count == 0 && _fallbackAttributes != null)
            {
                foreach (var attr in _fallbackAttributes)
                {
                    specs.Add(new KeyValuePair<string, string>(attr.Key, attr.Value));
                }
            }

            return specs;
        }
    }

    private ProcessedProduct? GetProcessed()
    {
        if (_isParsed) return _processed;

        if (!string.IsNullOrEmpty(_product.AIProcessedJson))
        {
            try
            {
                _processed = JsonSerializer.Deserialize<ProcessedProduct>(_product.AIProcessedJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (string.IsNullOrEmpty(_processed?.Name))
                {
                    ParseFallback(_product.AIProcessedJson);
                }
            }
            catch
            {
                ParseFallback(_product.AIProcessedJson);
            }
        }

        _isParsed = true;
        return _processed;
    }

    private void ParseFallback(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Attributes", out var attrs))
            {
                _fallbackAttributes = JsonSerializer.Deserialize<Dictionary<string, string>>(attrs.GetRawText());
            }
        }
        catch
        {
            // Ignore parse errors.
        }
    }

    private string? GetFallbackValue(string key)
    {
        try
        {
            if (string.IsNullOrEmpty(_product.AIProcessedJson)) return null;
            using var doc = JsonDocument.Parse(_product.AIProcessedJson);
            if (TryGetPropertyIgnoreCase(doc.RootElement, key, out var prop))
            {
                return prop.ValueKind switch
                {
                    JsonValueKind.String => prop.GetString(),
                    JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => prop.ToString(),
                    _ => null
                };
            }
        }
        catch
        {
            // Ignore parse errors.
        }

        return null;
    }

    private decimal? TryGetFallbackPrice()
    {
        try
        {
            if (string.IsNullOrEmpty(_product.AIProcessedJson)) return null;
            using var doc = JsonDocument.Parse(_product.AIProcessedJson);
            if (TryGetPropertyIgnoreCase(doc.RootElement, "Price", out var prop) && prop.ValueKind == JsonValueKind.Number)
            {
                return prop.GetDecimal();
            }
        }
        catch
        {
            // Ignore parse errors.
        }

        return null;
    }

    private void InvalidateParsed()
    {
        _isParsed = false;
        _processed = null;
        _fallbackAttributes = null;
    }

    private void UpsertAiString(string key, string? value)
    {
        var node = ParseOrCreateAiNode();
        node[key] = value ?? string.Empty;
        _product.AIProcessedJson = node.ToJsonString();
    }

    private void UpsertAiDecimal(string key, decimal? value)
    {
        var node = ParseOrCreateAiNode();
        node[key] = value.HasValue ? JsonValue.Create(value.Value) : null;
        _product.AIProcessedJson = node.ToJsonString();
    }

    private JsonObject ParseOrCreateAiNode()
    {
        if (string.IsNullOrWhiteSpace(_product.AIProcessedJson))
        {
            return new JsonObject();
        }

        try
        {
            var parsed = JsonNode.Parse(_product.AIProcessedJson) as JsonObject;
            return parsed ?? new JsonObject();
        }
        catch
        {
            return new JsonObject();
        }
    }

    private static void AppendUnique(List<string> target, IEnumerable<string?>? source)
    {
        if (source == null)
        {
            return;
        }

        foreach (var value in source)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var normalized = value.Trim();
            if (target.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            target.Add(normalized);
        }
    }

    private static string? ReadSingleImageUrl(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return ReadStringProperty(
                doc.RootElement,
                "imageUrl", "image_url", "ImageUrl",
                "primaryImageUrl", "primary_image_url", "PrimaryImageUrl",
                "thumbnailUrl", "thumbnail_url", "ThumbnailUrl");
        }
        catch
        {
            return null;
        }
    }

    private static List<string> ReadImageLinksFromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<string>();
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var names = new[]
            {
                "images", "Images", "imageUrls", "image_urls", "ImageUrls",
                "imageUrl", "image_url", "ImageUrl",
                "primaryImageUrl", "primary_image_url", "PrimaryImageUrl",
                "thumbnailUrl", "thumbnail_url", "ThumbnailUrl"
            };
            var result = new List<string>();
            foreach (var name in names)
            {
                if (!TryGetPropertyIgnoreCase(root, name, out var element))
                {
                    continue;
                }

                AppendUnique(result, ReadImageLinksFromElement(element));
            }

            return result;
        }
        catch
        {
            // Ignore parse errors.
        }

        return new List<string>();
    }

    private static List<string> ReadImageLinksFromElement(JsonElement element)
    {
        var result = new List<string>();

        if (element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                foreach (var item in value.Split('|', ';', '\n', '\r'))
                {
                    var trimmed = item.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed))
                    {
                        result.Add(trimmed);
                    }
                }
            }
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            var value = ReadStringProperty(
                element,
                "url", "Url", "src", "Src", "imageUrl", "image_url", "ImageUrl",
                "primaryImageUrl", "primary_image_url", "PrimaryImageUrl",
                "thumbnailUrl", "thumbnail_url", "ThumbnailUrl");
            if (!string.IsNullOrWhiteSpace(value))
            {
                result.Add(value.Trim());
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        result.Add(value.Trim());
                    }
                    continue;
                }

                if (item.ValueKind == JsonValueKind.Object)
                {
                    var value = ReadStringProperty(
                        item,
                        "url", "Url", "src", "Src", "imageUrl", "image_url", "ImageUrl",
                        "primaryImageUrl", "primary_image_url", "PrimaryImageUrl",
                        "thumbnailUrl", "thumbnail_url", "ThumbnailUrl");
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        result.Add(value.Trim());
                    }
                }
            }
        }

        return result
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? ReadStringProperty(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGetPropertyIgnoreCase(element, name, out var property) &&
                property.ValueKind == JsonValueKind.String)
            {
                return property.GetString();
            }
        }

        return null;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
