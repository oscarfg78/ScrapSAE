namespace ScrapSAE.Core.DTOs;

/// <summary>
/// Producto extraído del scraping (datos crudos)
/// </summary>
public class ScrapedProduct
{
    public string? SkuSource { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? RawHtml { get; set; }
    public string? ImageUrl { get; set; }
    public decimal? Price { get; set; }
    public string? Category { get; set; }
    public string? Brand { get; set; }
    public Dictionary<string, string> Attributes { get; set; } = new();
    public DateTime ScrapedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Producto procesado por IA (datos estructurados)
/// </summary>
public class ProcessedProduct
{
    public string? Sku { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string Description { get; set; } = string.Empty;
    public List<string> Features { get; set; } = new();
    public Dictionary<string, string> Specifications { get; set; } = new();
    public string? SuggestedCategory { get; set; }
    public string? LineCode { get; set; }
    public decimal? Price { get; set; }
    public decimal? ConfidenceScore { get; set; }
    public string? OriginalRawData { get; set; }
}

/// <summary>
/// Sugerencia de categoría de IA
/// </summary>
public class CategorySuggestion
{
    public string SaeLineCode { get; set; } = string.Empty;
    public string SaeLineName { get; set; } = string.Empty;
    public decimal ConfidenceScore { get; set; }
    public string? Reasoning { get; set; }
}

/// <summary>
/// Payload para webhook de notificación
/// </summary>
public class ProductWebhookPayload
{
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal Stock { get; set; }
    public bool Available { get; set; }
    public string? ImageUrl { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Configuración de selectores para scraping
/// </summary>
public class SiteSelectors
{
    public string? ProductListSelector { get; set; }
    public string? ProductListClassPrefix { get; set; }
    public string? ProductCardClassPrefix { get; set; }
    public string? ProductLinkSelector { get; set; }
    public string? TitleSelector { get; set; }
    public string? PriceSelector { get; set; }
    public string? DescriptionSelector { get; set; }
    public string? ImageSelector { get; set; }
    public string? SkuSelector { get; set; }
    public string? CategorySelector { get; set; }
    public string? BrandSelector { get; set; }
    public string? NextPageSelector { get; set; }
    public string? DetailButtonText { get; set; }
    public string? DetailButtonClassPrefix { get; set; }
    public bool UsesInfiniteScroll { get; set; }
    public int MaxPages { get; set; } = 10;
}

public class SelectorAnalysisRequest
{
    public string? Url { get; set; }
    public string? HtmlSnippet { get; set; }
    public List<string> ImagesBase64 { get; set; } = new();
    public string? Notes { get; set; }
}

public class SelectorSuggestion
{
    public string? ProductListClassPrefix { get; set; }
    public string? ProductCardClassPrefix { get; set; }
    public string? DetailButtonText { get; set; }
    public string? DetailButtonClassPrefix { get; set; }
    public string? TitleSelector { get; set; }
    public string? PriceSelector { get; set; }
    public string? SkuSelector { get; set; }
    public string? ImageSelector { get; set; }
    public string? NextPageSelector { get; set; }
    public decimal? ConfidenceScore { get; set; }
    public string? Reasoning { get; set; }
}

/// <summary>
/// Resultado de operación
/// </summary>
public class OperationResult<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? ErrorMessage { get; set; }
    public int? DurationMs { get; set; }
    
    public static OperationResult<T> Ok(T data, int? durationMs = null) => new()
    {
        Success = true,
        Data = data,
        DurationMs = durationMs
    };
    
    public static OperationResult<T> Fail(string error) => new()
    {
        Success = false,
        ErrorMessage = error
    };
}
