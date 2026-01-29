using System.Text.Json;
using ScrapSAE.Core.Entities;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Desktop.ViewModels;
using System.Collections.Generic;
using System.Linq;
using System;

namespace ScrapSAE.Desktop.Models;

public class StagingProductUi : ViewModelBase
{
    private readonly StagingProduct _product;
    private ProcessedProduct? _processed;
    private Dictionary<string, string>? _fallbackAttributes;
    private bool _isParsed;

    public StagingProductUi(StagingProduct product)
    {
        _product = product;
    }

    public StagingProduct Product => _product;

    public string Title => GetProcessed()?.Name ?? GetFallbackValue("Title") ?? _product.SkuSource ?? "Sin título";
    
    public string Sku => GetProcessed()?.Sku ?? _product.SkuSource ?? "";

    public string ImageUrl => GetProcessed()?.OriginalRawData ?? GetFallbackValue("ImageUrl") ?? "";

    public string Description => GetProcessed()?.Description ?? GetFallbackValue("Description") ?? "";

    public decimal? Price => GetProcessed()?.Price ?? TryGetFallbackPrice();

    public string Status => _product.Status;

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
                // Intentar como ProcessedProduct
                _processed = JsonSerializer.Deserialize<ProcessedProduct>(_product.AIProcessedJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                
                // Si no tiene Name, tal vez es el fallback
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
        catch { }
    }

    private string? GetFallbackValue(string key)
    {
        try
        {
            if (string.IsNullOrEmpty(_product.AIProcessedJson)) return null;
            using var doc = JsonDocument.Parse(_product.AIProcessedJson);
            if (doc.RootElement.TryGetProperty(key, out var prop))
            {
                return prop.GetString();
            }
        }
        catch { }
        return null;
    }

    private decimal? TryGetFallbackPrice()
    {
        try
        {
            if (string.IsNullOrEmpty(_product.AIProcessedJson)) return null;
            using var doc = JsonDocument.Parse(_product.AIProcessedJson);
            if (doc.RootElement.TryGetProperty("Price", out var prop) && prop.ValueKind == JsonValueKind.Number)
            {
                return prop.GetDecimal();
            }
        }
        catch { }
        return null;
    }
}
