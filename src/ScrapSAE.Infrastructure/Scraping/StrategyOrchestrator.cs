using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Interfaces;
using ScrapSAE.Infrastructure.Scraping.Strategies;

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
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _strategies = strategies ?? throw new ArgumentNullException(nameof(strategies));
    }

    public async Task<List<ScrapedProduct>> ExecuteStrategiesAsync(
        object pageObj,
        SiteProfile site,
        Guid executionId,
        ScrapeExecutionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var page = (IPage)pageObj;
        _logger.LogInformation(
            "[Orchestrator] Iniciando ejecución de estrategias para sitio {SiteName}",
            site.Name
        );

        var enabledStrategies = GetEnabledStrategies(site);
        
        if (!enabledStrategies.Any())
        {
            _logger.LogWarning("[Orchestrator] No hay estrategias habilitadas para el sitio {SiteName}", site.Name);
            return new List<ScrapedProduct>();
        }

        foreach (var strategyDef in enabledStrategies)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var strategy = _strategies.FirstOrDefault(s => s.StrategyName.Equals(strategyDef.StrategyName, StringComparison.OrdinalIgnoreCase));
            
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

                var products = await strategy.ExecuteAsync(page, site, executionId, context, cancellationToken);
                var validProducts = products.Where(p => SelectorCombinator.IsValidProduct(p)).ToList();

                if (validProducts.Any())
                {
                    _logger.LogInformation(
                        "[Orchestrator] Estrategia {StrategyName} exitosa: {Count} productos válidos extraídos",
                        strategy.StrategyName,
                        validProducts.Count
                    );
                    context?.LogTracker?.AddLog("Orchestrator", details: $"Estrategia {strategy.StrategyName} exitosa: {validProducts.Count} productos extraídos");
                    return validProducts;
                }
                else
                {
                    _logger.LogInformation(
                        "[Orchestrator] Estrategia {StrategyName} no extrajo productos válidos, intentando siguiente...",
                        strategy.StrategyName
                    );
                    context?.LogTracker?.AddLog("Orchestrator", details: $"Estrategia {strategy.StrategyName} no extrajo productos válidos");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[Orchestrator] Error en estrategia {StrategyName}, intentando siguiente...",
                    strategy.StrategyName
                );
                context?.LogTracker?.AddLog("Orchestrator", error: $"Error en estrategia {strategy.StrategyName}: {ex.Message}");
            }
        }

        _logger.LogWarning("[Orchestrator] Ninguna estrategia tuvo éxito para el sitio {SiteName}", site.Name);
        return new List<ScrapedProduct>();
    }

    private List<ScrapingStrategyDefinition> GetEnabledStrategies(SiteProfile site)
    {
        var strategies = new List<ScrapingStrategyDefinition>();

        if (site.Strategies != null && site.Strategies.Any())
        {
            strategies = site.Strategies
                .Where(s => s.IsEnabled)
                .ToList();
        }

        bool hasListSelectors = SelectorCombinator.GetDualSelector(site, "productContainer") != null ||
                                SelectorCombinator.GetDualSelector(site, "productCard") != null;

        var hasListStrategy = strategies.Any(s => s.StrategyName.Equals("List", StringComparison.OrdinalIgnoreCase));

        if (!hasListStrategy && hasListSelectors)
        {
            _logger.LogInformation("[Orchestrator] Inyectando ListStrategy automáticamente porque existen selectores de lista.");
            strategies.Add(new ScrapingStrategyDefinition { StrategyName = "List", Priority = 1, IsEnabled = true });
        }

        // Always prioritize List strategy over Direct strategy if List strategy is enabled or list selectors exist
        if (hasListSelectors || hasListStrategy)
        {
            foreach (var s in strategies)
            {
                if (s.StrategyName.Equals("List", StringComparison.OrdinalIgnoreCase))
                    s.Priority = 1;
                else if (s.StrategyName.Equals("Direct", StringComparison.OrdinalIgnoreCase))
                    s.Priority = 2;
                else if (s.StrategyName.Equals("Families", StringComparison.OrdinalIgnoreCase))
                    s.Priority = 3;
            }
        }

        if (!strategies.Any())
        {
            _logger.LogInformation("[Orchestrator] Usando orden de estrategias por defecto (List -> Direct)");
            strategies = new List<ScrapingStrategyDefinition>
            {
                new() { StrategyName = "List", Priority = 1, IsEnabled = true },
                new() { StrategyName = "Direct", Priority = 2, IsEnabled = true }
            };
        }

        return strategies.OrderBy(s => s.Priority).ToList();
    }
}
