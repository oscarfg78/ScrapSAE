using ScrapSAE.Core.Entities;

namespace ScrapSAE.Core.Interfaces;

/// <summary>
/// Tipo de URL para clasificar páginas de ejemplo
/// </summary>
public enum UrlType
{
    /// <summary>Página de detalle de producto individual</summary>
    ProductDetail,
    /// <summary>Página de listado con múltiples productos</summary>
    ProductListing,
    /// <summary>Página de subcategoría con enlaces a productos o más subcategorías</summary>
    Subcategory,
    /// <summary>Página de búsqueda con resultados</summary>
    SearchResults
}

/// <summary>
/// URL de ejemplo para aprendizaje
/// </summary>
public class ExampleUrl
{
    public string Url { get; set; } = string.Empty;
    public UrlType Type { get; set; }
    public DateTime LearnedAt { get; set; } = DateTime.UtcNow;
    public bool ProcessedSuccessfully { get; set; }
    public string? ExtractedHtmlSnippet { get; set; }
}

/// <summary>
/// Patrones aprendidos de las URLs de ejemplo
/// </summary>
public class LearnedPatterns
{
    public Guid SiteId { get; set; }
    public DateTime LearnedAt { get; set; } = DateTime.UtcNow;
    
    // Patrones de URL
    public string? ProductDetailUrlPattern { get; set; }   // ej: "/a/{id}/" o "/p/{slug}-id_{code}/"
    public string? ProductListingUrlPattern { get; set; }  // ej: "/c/.../id_pim{num}/?page={n}"
    public string? SubcategoryUrlPattern { get; set; }     // ej: "/c/.../id_pim{num}/"
    
    // Selectores aprendidos para detalle de producto
    public string? ProductTitleSelector { get; set; }
    public string? ProductPriceSelector { get; set; }
    public string? ProductSkuSelector { get; set; }
    public string? ProductImageSelector { get; set; }
    public string? ProductDescriptionSelector { get; set; }
    public string? BreadcrumbSelector { get; set; }
    
    // Selectores aprendidos para listados
    public string? ProductCardSelector { get; set; }
    public string? ProductLinkSelector { get; set; }
    public string? NextPageSelector { get; set; }
    public string? SubcategoryLinkSelector { get; set; }
    
    // Navegación
    public List<string> NavigationPath { get; set; } = new();  // Cómo llegar a productos
    public List<string> ExampleProductUrls { get; set; } = new();
    public List<string> ExampleListingUrls { get; set; } = new();
    
    // Confianza del aprendizaje
    public double ConfidenceScore { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// Resultado del análisis de una URL de ejemplo
/// </summary>
public class UrlAnalysisResult
{
    public string Url { get; set; } = string.Empty;
    public UrlType DetectedType { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    
    // Datos extraídos (si es producto)
    public string? Title { get; set; }
    public string? Price { get; set; }
    public string? Sku { get; set; }
    public string? ImageUrl { get; set; }
    
    // Selectores encontrados
    public Dictionary<string, string> FoundSelectors { get; set; } = new();
    
    // HTML relevante para análisis
    public string? HtmlSnippet { get; set; }
    public string? BreadcrumbPath { get; set; }
}

/// <summary>
/// Servicio de aprendizaje basado en URLs de ejemplo
/// </summary>
public interface ILearningService
{
    /// <summary>
    /// Aprende de una URL de ejemplo
    /// </summary>
    Task<UrlAnalysisResult> LearnFromUrlAsync(
        Guid siteId, 
        string url, 
        UrlType expectedType, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Aprende de múltiples URLs de ejemplo
    /// </summary>
    Task<List<UrlAnalysisResult>> LearnFromUrlsAsync(
        Guid siteId, 
        IEnumerable<ExampleUrl> urls, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Obtiene los patrones aprendidos para un sitio
    /// </summary>
    Task<LearnedPatterns?> GetLearnedPatternsAsync(Guid siteId);
    
    /// <summary>
    /// Usa IA para inferir selectores basándose en HTML de ejemplo
    /// </summary>
    Task<Dictionary<string, string>> InferSelectorsWithAIAsync(
        Guid siteId,
        string htmlSnippet,
        UrlType pageType,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Guarda los patrones aprendidos
    /// </summary>
    Task SaveLearnedPatternsAsync(Guid siteId, LearnedPatterns patterns);
}
