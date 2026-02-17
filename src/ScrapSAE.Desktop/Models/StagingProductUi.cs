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
        get => _overrideImageUrl ?? Images.FirstOrDefault() ?? GetFallbackValue("ImageUrl") ?? "";
        set => SetField(ref _overrideImageUrl, value);
    }

    public string ImageUrl => PrimaryImageUrl;

    public List<string> Images => GetProcessed()?.Images ?? new List<string>();

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
            if (doc.RootElement.TryGetProperty(key, out var prop))
            {
                return prop.GetString();
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
            if (doc.RootElement.TryGetProperty("Price", out var prop) && prop.ValueKind == JsonValueKind.Number)
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
}
