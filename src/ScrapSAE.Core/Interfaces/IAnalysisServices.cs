using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;

namespace ScrapSAE.Core.Interfaces;

/// <summary>
/// Resultado del análisis post-ejecución
/// </summary>
public class PostExecutionAnalysisResult
{
    public Guid SiteId { get; set; }
    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
    public int TotalProducts { get; set; }
    public int SuccessfulExtractions { get; set; }
    public int FailedExtractions { get; set; }
    public double SuccessRate => TotalProducts > 0 ? (double)SuccessfulExtractions / TotalProducts * 100 : 0;
    
    /// <summary>
    /// Patrones de fallo detectados (ej: "selector X falló 80% de las veces")
    /// </summary>
    public List<FailurePattern> FailurePatterns { get; set; } = new();
    
    /// <summary>
    /// Sugerencias de mejora generadas por el análisis
    /// </summary>
    public List<ConfigurationSuggestion> Suggestions { get; set; } = new();
    
    /// <summary>
    /// Indica si se requiere intervención manual
    /// </summary>
    public bool RequiresManualIntervention { get; set; }
    
    /// <summary>
    /// Resumen del análisis
    /// </summary>
    public string? Summary { get; set; }
}

public class FailurePattern
{
    public string PatternType { get; set; } = string.Empty; // "selector_failure", "timeout", "blocked", etc.
    public string Description { get; set; } = string.Empty;
    public int OccurrenceCount { get; set; }
    public double FailureRate { get; set; }
    public string? AffectedSelector { get; set; }
    public string? SampleUrl { get; set; }
}

public class ConfigurationSuggestion
{
    public string SuggestionType { get; set; } = string.Empty; // "selector_update", "strategy_change", "rate_limit", etc.
    public string Description { get; set; } = string.Empty;
    public string? PropertyName { get; set; }
    public string? CurrentValue { get; set; }
    public string? SuggestedValue { get; set; }
    public double Confidence { get; set; } // 0-1
    public bool AutoApplicable { get; set; } // Si se puede aplicar automáticamente
}

/// <summary>
/// Métricas de rendimiento de una ejecución de scraping
/// </summary>
public class ScrapeExecutionMetrics
{
    public Guid SiteId { get; set; }
    public Guid ExecutionId { get; set; } = Guid.NewGuid();
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public TimeSpan Duration => CompletedAt.HasValue ? CompletedAt.Value - StartedAt : TimeSpan.Zero;
    
    // Métricas de navegación
    public int TotalPagesVisited { get; set; }
    public double AveragePageLoadTimeMs { get; set; }
    public int TimeoutCount { get; set; }
    public int NavigationErrorCount { get; set; }
    
    // Métricas de extracción
    public int TotalSelectorsUsed { get; set; }
    public int SelectorSuccessCount { get; set; }
    public int SelectorFailureCount { get; set; }
    public double SelectorSuccessRate => TotalSelectorsUsed > 0 ? (double)SelectorSuccessCount / TotalSelectorsUsed * 100 : 0;
    
    // Métricas de productos
    public int ProductsFound { get; set; }
    public int ProductsWithPrice { get; set; }
    public int ProductsWithSku { get; set; }
    public int ProductsSkipped { get; set; }
    
    // Detalles por selector
    public Dictionary<string, SelectorMetric> SelectorMetrics { get; set; } = new();
}

public class SelectorMetric
{
    public string SelectorName { get; set; } = string.Empty;
    public string SelectorValue { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public double SuccessRate => AttemptCount > 0 ? (double)SuccessCount / AttemptCount * 100 : 0;
    public double AverageExecutionTimeMs { get; set; }
}

/// <summary>
/// Interface para el analizador post-ejecución
/// </summary>
public interface IPostExecutionAnalyzer
{
    /// <summary>
    /// Analiza los logs y métricas de una ejecución completada
    /// </summary>
    Task<PostExecutionAnalysisResult> AnalyzeExecutionAsync(
        Guid siteId, 
        ScrapeExecutionMetrics metrics,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Obtiene los logs recientes de un sitio para análisis
    /// </summary>
    Task<IEnumerable<SyncLog>> GetRecentLogsAsync(Guid siteId, int count = 100);
}

/// <summary>
/// Interface para actualizar configuración automáticamente
/// </summary>
public interface IConfigurationUpdater
{
    /// <summary>
    /// Aplica sugerencias de configuración automáticamente
    /// </summary>
    Task<bool> ApplySuggestionsAsync(
        Guid siteId, 
        IEnumerable<ConfigurationSuggestion> suggestions,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Actualiza un selector específico
    /// </summary>
    Task<bool> UpdateSelectorAsync(
        Guid siteId, 
        string selectorName, 
        string newValue,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Obtiene el historial de cambios de configuración
    /// </summary>
    Task<IEnumerable<ConfigurationChange>> GetConfigurationHistoryAsync(Guid siteId);
}

public class ConfigurationChange
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SiteId { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    public string PropertyName { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string ChangeSource { get; set; } = string.Empty; // "auto", "manual", "ai_suggestion"
    public string? Reason { get; set; }
}

/// <summary>
/// Interface para recolectar métricas de rendimiento durante el scraping
/// </summary>
public interface IPerformanceMetricsCollector
{
    /// <summary>
    /// Inicia una nueva sesión de métricas
    /// </summary>
    ScrapeExecutionMetrics StartSession(Guid siteId);
    
    /// <summary>
    /// Registra una visita a página
    /// </summary>
    void RecordPageVisit(Guid executionId, string url, double loadTimeMs, bool success);
    
    /// <summary>
    /// Registra uso de un selector
    /// </summary>
    void RecordSelectorUsage(Guid executionId, string selectorName, string selectorValue, bool success, double executionTimeMs);
    
    /// <summary>
    /// Registra extracción de producto
    /// </summary>
    void RecordProductExtraction(Guid executionId, bool hasPrice, bool hasSku, bool success);
    
    /// <summary>
    /// Finaliza la sesión y obtiene las métricas completas
    /// </summary>
    ScrapeExecutionMetrics EndSession(Guid executionId);
    
    /// <summary>
    /// Obtiene las métricas de una ejecución
    /// </summary>
    ScrapeExecutionMetrics? GetMetrics(Guid executionId);
}
