using Microsoft.Extensions.Logging;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Interfaces;
using System.Text.Json;


namespace ScrapSAE.Api.Services;


/// <summary>
/// Servicio para analizar ejecuciones de scraping y generar sugerencias de mejora
/// </summary>
public class PostExecutionAnalyzerService : IPostExecutionAnalyzer
{
    private readonly ILogger<PostExecutionAnalyzerService> _logger;
    private readonly ISyncLogService _syncLogService;
    private readonly IAIProcessorService _aiProcessor;
    
    public PostExecutionAnalyzerService(
        ILogger<PostExecutionAnalyzerService> logger,
        ISyncLogService syncLogService,
        IAIProcessorService aiProcessor)
    {
        _logger = logger;
        _syncLogService = syncLogService;
        _aiProcessor = aiProcessor;
    }
    
    public async Task<PostExecutionAnalysisResult> AnalyzeExecutionAsync(
        Guid siteId,
        ScrapeExecutionMetrics metrics,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Iniciando análisis post-ejecución para sitio {SiteId}", siteId);
        
        var result = new PostExecutionAnalysisResult
        {
            SiteId = siteId,
            TotalProducts = metrics.ProductsFound + metrics.ProductsSkipped,
            SuccessfulExtractions = metrics.ProductsFound,
            FailedExtractions = metrics.ProductsSkipped
        };
        
        // Analizar patrones de fallo en selectores
        var failurePatterns = AnalyzeSelectorFailures(metrics);
        result.FailurePatterns.AddRange(failurePatterns);
        
        // Generar sugerencias basadas en los patrones
        var suggestions = GenerateSuggestions(metrics, failurePatterns);
        result.Suggestions.AddRange(suggestions);
        
        // Si hay fallos complejos, consultar a IA
        if (failurePatterns.Any(p => p.FailureRate > 50))
        {
            try
            {
                var aiSuggestions = await GetAISuggestionsAsync(siteId, metrics, failurePatterns, cancellationToken);
                result.Suggestions.AddRange(aiSuggestions);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error obteniendo sugerencias de IA");
            }
        }
        
        // Determinar si requiere intervención manual
        result.RequiresManualIntervention = 
            result.SuccessRate < 30 || 
            failurePatterns.Any(p => p.PatternType == "blocked");
        
        // Generar resumen
        result.Summary = GenerateSummary(result);
        
        _logger.LogInformation("Análisis completado: {Summary}", result.Summary);
        
        return result;
    }
    
    public async Task<IEnumerable<SyncLog>> GetRecentLogsAsync(Guid siteId, int count = 100)
    {
        var from = DateTime.UtcNow.AddDays(-7);
        var to = DateTime.UtcNow;
        
        var logs = await _syncLogService.GetLogsAsync(from, to);
        return logs
            .Where(l => l.SiteId == siteId)
            .OrderByDescending(l => l.CreatedAt)
            .Take(count);
    }
    
    private List<FailurePattern> AnalyzeSelectorFailures(ScrapeExecutionMetrics metrics)
    {
        var patterns = new List<FailurePattern>();
        
        foreach (var (name, selector) in metrics.SelectorMetrics)
        {
            if (selector.SuccessRate < 80 && selector.AttemptCount > 5)
            {
                patterns.Add(new FailurePattern
                {
                    PatternType = "selector_failure",
                    Description = $"Selector '{name}' tiene baja tasa de éxito",
                    OccurrenceCount = selector.FailureCount,
                    FailureRate = 100 - selector.SuccessRate,
                    AffectedSelector = selector.SelectorValue
                });
            }
        }
        
        // Detectar timeouts
        if (metrics.TimeoutCount > metrics.TotalPagesVisited * 0.1)
        {
            patterns.Add(new FailurePattern
            {
                PatternType = "timeout",
                Description = "Alto número de timeouts detectado",
                OccurrenceCount = metrics.TimeoutCount,
                FailureRate = (double)metrics.TimeoutCount / Math.Max(1, metrics.TotalPagesVisited) * 100
            });
        }
        
        // Detectar posible bloqueo
        if (metrics.NavigationErrorCount > metrics.TotalPagesVisited * 0.3)
        {
            patterns.Add(new FailurePattern
            {
                PatternType = "blocked",
                Description = "Posible bloqueo detectado - Alto número de errores de navegación",
                OccurrenceCount = metrics.NavigationErrorCount,
                FailureRate = (double)metrics.NavigationErrorCount / Math.Max(1, metrics.TotalPagesVisited) * 100
            });
        }
        
        return patterns;
    }
    
    private List<ConfigurationSuggestion> GenerateSuggestions(
        ScrapeExecutionMetrics metrics, 
        List<FailurePattern> patterns)
    {
        var suggestions = new List<ConfigurationSuggestion>();
        
        foreach (var pattern in patterns)
        {
            switch (pattern.PatternType)
            {
                case "selector_failure":
                    suggestions.Add(new ConfigurationSuggestion
                    {
                        SuggestionType = "selector_update",
                        Description = $"Actualizar selector que falla frecuentemente",
                        PropertyName = pattern.AffectedSelector,
                        CurrentValue = pattern.AffectedSelector,
                        Confidence = 0.7,
                        AutoApplicable = false // Requiere análisis de IA
                    });
                    break;
                    
                case "timeout":
                    suggestions.Add(new ConfigurationSuggestion
                    {
                        SuggestionType = "performance_tuning",
                        Description = "Incrementar timeouts y agregar delays entre páginas",
                        PropertyName = "NavigationTimeout",
                        CurrentValue = "30000",
                        SuggestedValue = "60000",
                        Confidence = 0.8,
                        AutoApplicable = true
                    });
                    break;
                    
                case "blocked":
                    suggestions.Add(new ConfigurationSuggestion
                    {
                        SuggestionType = "stealth_update",
                        Description = "Cambiar User-Agent y agregar más delays humanos",
                        Confidence = 0.6,
                        AutoApplicable = false
                    });
                    break;
            }
        }
        
        // Sugerencias basadas en métricas generales
        if (metrics.AveragePageLoadTimeMs > 5000)
        {
            suggestions.Add(new ConfigurationSuggestion
            {
                SuggestionType = "rate_limit",
                Description = "El sitio responde lento, reducir velocidad de scraping",
                PropertyName = "DelayBetweenPages",
                SuggestedValue = "3000",
                Confidence = 0.9,
                AutoApplicable = true
            });
        }
        
        return suggestions;
    }
    
    private async Task<List<ConfigurationSuggestion>> GetAISuggestionsAsync(
        Guid siteId,
        ScrapeExecutionMetrics metrics,
        List<FailurePattern> patterns,
        CancellationToken cancellationToken)
    {
        var suggestions = new List<ConfigurationSuggestion>();
        
        // Para cada selector problemático, pedir sugerencia de IA
        foreach (var pattern in patterns.Where(p => p.PatternType == "selector_failure"))
        {
            try
            {
                var request = new SelectorAnalysisRequest
                {
                    Notes = $"Selector actual: {pattern.AffectedSelector}\nEste selector falla {pattern.FailureRate:F1}% de las veces ({pattern.OccurrenceCount} fallos)"
                };

                
                var aiResult = await _aiProcessor.AnalyzeSelectorsAsync(request, cancellationToken);
                
                // Extraer sugerencias de selectores del resultado
                var extractedSuggestions = ExtractSuggestionsFromAIResult(aiResult, pattern);
                suggestions.AddRange(extractedSuggestions);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error consultando IA para selector");
            }
        }
        
        return suggestions;
    }
    
    private List<ConfigurationSuggestion> ExtractSuggestionsFromAIResult(SelectorSuggestion aiResult, FailurePattern pattern)
    {
        var suggestions = new List<ConfigurationSuggestion>();
        
        // Extraer cada selector sugerido por la IA
        if (!string.IsNullOrEmpty(aiResult.TitleSelector))
        {
            suggestions.Add(CreateSuggestion("TitleSelector", aiResult.TitleSelector, aiResult, pattern));
        }
        if (!string.IsNullOrEmpty(aiResult.PriceSelector))
        {
            suggestions.Add(CreateSuggestion("PriceSelector", aiResult.PriceSelector, aiResult, pattern));
        }
        if (!string.IsNullOrEmpty(aiResult.SkuSelector))
        {
            suggestions.Add(CreateSuggestion("SkuSelector", aiResult.SkuSelector, aiResult, pattern));
        }
        if (!string.IsNullOrEmpty(aiResult.ImageSelector))
        {
            suggestions.Add(CreateSuggestion("ImageSelector", aiResult.ImageSelector, aiResult, pattern));
        }
        if (!string.IsNullOrEmpty(aiResult.ProductListClassPrefix))
        {
            suggestions.Add(CreateSuggestion("ProductListClassPrefix", aiResult.ProductListClassPrefix, aiResult, pattern));
        }
        if (!string.IsNullOrEmpty(aiResult.ProductCardClassPrefix))
        {
            suggestions.Add(CreateSuggestion("ProductCardClassPrefix", aiResult.ProductCardClassPrefix, aiResult, pattern));
        }
        
        return suggestions;
    }
    
    private ConfigurationSuggestion CreateSuggestion(string propertyName, string value, SelectorSuggestion aiResult, FailurePattern pattern)
    {
        return new ConfigurationSuggestion
        {
            SuggestionType = "ai_selector_update",
            Description = aiResult.Reasoning ?? "Sugerencia de IA",
            PropertyName = propertyName,
            CurrentValue = pattern.AffectedSelector,
            SuggestedValue = value,
            Confidence = (double)(aiResult.ConfidenceScore ?? 0.75m),
            AutoApplicable = (aiResult.ConfidenceScore ?? 0) >= 0.7m
        };
    }

    
    private string GenerateSummary(PostExecutionAnalysisResult result)
    {
        var parts = new List<string>
        {
            $"Tasa de éxito: {result.SuccessRate:F1}%"
        };
        
        if (result.FailurePatterns.Count > 0)
        {
            parts.Add($"{result.FailurePatterns.Count} patrones de fallo detectados");
        }
        
        if (result.Suggestions.Count > 0)
        {
            var autoApplicable = result.Suggestions.Count(s => s.AutoApplicable);
            parts.Add($"{result.Suggestions.Count} sugerencias ({autoApplicable} auto-aplicables)");
        }
        
        if (result.RequiresManualIntervention)
        {
            parts.Add("⚠️ Requiere intervención manual");
        }
        
        return string.Join(" | ", parts);
    }
}
