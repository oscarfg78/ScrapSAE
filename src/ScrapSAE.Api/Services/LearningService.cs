using Microsoft.Extensions.Logging;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Interfaces;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScrapSAE.Api.Services;

/// <summary>
/// Servicio de aprendizaje que analiza URLs de ejemplo y extrae patrones
/// </summary>
public class LearningService : ILearningService
{
    private readonly ILogger<LearningService> _logger;
    private readonly ISupabaseRestClient _supabase;
    private readonly IAIProcessorService _aiProcessor;
    private readonly Dictionary<Guid, LearnedPatterns> _patternsCache = new();
    private const string TableName = "learned_patterns";
    
    public LearningService(
        ILogger<LearningService> logger,
        ISupabaseRestClient supabase,
        IAIProcessorService aiProcessor)
    {
        _logger = logger;
        _supabase = supabase;
        _aiProcessor = aiProcessor;
    }
    
    public async Task<UrlAnalysisResult> LearnFromUrlAsync(
        Guid siteId,
        string url,
        UrlType expectedType,
        CancellationToken cancellationToken = default)
    {
        var result = new UrlAnalysisResult
        {
            Url = url,
            DetectedType = expectedType
        };
        
        try
        {
            _logger.LogInformation("Analizando URL de ejemplo: {Url} (tipo esperado: {Type})", url, expectedType);
            
            // Detectar el tipo de URL basándose en el patrón
            result.DetectedType = DetectUrlType(url);
            
            // Almacenar patrón de URL
            await UpdateUrlPatternsAsync(siteId, url, result.DetectedType);
            
            result.Success = true;
            _logger.LogInformation("URL analizada exitosamente: {Url} -> {Type}", url, result.DetectedType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analizando URL: {Url}", url);
            result.Success = false;
            result.Error = ex.Message;
        }
        
        return result;
    }
    
    public async Task<List<UrlAnalysisResult>> LearnFromUrlsAsync(
        Guid siteId,
        IEnumerable<ExampleUrl> urls,
        CancellationToken cancellationToken = default)
    {
        var results = new List<UrlAnalysisResult>();
        
        foreach (var exampleUrl in urls)
        {
            var result = await LearnFromUrlAsync(siteId, exampleUrl.Url, exampleUrl.Type, cancellationToken);
            results.Add(result);
        }
        
        // Después de procesar todas las URLs, intentar inferir selectores con IA
        if (results.Any(r => r.Success))
        {
            await InferPatternFromExamplesAsync(siteId, results, cancellationToken);
        }
        
        return results;
    }
    
    public async Task<LearnedPatterns?> GetLearnedPatternsAsync(Guid siteId)
    {
        // Primero buscar en cache
        if (_patternsCache.TryGetValue(siteId, out var cached))
            return cached;
        
        // Si no está en cache, buscar en Supabase
        try
        {
            var query = $"{TableName}?site_id=eq.{siteId}&select=*";
            var results = await _supabase.GetAsync<LearnedPatternsDto>(query);
            var dto = results.FirstOrDefault();
            
            if (dto != null)
            {
                var patterns = MapFromDto(dto);
                _patternsCache[siteId] = patterns;
                return patterns;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error cargando patrones de Supabase, usando cache");
        }
        
        return null;
    }
    
    public async Task<Dictionary<string, string>> InferSelectorsWithAIAsync(
        Guid siteId,
        string htmlSnippet,
        UrlType pageType,
        CancellationToken cancellationToken = default)
    {
        var selectors = new Dictionary<string, string>();
        
        try
        {
            var request = new SelectorAnalysisRequest
            {
                HtmlSnippet = htmlSnippet,
                Notes = $"Tipo de página: {pageType}. Identifica los selectores CSS para: título del producto, precio, SKU/código de artículo, imagen principal, descripción."
            };
            
            var result = await _aiProcessor.AnalyzeSelectorsAsync(request, cancellationToken);
            
            if (!string.IsNullOrEmpty(result.TitleSelector))
                selectors["title"] = result.TitleSelector;
            if (!string.IsNullOrEmpty(result.PriceSelector))
                selectors["price"] = result.PriceSelector;
            if (!string.IsNullOrEmpty(result.SkuSelector))
                selectors["sku"] = result.SkuSelector;
            if (!string.IsNullOrEmpty(result.ImageSelector))
                selectors["image"] = result.ImageSelector;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error infiriendo selectores con IA");
        }
        
        return selectors;
    }
    
    public async Task SaveLearnedPatternsAsync(Guid siteId, LearnedPatterns patterns)
    {
        patterns.SiteId = siteId;
        patterns.LearnedAt = DateTime.UtcNow;
        _patternsCache[siteId] = patterns;
        
        // Persistir en Supabase
        try
        {
            var dto = MapToDto(patterns);
            
            // Verificar si ya existe
            var existing = await _supabase.GetAsync<LearnedPatternsDto>($"{TableName}?site_id=eq.{siteId}&select=id");
            
            if (existing.Any())
            {
                // Actualizar
                dto.Id = existing.First().Id;
                await _supabase.PatchAsync($"{TableName}?id=eq.{dto.Id}", JsonSerializer.Serialize(dto));
                _logger.LogInformation("Patrones actualizados en Supabase para sitio {SiteId}", siteId);
            }
            else
            {
                // Insertar nuevo
                dto.Id = Guid.NewGuid();
                await _supabase.PostAsync(TableName, dto);
                _logger.LogInformation("Patrones creados en Supabase para sitio {SiteId}", siteId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error guardando patrones en Supabase, solo guardado en cache");
        }
    }
    
    /// <summary>
    /// Detecta el tipo de URL basándose en patrones conocidos de Festo
    /// </summary>
    private UrlType DetectUrlType(string url)
    {
        // Producto por ID: /a/5249943/
        if (Regex.IsMatch(url, @"/a/\d+/?$"))
            return UrlType.ProductDetail;
        
        // Producto por slug: /p/nombre-id_CODIGO/
        if (Regex.IsMatch(url, @"/p/[^/]+-id_[^/]+/?$"))
            return UrlType.ProductDetail;
        
        // Listado con paginación: /c/.../id_pim123/?page=0
        if (Regex.IsMatch(url, @"/c/.*id_pim\d+/\?page=\d+"))
            return UrlType.ProductListing;
        
        // Subcategoría: /c/.../id_pim123/
        if (Regex.IsMatch(url, @"/c/.*id_pim\d+/?$"))
            return UrlType.Subcategory;
        
        // Categoría genérica: /c/productos/...
        if (url.Contains("/c/productos/") || url.Contains("/c/"))
            return UrlType.Subcategory;
        
        // Por defecto asumir detalle de producto
        return UrlType.ProductDetail;
    }
    
    /// <summary>
    /// Actualiza los patrones de URL aprendidos
    /// </summary>
    private async Task UpdateUrlPatternsAsync(Guid siteId, string url, UrlType type)
    {
        var patterns = await GetLearnedPatternsAsync(siteId) ?? new LearnedPatterns { SiteId = siteId };
        
        switch (type)
        {
            case UrlType.ProductDetail:
                if (!patterns.ExampleProductUrls.Contains(url))
                    patterns.ExampleProductUrls.Add(url);
                // Inferir patrón
                if (url.Contains("/a/"))
                    patterns.ProductDetailUrlPattern = "/a/{id}/";
                else if (url.Contains("/p/"))
                    patterns.ProductDetailUrlPattern = "/p/{slug}-id_{code}/";
                break;
                
            case UrlType.ProductListing:
                if (!patterns.ExampleListingUrls.Contains(url))
                    patterns.ExampleListingUrls.Add(url);
                patterns.ProductListingUrlPattern = "/c/.../id_pim{num}/?page={n}";
                break;
                
            case UrlType.Subcategory:
                patterns.SubcategoryUrlPattern = "/c/.../id_pim{num}/";
                break;
        }
        
        _patternsCache[siteId] = patterns;
    }
    
    /// <summary>
    /// Intenta inferir patrones de navegación basándose en los ejemplos procesados
    /// </summary>
    private async Task InferPatternFromExamplesAsync(
        Guid siteId,
        List<UrlAnalysisResult> results,
        CancellationToken cancellationToken)
    {
        var patterns = await GetLearnedPatternsAsync(siteId);
        if (patterns == null) return;
        
        // Analizar la estructura de URLs para inferir navegación
        var productUrls = results.Where(r => r.DetectedType == UrlType.ProductDetail).ToList();
        var listingUrls = results.Where(r => r.DetectedType == UrlType.ProductListing).ToList();
        
        if (listingUrls.Count > 0 && productUrls.Count > 0)
        {
            // Tenemos ejemplos de ambos tipos - podemos inferir la navegación
            patterns.NavigationPath = new List<string>
            {
                "Navegar a página de listado",
                "Buscar enlaces de productos en la lista",
                "Hacer click para ir al detalle",
                "Extraer datos del producto"
            };
            
            patterns.ConfidenceScore = 0.8;
        }
        
        patterns.Notes = $"Aprendido de {results.Count} URLs de ejemplo. " +
            $"Productos: {productUrls.Count}, Listados: {listingUrls.Count}";
        
        await SaveLearnedPatternsAsync(siteId, patterns);
    }
    
    // DTOs para Supabase
    private class LearnedPatternsDto
    {
        public Guid Id { get; set; }
        public Guid SiteId { get; set; }
        public DateTime LearnedAt { get; set; }
        public string? ProductDetailUrlPattern { get; set; }
        public string? ProductListingUrlPattern { get; set; }
        public string? SubcategoryUrlPattern { get; set; }
        public string? ExampleProductUrls { get; set; } // JSON array
        public string? ExampleListingUrls { get; set; } // JSON array
        public string? NavigationPath { get; set; } // JSON array
        public double ConfidenceScore { get; set; }
        public string? Notes { get; set; }
    }
    
    private LearnedPatternsDto MapToDto(LearnedPatterns patterns)
    {
        return new LearnedPatternsDto
        {
            Id = Guid.NewGuid(),
            SiteId = patterns.SiteId,
            LearnedAt = patterns.LearnedAt,
            ProductDetailUrlPattern = patterns.ProductDetailUrlPattern,
            ProductListingUrlPattern = patterns.ProductListingUrlPattern,
            SubcategoryUrlPattern = patterns.SubcategoryUrlPattern,
            ExampleProductUrls = JsonSerializer.Serialize(patterns.ExampleProductUrls),
            ExampleListingUrls = JsonSerializer.Serialize(patterns.ExampleListingUrls),
            NavigationPath = JsonSerializer.Serialize(patterns.NavigationPath),
            ConfidenceScore = patterns.ConfidenceScore,
            Notes = patterns.Notes
        };
    }
    
    private LearnedPatterns MapFromDto(LearnedPatternsDto dto)
    {
        return new LearnedPatterns
        {
            SiteId = dto.SiteId,
            LearnedAt = dto.LearnedAt,
            ProductDetailUrlPattern = dto.ProductDetailUrlPattern,
            ProductListingUrlPattern = dto.ProductListingUrlPattern,
            SubcategoryUrlPattern = dto.SubcategoryUrlPattern,
            ExampleProductUrls = string.IsNullOrEmpty(dto.ExampleProductUrls) 
                ? new() 
                : JsonSerializer.Deserialize<List<string>>(dto.ExampleProductUrls) ?? new(),
            ExampleListingUrls = string.IsNullOrEmpty(dto.ExampleListingUrls) 
                ? new() 
                : JsonSerializer.Deserialize<List<string>>(dto.ExampleListingUrls) ?? new(),
            NavigationPath = string.IsNullOrEmpty(dto.NavigationPath) 
                ? new() 
                : JsonSerializer.Deserialize<List<string>>(dto.NavigationPath) ?? new(),
            ConfidenceScore = dto.ConfidenceScore,
            Notes = dto.Notes
        };
    }
}
