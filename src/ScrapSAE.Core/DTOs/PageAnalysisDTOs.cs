namespace ScrapSAE.Core.DTOs;

/// <summary>
/// Solicitud de análisis de página web para detección de estructura de catálogo de productos
/// </summary>
public class PageAnalysisRequest
{
    /// <summary>URL de la página a analizar (debe ser http o https)</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>URL opcional de un detalle de producto para forzar el análisis de campos de detalle en esta URL</summary>
    public string? ProductDetailUrl { get; set; }
}

/// <summary>
/// Nivel de confianza del análisis IA para un campo detectado
/// </summary>
public enum FieldConfidence
{
    High,
    Medium,
    Low
}

/// <summary>
/// Campo detectado por la IA en la estructura de la página (selector + confianza)
/// </summary>
public class DetectedField
{
    /// <summary>Nombre del campo (ej: "SKU", "Nombre", "Imagen", "Precio", "Características")</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Selector CSS sugerido para este campo (null si no se detectó)</summary>
    public string? Selector { get; set; }

    /// <summary>Nivel de confianza de la detección</summary>
    public FieldConfidence Confidence { get; set; } = FieldConfidence.Low;

    /// <summary>Nota explicativa del análisis para este campo</summary>
    public string? Note { get; set; }
}

/// <summary>
/// Estrategia de scraping recomendada por el análisis IA
/// </summary>
public class StrategyRecommendation
{
    /// <summary>Nombre de la estrategia: "Direct", "List", "Families"</summary>
    public string StrategyName { get; set; } = string.Empty;

    /// <summary>Prioridad (1 = más alta)</summary>
    public int Priority { get; set; } = 1;

    /// <summary>Razón por la que se recomienda esta estrategia</summary>
    public string? Reason { get; set; }
}

/// <summary>
/// Resultado del análisis IA de una página de proveedor para detección de estructura de catálogo
/// </summary>
public class PageAnalysisResult
{
    /// <summary>Indica si la página parece ser un catálogo de productos</summary>
    public bool IsProductCatalog { get; set; }

    /// <summary>Tipo de estrategia detectada (ej. Shopify, Generic)</summary>
    public string StrategyType { get; set; } = "Generic";

    /// <summary>Título detectado de la página</summary>
    public string? PageTitle { get; set; }

    /// <summary>Idioma detectado del contenido</summary>
    public string? DetectedLanguage { get; set; }

    /// <summary>Selector CSS del contenedor principal de la lista de productos</summary>
    public string? ProductContainerSelector { get; set; }

    /// <summary>Selector CSS de cada tarjeta/ítem de producto individual</summary>
    public string? ProductCardSelector { get; set; }

    /// <summary>Selector CSS del SKU/código de producto</summary>
    public string? SkuSelector { get; set; }

    /// <summary>Selector CSS del nombre del producto</summary>
    public string? NameSelector { get; set; }

    /// <summary>Selector CSS de la imagen del producto</summary>
    public string? ImageSelector { get; set; }

    /// <summary>Selector CSS del precio del producto</summary>
    public string? PriceSelector { get; set; }

    /// <summary>Selector CSS de las características/especificaciones del producto</summary>
    public string? CharacteristicsSelector { get; set; }

    /// <summary>Selectores secundarios sugeridos (alternativas y fallbacks por campo)</summary>
    public Dictionary<string, List<string>> SecondarySelectors { get; set; } = new();

    /// <summary>Estrategias de scraping recomendadas en orden de prioridad</summary>
    public List<StrategyRecommendation> RecommendedStrategies { get; set; } = new();

    /// <summary>Lista detallada de campos detectados con selector y nivel de confianza</summary>
    public List<DetectedField> DetectedFields { get; set; } = new();

    /// <summary>Resumen textual del análisis realizado por la IA</summary>
    public string? AnalysisSummary { get; set; }

    /// <summary>URL de la página que fue analizada</summary>
    public string? AnalyzedUrl { get; set; }

    /// <summary>Fecha y hora del análisis (UTC)</summary>
    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Producto de preview del scrape de prueba del wizard (solo datos en memoria, no persistido)
/// </summary>
public class WizardScrapePreviewProduct
{
    public string? Sku { get; set; }
    public string? Name { get; set; }
    public string? ImageUrl { get; set; }
    public string? Price { get; set; }
    public int CharacteristicsCount { get; set; }
    public string? SourceUrl { get; set; }

    /// <summary>Campos que fueron encontrados exitosamente</summary>
    public List<string> FoundFields { get; set; } = new();

    /// <summary>Campos que no pudieron ser extraídos</summary>
    public List<string> MissingFields { get; set; } = new();

    /// <summary>Resumen de campos encontrados para mostrar en el DataGrid</summary>
    public string FoundFieldsSummary => FoundFields.Count > 0
        ? string.Join(", ", FoundFields)
        : "(ninguno)";
}
