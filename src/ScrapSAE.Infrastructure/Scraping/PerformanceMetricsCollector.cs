using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ScrapSAE.Core.Interfaces;

namespace ScrapSAE.Infrastructure.Scraping;

/// <summary>
/// Recolector de métricas de rendimiento para el scraping
/// </summary>
public class PerformanceMetricsCollector : IPerformanceMetricsCollector
{
    private readonly ILogger<PerformanceMetricsCollector> _logger;
    private readonly ConcurrentDictionary<Guid, ScrapeExecutionMetrics> _sessions = new();
    
    public PerformanceMetricsCollector(ILogger<PerformanceMetricsCollector> logger)
    {
        _logger = logger;
    }
    
    public ScrapeExecutionMetrics StartSession(Guid siteId)
    {
        var metrics = new ScrapeExecutionMetrics
        {
            SiteId = siteId,
            StartedAt = DateTime.UtcNow
        };
        
        _sessions[metrics.ExecutionId] = metrics;
        _logger.LogInformation("Sesión de métricas iniciada: {ExecutionId} para sitio {SiteId}", 
            metrics.ExecutionId, siteId);
        
        return metrics;
    }
    
    public void RecordPageVisit(Guid executionId, string url, double loadTimeMs, bool success)
    {
        if (!_sessions.TryGetValue(executionId, out var metrics))
        {
            _logger.LogWarning("Sesión de métricas no encontrada: {ExecutionId}", executionId);
            return;
        }
        
        metrics.TotalPagesVisited++;
        
        // Calcular promedio incremental
        metrics.AveragePageLoadTimeMs = 
            ((metrics.AveragePageLoadTimeMs * (metrics.TotalPagesVisited - 1)) + loadTimeMs) / 
            metrics.TotalPagesVisited;
        
        if (!success)
        {
            metrics.NavigationErrorCount++;
        }
        
        _logger.LogDebug("Página visitada: {Url} - {LoadTimeMs}ms - Éxito: {Success}", 
            url, loadTimeMs, success);
    }
    
    public void RecordSelectorUsage(Guid executionId, string selectorName, string selectorValue, bool success, double executionTimeMs)
    {
        if (!_sessions.TryGetValue(executionId, out var metrics))
        {
            return;
        }
        
        metrics.TotalSelectorsUsed++;
        
        if (success)
        {
            metrics.SelectorSuccessCount++;
        }
        else
        {
            metrics.SelectorFailureCount++;
        }
        
        // Actualizar métricas del selector específico
        if (!metrics.SelectorMetrics.TryGetValue(selectorName, out var selectorMetric))
        {
            selectorMetric = new SelectorMetric
            {
                SelectorName = selectorName,
                SelectorValue = selectorValue
            };
            metrics.SelectorMetrics[selectorName] = selectorMetric;
        }
        
        selectorMetric.AttemptCount++;
        if (success)
        {
            selectorMetric.SuccessCount++;
        }
        else
        {
            selectorMetric.FailureCount++;
        }
        
        // Promedio incremental del tiempo de ejecución
        selectorMetric.AverageExecutionTimeMs = 
            ((selectorMetric.AverageExecutionTimeMs * (selectorMetric.AttemptCount - 1)) + executionTimeMs) / 
            selectorMetric.AttemptCount;
    }
    
    public void RecordProductExtraction(Guid executionId, bool hasPrice, bool hasSku, bool success)
    {
        if (!_sessions.TryGetValue(executionId, out var metrics))
        {
            return;
        }
        
        if (success)
        {
            metrics.ProductsFound++;
            if (hasPrice) metrics.ProductsWithPrice++;
            if (hasSku) metrics.ProductsWithSku++;
        }
        else
        {
            metrics.ProductsSkipped++;
        }
    }
    
    public ScrapeExecutionMetrics EndSession(Guid executionId)
    {
        if (!_sessions.TryGetValue(executionId, out var metrics))
        {
            _logger.LogWarning("Sesión de métricas no encontrada al finalizar: {ExecutionId}", executionId);
            return new ScrapeExecutionMetrics { ExecutionId = executionId };
        }
        
        metrics.CompletedAt = DateTime.UtcNow;
        
        _logger.LogInformation(
            "Sesión de métricas finalizada: {ExecutionId} - Duración: {Duration} - Productos: {Products} - Tasa éxito selectores: {Rate:F1}%",
            executionId, metrics.Duration, metrics.ProductsFound, metrics.SelectorSuccessRate);
        
        // Log de selectores problemáticos
        foreach (var selector in metrics.SelectorMetrics.Values.Where(s => s.SuccessRate < 50))
        {
            _logger.LogWarning(
                "Selector problemático: {Name} - Tasa éxito: {Rate:F1}% ({Success}/{Total})",
                selector.SelectorName, selector.SuccessRate, selector.SuccessCount, selector.AttemptCount);
        }
        
        return metrics;
    }
    
    public ScrapeExecutionMetrics? GetMetrics(Guid executionId)
    {
        return _sessions.TryGetValue(executionId, out var metrics) ? metrics : null;
    }
}
