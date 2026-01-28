using Microsoft.Extensions.Logging;
using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Interfaces;
using System.Text.Json;

namespace ScrapSAE.Api.Services;


/// <summary>
/// Servicio para actualizar configuración de sitios automáticamente
/// </summary>
public class ConfigurationUpdaterService : IConfigurationUpdater
{
    private readonly ILogger<ConfigurationUpdaterService> _logger;
    private readonly ISupabaseRestClient _supabase;
    private readonly List<ConfigurationChange> _changeHistory = new();
    
    public ConfigurationUpdaterService(
        ILogger<ConfigurationUpdaterService> logger,
        ISupabaseRestClient supabase)
    {
        _logger = logger;
        _supabase = supabase;
    }
    
    public async Task<bool> ApplySuggestionsAsync(
        Guid siteId,
        IEnumerable<ConfigurationSuggestion> suggestions,
        CancellationToken cancellationToken = default)
    {
        var autoApplicable = suggestions.Where(s => s.AutoApplicable && s.Confidence >= 0.7).ToList();
        
        if (autoApplicable.Count == 0)
        {
            _logger.LogInformation("No hay sugerencias auto-aplicables para sitio {SiteId}", siteId);
            return true;
        }
        
        _logger.LogInformation("Aplicando {Count} sugerencias a sitio {SiteId}", autoApplicable.Count, siteId);
        
        foreach (var suggestion in autoApplicable)
        {
            if (string.IsNullOrEmpty(suggestion.PropertyName) || string.IsNullOrEmpty(suggestion.SuggestedValue))
            {
                continue;
            }
            
            try
            {
                var success = await UpdateSelectorAsync(
                    siteId, 
                    suggestion.PropertyName, 
                    suggestion.SuggestedValue, 
                    cancellationToken);
                
                if (success)
                {
                    _logger.LogInformation(
                        "Aplicada sugerencia: {Property} = {Value} (confianza: {Confidence:F0}%)",
                        suggestion.PropertyName, suggestion.SuggestedValue, suggestion.Confidence * 100);
                    
                    _changeHistory.Add(new ConfigurationChange
                    {
                        SiteId = siteId,
                        PropertyName = suggestion.PropertyName,
                        OldValue = suggestion.CurrentValue,
                        NewValue = suggestion.SuggestedValue,
                        ChangeSource = "auto",
                        Reason = suggestion.Description
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error aplicando sugerencia {Property}", suggestion.PropertyName);
            }
        }
        
        return true;
    }
    
    public async Task<bool> UpdateSelectorAsync(
        Guid siteId,
        string selectorName,
        string newValue,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Obtener configuración actual
            var siteJson = await _supabase.GetAsync($"sites?id=eq.{siteId}&select=*");
            if (string.IsNullOrEmpty(siteJson))
            {
                _logger.LogWarning("Sitio no encontrado: {SiteId}", siteId);
                return false;
            }
            
            using var doc = JsonDocument.Parse(siteJson);
            var sites = doc.RootElement;
            if (sites.GetArrayLength() == 0)
            {
                return false;
            }
            
            var site = sites[0];
            var selectorsJson = site.TryGetProperty("selectors_json", out var sel) 
                ? sel.GetString() ?? "{}" 
                : "{}";
            
            // Parsear y actualizar selectores
            var selectors = JsonSerializer.Deserialize<Dictionary<string, object>>(selectorsJson) 
                ?? new Dictionary<string, object>();
            
            selectors[selectorName] = newValue;
            
            var newSelectorsJson = JsonSerializer.Serialize(selectors);
            
            // Actualizar en Supabase
            var updatePayload = JsonSerializer.Serialize(new { selectors_json = newSelectorsJson });
            await _supabase.PatchAsync($"sites?id=eq.{siteId}", updatePayload);
            
            _logger.LogInformation("Selector actualizado: {Name} = {Value} para sitio {SiteId}", 
                selectorName, newValue, siteId);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error actualizando selector {Name} para sitio {SiteId}", selectorName, siteId);
            return false;
        }
    }
    
    public Task<IEnumerable<ConfigurationChange>> GetConfigurationHistoryAsync(Guid siteId)
    {
        var history = _changeHistory
            .Where(c => c.SiteId == siteId)
            .OrderByDescending(c => c.ChangedAt)
            .AsEnumerable();
        
        return Task.FromResult(history);
    }
}
