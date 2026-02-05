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
    public string? ScreenshotBase64 { get; set; }
    public string? ImageUrl { get; set; }
    public List<string> ImageUrls { get; set; } = new();
    public decimal? Price { get; set; }
    public string? Category { get; set; }
    public string? Brand { get; set; }
    /// <summary>URL de donde se extrajo este producto</summary>
    public string? SourceUrl { get; set; }
    public Dictionary<string, string> Attributes { get; set; } = new();
    public List<string> NavigationUrls { get; set; } = new();
    public DateTime ScrapedAt { get; set; } = DateTime.UtcNow;
    public bool AiEnriched { get; set; }
    
    /// <summary>
    /// Adjuntos encontrados durante el scraping (ej: datasheets)
    /// </summary>
    public List<ProductAttachment> Attachments { get; set; } = new();
}

/// <summary>
/// Archivo adjunto de producto (PDF, manual, ficha técnica)
/// </summary>
public class ProductAttachment
{
    public string FileName { get; set; } = string.Empty;
    public string FileUrl { get; set; } = string.Empty;
    public string? FileType { get; set; }
    public long? FileSizeBytes { get; set; }
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
    public List<string> Categories { get; set; } = new();
    public string? LineCode { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public int? Stock { get; set; }
    public List<string> Images { get; set; } = new();
    public List<ProductAttachment> Attachments { get; set; } = new();
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
    public string? CategoryLandingUrl { get; set; }
    public string? CategoryLinkSelector { get; set; }
    public string? CategoryNameSelector { get; set; }
    public List<string> CategorySearchTerms { get; set; } = new();
    public string? SearchInputSelector { get; set; }
    public string? SearchButtonSelector { get; set; }
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
    public string? VariantTableSelector { get; set; }
    public string? VariantRowSelector { get; set; }
    public string? VariantSkuLinkSelector { get; set; }
    public string? DetailSkuSelector { get; set; }
    public string? DetailPriceSelector { get; set; }
    public bool UsesInfiniteScroll { get; set; }
    public int MaxPages { get; set; } = 10;
    
    // Propiedades para modo de scraping de familias (Festo-style)
    public string? ScrapingMode { get; set; } // "traditional" o "families"
    public string? ProductFamilyLinkSelector { get; set; }  // Selector para enlaces de familias
    public string? ProductFamilyLinkText { get; set; }      // Texto del enlace (ej: "Explorar la serie")
    public List<string>? CategoryUrls { get; set; }         // URLs directas de categorías para modo families
    
    // Propiedades para extracción profunda de detalle de variante
    public string? VariantDetailLinkSelector { get; set; }  // Selector del enlace a la página de detalle desde la fila de variante
    public string? DetailTitleSelector { get; set; }        // Selector para el título en la página de detalle
    public string? DetailDescriptionSelector { get; set; }  // Selector para la descripción en la página de detalle
    public string? DetailImageSelector { get; set; }        // Selector para la imagen principal en la página de detalle
    
    // Selectores para galería de imágenes
    public string? ImageGallerySelector { get; set; }       // Selector para el contenedor de la galería
    public string? ImageGalleryItemSelector { get; set; }   // Selector para cada imagen en la galería
    
    // Selectores para archivos adjuntos
    public string? AttachmentLinkSelector { get; set; }     // Selector para enlaces a PDFs/documentos
    
    // Selectores para stock
    public string? StockSelector { get; set; }              // Selector para información de stock
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

/// <summary>
/// Resultado de inspección de una URL
/// </summary>
public class DirectUrlResult
{
    public string Url { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Error { get; set; }
    
    // Datos detectados
    public string? DetectedType { get; set; }
    public string? Title { get; set; }
    public string? Sku { get; set; }
    public string? Price { get; set; }
    public string? ImageUrl { get; set; }
    public string? Breadcrumb { get; set; }
    
    // Estadísticas para páginas de listado
    public int? ProductsFound { get; set; }
    public List<string>? ChildLinks { get; set; }
    
    // Screenshot para debug
    public string? ScreenshotBase64 { get; set; }
}
