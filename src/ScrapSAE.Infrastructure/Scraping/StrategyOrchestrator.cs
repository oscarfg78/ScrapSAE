using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Interfaces;

namespace ScrapSAE.Infrastructure.Scraping;

/// <summary>
/// Orquestador que ejecuta múltiples estrategias de scraping en orden de prioridad
/// </summary>
public class StrategyOrchestrator : IStrategyOrchestrator
{
    private readonly ILogger<StrategyOrchestrator> _logger;
    private readonly IEnumerable<IScrapingStrategy> _strategies;

    public StrategyOrchestrator(
        ILogger<StrategyOrchestrator> logger,
        IEnumerable<IScrapingStrategy> strategies)
    {
        _logger = logger;
        _strategies = strategies;
    }

    public async Task<List<ScrapedProduct>> ExecuteStrategiesAsync(
        object pageObj,
        SiteProfile site,
        Guid executionId,
        CancellationToken cancellationToken = default)
    {
        var page = (IPage)pageObj;
        _logger.LogInformation(
            "[Orchestrator] Iniciando ejecución de estrategias para sitio {SiteName}",
            site.Name
        );

        // Obtener las estrategias habilitadas y ordenadas por prioridad
        var enabledStrategies = GetEnabledStrategies(site);
        
        if (!enabledStrategies.Any())
        {
            _logger.LogWarning("[Orchestrator] No hay estrategias habilitadas para el sitio {SiteName}", site.Name);
            return new List<ScrapedProduct>();
        }

        // Intentar cada estrategia en orden
        foreach (var strategyDef in enabledStrategies)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var strategy = _strategies.FirstOrDefault(s => s.StrategyName == strategyDef.StrategyName);
            
            if (strategy == null)
            {
                _logger.LogWarning(
                    "[Orchestrator] Estrategia no encontrada: {StrategyName}",
                    strategyDef.StrategyName
                );
                continue;
            }

            try
            {
                _logger.LogInformation(
                    "[Orchestrator] Ejecutando estrategia {StrategyName} (Prioridad: {Priority})",
                    strategy.StrategyName,
                    strategyDef.Priority
                );

                var products = await strategy.ExecuteAsync(page, site, executionId, cancellationToken);

                if (products.Any())
                {
                    _logger.LogInformation(
                        "[Orchestrator] Estrategia {StrategyName} exitosa: {Count} productos extraídos",
                        strategy.StrategyName,
                        products.Count
                    );
                    return products;
                }
                else
                {
                    _logger.LogInformation(
                        "[Orchestrator] Estrategia {StrategyName} no extrajo productos, intentando siguiente...",
                        strategy.StrategyName
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[Orchestrator] Error en estrategia {StrategyName}, intentando siguiente...",
                    strategy.StrategyName
                );
            }
        }

        _logger.LogWarning("[Orchestrator] Ninguna estrategia tuvo éxito para el sitio {SiteName}", site.Name);
        return new List<ScrapedProduct>();
    }

    private List<ScrapingStrategyDefinition> GetEnabledStrategies(SiteProfile site)
    {
        // Si el sitio tiene estrategias configuradas, usarlas
        if (site.Strategies != null && site.Strategies.Any())
        {
            var strategies = site.Strategies
                .Where(s => s.IsEnabled)
                .OrderBy(s => s.Priority)
                .ToList();

            // Auto-inject List strategy si no está pero tenemos selectores de lista en el diccionario
            var hasListStrategy = strategies.Any(s => s.StrategyName.Equals("List", StringComparison.OrdinalIgnoreCase));
            bool hasListSelectors = false;
            if (site.Selectors != null)
            {
                try
                {
                    var selectorsJson = System.Text.Json.JsonSerializer.Serialize(site.Selectors);
                    var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(selectorsJson);
                    hasListSelectors = dict != null && dict.ContainsKey("productContainer") && !string.IsNullOrWhiteSpace(dict["productContainer"]);
                }
                catch { /* Ignore */ }
            }
            if (!hasListStrategy && hasListSelectors)
            {
                _logger.LogInformation("[Orchestrator] Inyectando ListStrategy automáticamente porque existen selectores de lista pero no estaba habilitada explícitamente.");
                strategies.Add(new ScrapingStrategyDefinition { StrategyName = "List", Priority = 2, IsEnabled = true });
                strategies = strategies.OrderBy(s => s.Priority).ToList();
            }

            return strategies;
        }

        // Si no hay estrategias configuradas, usar un orden por defecto
        _logger.LogInformation("[Orchestrator] Usando orden de estrategias por defecto");
        return new List<ScrapingStrategyDefinition>
        {
            new ScrapingStrategyDefinition { StrategyName = "Direct", Priority = 1, IsEnabled = true },
            new ScrapingStrategyDefinition { StrategyName = "List", Priority = 2, IsEnabled = true },
            new ScrapingStrategyDefinition { StrategyName = "Families", Priority = 3, IsEnabled = true }
        };
    }
}
