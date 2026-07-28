namespace ScrapSAE.Core.DTOs;

/// <summary>
/// Producto extraÃ­do del scraping (datos crudos)
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
    public string? CharacteristicsHtml { get; set; }
    
    /// <summary>
    /// Adjuntos encontrados durante el scraping (ej: datasheets)
    /// </summary>
    public List<ProductAttachment> Attachments { get; set; } = new();
}

/// <summary>
/// Archivo adjunto de producto (PDF, manual, ficha tÃ©cnica)
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
/// Sugerencia de categorÃ­a de IA
/// </summary>
public class CategorySuggestion
{
    public string SaeLineCode { get; set; } = string.Empty;
    public string SaeLineName { get; set; } = string.Empty;
    public decimal ConfidenceScore { get; set; }
    public string? Reasoning { get; set; }
}

/// <summary>
/// Payload para webhook de notificaciÃ³n
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
/// ConfiguraciÃ³n de selectores para scraping
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
    public List<string>? CategoryUrls { get; set; }         // URLs directas de categorÃ­as para modo families
    
    // Propiedades para extracciÃ³n profunda de detalle de variante
    public string? VariantDetailLinkSelector { get; set; }  // Selector del enlace a la pÃ¡gina de detalle desde la fila de variante
    public string? DetailTitleSelector { get; set; }        // Selector para el tÃ­tulo en la pÃ¡gina de detalle
    public string? DetailDescriptionSelector { get; set; }  // Selector para la descripciÃ³n en la pÃ¡gina de detalle
    public string? DetailImageSelector { get; set; }        // Selector para la imagen principal en la pÃ¡gina de detalle
    public string? CharacteristicsSelector { get; set; }    // Selector para especificaciones detalladas (ej: tab-content-description)
    
    // Selectores para galerÃ­a de imÃ¡genes
    public string? ImageGallerySelector { get; set; }       // Selector para el contenedor de la galerÃ­a
    public string? ImageGalleryItemSelector { get; set; }   // Selector para cada imagen en la galerÃ­a
    
    // Selectores para archivos adjuntos
    public string? AttachmentLinkSelector { get; set; }     // Selector para enlaces a PDFs/documentos
    
    // Selectores para stock
    public string? StockSelector { get; set; }              // Selector para informaciÃ³n de stock
}


/// <summary>
/// Opciones para scraping directo de URLs.
/// </summary>
public class DirectUrlScrapeOptions
{
    /// <summary>
    /// Si es true, se inspecciona sin persistir productos.
    /// </summary>
    public bool InspectOnly { get; set; }

    /// <summary>
    /// Si es true, extrae solo el producto de la URL dada (modo 1:1).
    /// </summary>
    public bool SingleProductOnly { get; set; }

    /// <summary>
    /// Si es true, permite expandir a URLs relacionadas.
    /// </summary>
    public bool ExpandRelated { get; set; } = true;
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
/// Resultado de operaciÃ³n
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
/// Resultado de inspecciÃ³n de una URL
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
    
    // EstadÃ­sticas para pÃ¡ginas de listado
    public int? ProductsFound { get; set; }
    public List<string>? ChildLinks { get; set; }
    
    // Screenshot para debug
    public string? ScreenshotBase64 { get; set; }
}

/// <summary>
/// Respuesta estandarizada para inspección directa de URLs.
/// </summary>
public class InspectUrlsResponse
{
    public int TotalUrls { get; set; }
    public int SuccessCount { get; set; }
    public int ProductsCreated { get; set; }
    public int ProductsUpdated { get; set; }
    public bool InspectOnly { get; set; }
    public List<DirectUrlResult> Results { get; set; } = new();
}

/// <summary>
/// Request para encolar un rescrape de productos de staging.
/// </summary>
public class RescrapeRequest
{
    public List<Guid> ProductIds { get; set; } = new();
    public bool ManualLogin { get; set; }
}

/// <summary>
/// Respuesta al crear un job de rescrape.
/// </summary>
public class RescrapeJobResponse
{
    public Guid JobId { get; set; }
    public int TotalItems { get; set; }
    public DateTime QueuedAt { get; set; }
}

/// <summary>
/// Estado agregado de un job de rescrape.
/// </summary>
public class RescrapeJobStatusResponse
{
    public Guid JobId { get; set; }
    public string Status { get; set; } = "queued";
    public DateTime RequestedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int TotalItems { get; set; }
    public int ProcessedItems { get; set; }
    public int SuccessItems { get; set; }
    public int FailedItems { get; set; }
    public int SkippedItems { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Estado por item de un job de rescrape.
/// </summary>
public class RescrapeJobItemResponse
{
    public Guid ItemId { get; set; }
    public Guid JobId { get; set; }
    public Guid StagingProductId { get; set; }
    public Guid SiteId { get; set; }
    public string? SourceUrl { get; set; }
    public string Status { get; set; } = "pending";
    public bool Changed { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ResultJson { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Evento de ejecución de un job de rescrape.
/// </summary>
public class RescrapeJobLogResponse
{
    public Guid LogId { get; set; }
    public Guid JobId { get; set; }
    public Guid? ItemId { get; set; }
    public Guid? StagingProductId { get; set; }
    public string Level { get; set; } = "info";
    public string Message { get; set; } = string.Empty;
    public string? DetailsJson { get; set; }
    public DateTime CreatedAt { get; set; }
}

#region Extraction Pipeline Contracts

public enum SelectorType { Css, XPath, Attribute }

public class SelectorDescriptor 
{
    public SelectorType Type { get; set; }
    public string Expression { get; set; } = string.Empty;
    public string? TargetAttribute { get; set; }
    public decimal Confidence { get; set; } = 1.0m;
}

public enum ContributorStatus { NotApplicable, NoData, Partial, Success, RecoverableFailure, FatalFailure }

public class ContributorDescriptor 
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public List<string> Capabilities { get; set; } = new();
}

public class ContributorResult 
{
    public string ContributorId { get; set; } = string.Empty;
    public ContributorStatus Status { get; set; }
    public List<ProductObservation> Observations { get; set; } = new();
    public List<string> CandidateUrls { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public int DurationMs { get; set; }
}

public class ProductObservation 
{
    public string Field { get; set; } = string.Empty;
    public string? RawValue { get; set; }
    public string? NormalizedValue { get; set; }
    public SelectorDescriptor? ProvenanceSelector { get; set; }
    public string ContributorId { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public decimal Confidence { get; set; } = 1.0m;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class ReconciledProduct 
{
    public string? Sku { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public string? Brand { get; set; }
    public string? ImageUrl { get; set; }
    public List<string> ImageUrls { get; set; } = new();
    public int? Stock { get; set; }
    public string SourceUrl { get; set; } = string.Empty;
    
    public Dictionary<string, ProductObservation> FieldProvenance { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public enum QualityGateResult { Pass, PassWithWarnings, Fail }

public class QualityGateEvaluation
{
    public QualityGateResult Result { get; set; }
    public List<string> Reasons { get; set; } = new();
}

public class ExtractionRunReport 
{
    public string RunId { get; set; } = Guid.NewGuid().ToString();
    public bool IsDemo { get; set; }
    public List<ContributorResult> ContributorResults { get; set; } = new();
    public List<ReconciledProduct> Products { get; set; } = new();
    public QualityGateEvaluation QualityGate { get; set; } = new();
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public int TotalDurationMs { get; set; }
}

#endregion
