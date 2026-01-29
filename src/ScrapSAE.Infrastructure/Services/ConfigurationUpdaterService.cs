using Microsoft.Extensions.Logging;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Interfaces;

namespace ScrapSAE.Infrastructure.Services;

/// <summary>
/// Servicio que aplica automáticamente las mejoras sugeridas a la configuración del sitio
/// </summary>
public class ConfigurationUpdaterService : IConfigurationUpdaterService
{
    private readonly ILogger<ConfigurationUpdaterService> _logger;
    private readonly IStagingService _stagingService;
    private const double MinimumConfidence = 0.8;

    public ConfigurationUpdaterService(
        ILogger<ConfigurationUpdaterService> logger,
        IStagingService stagingService)
    {
        _logger = logger;
        _stagingService = stagingService;
    }

    public async Task ApplySuggestionsAsync(Guid siteId, IEnumerable<Suggestion> suggestions)
    {
        var appliedCount = 0;
        
        foreach (var suggestion in suggestions)
        {
            // Solo aplicar sugerencias con alta confianza
            if (suggestion.Confidence < MinimumConfidence)
            {
                _logger.LogInformation(
                    "[ConfigUpdater] Sugerencia ignorada por baja confianza ({Confidence}): {Suggestion}",
                    suggestion.Confidence,
                    suggestion.Message
                );
                continue;
            }

            try
            {
                switch (suggestion.Type)
                {
                    case "ai_selector_update":
                        await ApplySelectorUpdateAsync(siteId, suggestion);
                        appliedCount++;
                        break;
                    
                    case "strategy_reorder":
                        await ApplyStrategyReorderAsync(siteId, suggestion);
                        appliedCount++;
                        break;
                    
                    default:
                        _logger.LogWarning(
                            "[ConfigUpdater] Tipo de sugerencia desconocido: {Type}",
                            suggestion.Type
                        );
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[ConfigUpdater] Error al aplicar sugerencia: {Suggestion}",
                    suggestion.Message
                );
            }
        }

        _logger.LogInformation(
            "[ConfigUpdater] Se aplicaron {Count} sugerencias de {Total} para el sitio {SiteId}",
            appliedCount,
            suggestions.Count(),
            siteId
        );
    }

    public async Task UpdateSelectorAsync(Guid siteId, string selectorKey, string newSelector)
    {
        try
        {
            // Obtener el sitio actual
            var sites = await _stagingService.GetActiveSitesAsync();
            var site = sites.FirstOrDefault(s => s.Id == siteId);
            
            if (site == null)
            {
                _logger.LogError("[ConfigUpdater] Sitio no encontrado: {SiteId}", siteId);
                return;
            }

            // Mover el selector actual a secundarios
            if (site.Selectors is Dictionary<string, object> selectors && 
                selectors.ContainsKey(selectorKey))
            {
                var currentSelector = selectors[selectorKey]?.ToString();
                
                if (!string.IsNullOrEmpty(currentSelector))
                {
                    // Agregar a la lista de selectores secundarios
                    if (!site.SecondarySelectors.ContainsKey(selectorKey))
                    {
                        site.SecondarySelectors[selectorKey] = new List<string>();
                    }
                    
                    if (!site.SecondarySelectors[selectorKey].Contains(currentSelector))
                    {
                        site.SecondarySelectors[selectorKey].Add(currentSelector);
                    }
                }
                
                // Actualizar el selector primario
                selectors[selectorKey] = newSelector;
                
                _logger.LogInformation(
                    "[ConfigUpdater] Selector actualizado: {Key} = {NewSelector} (antiguo guardado en secundarios)",
                    selectorKey,
                    newSelector
                );
            }
            
            // TODO: Guardar los cambios en la base de datos (Supabase)
            // Esto requeriría un método en IStagingService como UpdateSiteAsync
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ConfigUpdater] Error al actualizar selector");
        }
    }

    private async Task ApplySelectorUpdateAsync(Guid siteId, Suggestion suggestion)
    {
        // Extraer información de la sugerencia
        // Formato esperado en suggestion.Data: { "selectorKey": "productName", "newSelector": "h1.title" }
        
        if (suggestion.Data is Dictionary<string, object> data &&
            data.ContainsKey("selectorKey") &&
            data.ContainsKey("newSelector"))
        {
            var selectorKey = data["selectorKey"].ToString();
            var newSelector = data["newSelector"].ToString();
            
            if (!string.IsNullOrEmpty(selectorKey) && !string.IsNullOrEmpty(newSelector))
            {
                await UpdateSelectorAsync(siteId, selectorKey, newSelector);
            }
        }
        else
        {
            _logger.LogWarning(
                "[ConfigUpdater] Sugerencia de selector mal formada: {Data}",
                suggestion.Data
            );
        }
    }

    private async Task ApplyStrategyReorderAsync(Guid siteId, Suggestion suggestion)
    {
        try
        {
            // Obtener el sitio actual
            var sites = await _stagingService.GetActiveSitesAsync();
            var site = sites.FirstOrDefault(s => s.Id == siteId);
            
            if (site == null)
            {
                _logger.LogError("[ConfigUpdater] Sitio no encontrado: {SiteId}", siteId);
                return;
            }

            // Extraer el nuevo orden de estrategias
            // Formato esperado: { "newOrder": ["Families", "List", "Direct"] }
            if (suggestion.Data is Dictionary<string, object> data &&
                data.ContainsKey("newOrder") &&
                data["newOrder"] is List<string> newOrder)
            {
                // Reordenar las estrategias según la nueva prioridad
                for (int i = 0; i < newOrder.Count; i++)
                {
                    var strategyName = newOrder[i];
                    var strategy = site.Strategies.FirstOrDefault(s => s.StrategyName == strategyName);
                    
                    if (strategy != null)
                    {
                        strategy.Priority = i + 1;
                    }
                }
                
                _logger.LogInformation(
                    "[ConfigUpdater] Estrategias reordenadas para sitio {SiteId}: {NewOrder}",
                    siteId,
                    string.Join(" > ", newOrder)
                );
                
                // TODO: Guardar los cambios en la base de datos
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ConfigUpdater] Error al reordenar estrategias");
        }
    }
}
