using ScrapSAE.Api.Models;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Interfaces;
using System.Text.Json;

namespace ScrapSAE.Api.Services;

public sealed class ScrapingRunner
{
    private readonly IScrapingService _scrapingService;
    private readonly ISupabaseRestClient _supabase;
    private readonly IAIProcessorService _aiProcessorService;
    private readonly SupabaseTableService<SyncLog> _syncLogService;
    private readonly SupabaseTableService<CategoryMapping> _categoryMappingService;
    private readonly IScrapeControlService _scrapeControl;
    private readonly IPostExecutionAnalyzer? _postExecutionAnalyzer;
    private readonly IConfigurationUpdater? _configurationUpdater;
    private readonly IPerformanceMetricsCollector? _metricsCollector;
    private readonly ILearningService? _learningService;
    private readonly ILogger<ScrapingRunner> _logger;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public ScrapingRunner(
        IScrapingService scrapingService,
        ISupabaseRestClient supabase,
        IAIProcessorService aiProcessorService,
        SupabaseTableService<SyncLog> syncLogService,
        SupabaseTableService<CategoryMapping> categoryMappingService,
        IScrapeControlService scrapeControl,
        ILogger<ScrapingRunner> logger,
        IPostExecutionAnalyzer? postExecutionAnalyzer = null,
        IConfigurationUpdater? configurationUpdater = null,
        IPerformanceMetricsCollector? metricsCollector = null,
        ILearningService? learningService = null)
    {
        _scrapingService = scrapingService;
        _supabase = supabase;
        _aiProcessorService = aiProcessorService;
        _syncLogService = syncLogService;
        _categoryMappingService = categoryMappingService;
        _scrapeControl = scrapeControl;
        _logger = logger;
        _postExecutionAnalyzer = postExecutionAnalyzer;
        _configurationUpdater = configurationUpdater;
        _metricsCollector = metricsCollector;
        _learningService = learningService;
    }



    public async Task<ScrapeRunResult> RunForSiteAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow;
        var site = await GetSiteAsync(siteId);
        if (site == null)
        {
            throw new InvalidOperationException($"Site {siteId} not found.");
        }

        await LogAsync(site, "scrape", "info", $"🚀 Iniciando scraping para {site.Name}...");
        var scrapingMode = Environment.GetEnvironmentVariable("SCRAPSAE_MODE") ?? "traditional";
        await LogAsync(site, "scrape", "info", $"⚙️ Modo detectado: {(scrapingMode == "families" ? "Familias (Festo)" : "Tradicional")}");
        site = await EnrichSiteSelectorsAsync(site);
        var controlToken = _scrapeControl.Start(siteId);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, controlToken);
        
        // Cargar URLs aprendidas si el servicio está disponible
        string? previousLearnedUrls = null;
        if (_learningService != null)
        {
            try
            {
                _logger.LogInformation("Consultando LearningService para patrones de sitio {SiteId}", siteId);
                var patterns = await _learningService.GetLearnedPatternsAsync(siteId);
                if (patterns != null && (patterns.ExampleProductUrls.Count > 0 || patterns.ExampleListingUrls.Count > 0))
                {
                    // Combinar URLs de productos y listados para inspección directa
                    var learnedUrls = patterns.ExampleProductUrls
                        .Concat(patterns.ExampleListingUrls)
                        .Distinct()
                        .ToList();
                    
                    if (learnedUrls.Count > 0)
                    {
                        previousLearnedUrls = Environment.GetEnvironmentVariable("SCRAPSAE_LEARNED_URLS");
                        var urlsJson = JsonSerializer.Serialize(learnedUrls);
                        Environment.SetEnvironmentVariable("SCRAPSAE_LEARNED_URLS", urlsJson);
                        _logger.LogInformation("Cargadas {Count} URLs aprendidas para sitio {SiteId}", 
                            learnedUrls.Count, siteId);
                        await LogAsync(site, "scrape", "info", 
                            $"Usando {learnedUrls.Count} URLs aprendidas como punto de partida.");
                    }
                    else
                    {
                        _logger.LogInformation("No se encontraron URLs individuales en los patrones aprendidos para {SiteId}", siteId);
                    }
                }
                else
                {
                    _logger.LogInformation("No se encontraron patrones aprendidos para el sitio {SiteId}", siteId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error cargando URLs aprendidas para sitio {SiteId}, procediendo con scraping normal", siteId);
            }
        }
        else
        {
            _logger.LogWarning("LearningService no está disponible en ScrapingRunner");
        }
        
        List<ScrapedProduct> scraped;
        try
        {
            scraped = (await _scrapingService.ScrapeAsync(site, linkedCts.Token)).ToList();
        }

        catch (Exception ex)
        {
            _scrapeControl.MarkError(siteId, ex.Message);
            await LogAsync(site, "scrape", "error", ex.Message);
            throw;
        }
        var created = 0;
        var updated = 0;
        var skipped = 0;

        foreach (var item in scraped)
        {
            if (string.IsNullOrWhiteSpace(item.SkuSource))
            {
                skipped++;
                _logger.LogWarning("Producto omitido por SKU vacío. Título: {Title}", item.Title);
                continue;
            }

            var existing = await GetStagingBySkuAsync(siteId, item.SkuSource);
            if (existing == null)
            {
                var staging = MapToStaging(siteId, item);
                staging.AIProcessedJson = await BuildAiJsonAsync(item, cancellationToken);
                await _supabase.PostAsync("staging_products", staging);
                created++;
            }
            else
            {
                var update = new
                {
                    raw_data = item.RawHtml,
                    ai_processed_json = await BuildAiJsonAsync(item, cancellationToken),
                    updated_at = DateTime.UtcNow,
                    last_seen_at = DateTime.UtcNow
                };
                await _supabase.PatchAsync<StagingProduct>($"staging_products?id=eq.{existing.Id}", update);
                updated++;
            }
        }

        var duration = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds;
        await LogAsync(site, "scrape", "success", $"Scraping finalizado. Productos creados: {created}. Actualizados: {updated}.", duration);
        
        // === ANÁLISIS POST-EJECUCIÓN ===
        if (_postExecutionAnalyzer != null && _metricsCollector != null)
        {
            try
            {
                // Crear métricas de ejecución
                var metrics = new ScrapeExecutionMetrics
                {
                    SiteId = siteId,
                    StartedAt = startedAt,
                    CompletedAt = DateTime.UtcNow,
                    ProductsFound = scraped.Count - skipped,
                    ProductsSkipped = skipped,
                    ProductsWithSku = scraped.Count(p => !string.IsNullOrEmpty(p.SkuSource)),
                    ProductsWithPrice = scraped.Count(p => p.Price.HasValue)
                };
                
                // Analizar la ejecución
                var analysisResult = await _postExecutionAnalyzer.AnalyzeExecutionAsync(siteId, metrics, cancellationToken);
                await LogAsync(site, "analysis", "info", analysisResult.Summary ?? "Análisis completado");
                
                // Aplicar sugerencias automáticamente si hay un configurador disponible
                if (_configurationUpdater != null && analysisResult.Suggestions.Any(s => s.AutoApplicable))
                {
                    await _configurationUpdater.ApplySuggestionsAsync(siteId, analysisResult.Suggestions, cancellationToken);
                    await LogAsync(site, "config", "updated", $"Aplicadas {analysisResult.Suggestions.Count(s => s.AutoApplicable)} sugerencias automáticas");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error en análisis post-ejecución para sitio {SiteId}", siteId);
            }
        }
        
        _scrapeControl.MarkCompleted(siteId, "Scraping completado.");
        return new ScrapeRunResult
        {
            SiteId = siteId,
            StartedAtUtc = startedAt,
            ProductsFound = scraped.Count,
            ProductsCreated = created,
            ProductsUpdated = updated,
            ProductsSkipped = skipped,
            DurationMs = duration
        };
    }


    private async Task<SiteProfile?> GetSiteAsync(Guid siteId)
    {
        var sites = await _supabase.GetAsync<SiteProfile>($"config_sites?id=eq.{siteId}&select=*");
        return sites.FirstOrDefault();
    }

    private async Task<StagingProduct?> GetStagingBySkuAsync(Guid siteId, string skuSource)
    {
        var query = $"staging_products?site_id=eq.{siteId}&sku_source=eq.{Uri.EscapeDataString(skuSource)}&select=*";
        var results = await _supabase.GetAsync<StagingProduct>(query);
        return results.FirstOrDefault();
    }

    private static StagingProduct MapToStaging(Guid siteId, ScrapedProduct item)
    {
        return new StagingProduct
        {
            Id = Guid.NewGuid(),
            SiteId = siteId,
            SkuSource = item.SkuSource,
            RawData = item.RawHtml,
            SourceUrl = item.SourceUrl,
            Status = "pending",
            Attempts = 0,
            LastSeenAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }


    private async Task LogAsync(SiteProfile site, string operationType, string status, string message, int? durationMs = null)
    {
        try
        {
            var log = new SyncLog
            {
                OperationType = operationType,
                SiteId = site.Id,
                Status = status,
                Message = message,
                DurationMs = durationMs,
                CreatedAt = DateTime.UtcNow
            };
            await _syncLogService.CreateAsync(log);
        }
        catch
        {
            // Avoid breaking scraping flow if logging fails.
        }
    }

    private async Task<string?> BuildAiJsonAsync(ScrapedProduct scrapedProduct, CancellationToken cancellationToken)
    {
        var rawPayload = new
        {
            scrapedProduct.SkuSource,
            scrapedProduct.Title,
            scrapedProduct.Description,
            scrapedProduct.Price,
            scrapedProduct.ImageUrl,
            scrapedProduct.ScreenshotBase64,
            scrapedProduct.Brand,
            scrapedProduct.Category,
            scrapedProduct.Attributes
        };

        var rawData = JsonSerializer.Serialize(rawPayload);

        try
        {
            var processed = await _aiProcessorService.ProcessProductAsync(rawData, cancellationToken);
            processed.Sku ??= scrapedProduct.SkuSource;
            processed.Name = string.IsNullOrWhiteSpace(processed.Name) ? (scrapedProduct.Title ?? string.Empty) : processed.Name;
            processed.Description = string.IsNullOrWhiteSpace(processed.Description) ? (scrapedProduct.Description ?? string.Empty) : processed.Description;
            processed.Brand ??= scrapedProduct.Brand;
            processed.Price ??= scrapedProduct.Price;

            return JsonSerializer.Serialize(processed);
        }
        catch
        {
            return JsonSerializer.Serialize(new
            {
                scrapedProduct.Title,
                scrapedProduct.Price,
                scrapedProduct.ImageUrl,
                scrapedProduct.Description,
                scrapedProduct.Attributes
            });
        }
    }

    private async Task<SiteProfile> EnrichSiteSelectorsAsync(SiteProfile site)
    {
        try
        {
            var selectors = DeserializeSelectors(site.Selectors);
            if (selectors == null)
            {
                return site;
            }

            if (selectors.CategorySearchTerms.Count == 0)
            {
                var terms = await LoadCategorySearchTermsAsync();
                if (terms.Count > 0)
                {
                    selectors.CategorySearchTerms = terms;
                    // Asegurar que conservamos el modo si ya existe
                    if (string.IsNullOrEmpty(selectors.ScrapingMode) && site.Name.Contains("Festo", StringComparison.OrdinalIgnoreCase))
                    {
                        selectors.ScrapingMode = "families";
                    }
                    site.Selectors = JsonSerializer.Serialize(selectors, _jsonOptions);
                    await LogAsync(site, "scrape", "info", $"Categorias cargadas: {terms.Count}.");
                }
            }
        }
        catch
        {
            // Ignore selector enrichment failures.
        }

        return site;
    }

    private static SiteSelectors? DeserializeSelectors(object? selectorsObj)
    {
        if (selectorsObj == null)
        {
            return null;
        }

        try
        {
            if (selectorsObj is JsonElement jsonElement)
            {
                return JsonSerializer.Deserialize<SiteSelectors>(jsonElement.GetRawText(), _jsonOptions);
            }

            if (selectorsObj is string json)
            {
                return JsonSerializer.Deserialize<SiteSelectors>(json, _jsonOptions);
            }

            return JsonSerializer.Deserialize<SiteSelectors>(JsonSerializer.Serialize(selectorsObj, _jsonOptions), _jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private async Task<List<string>> LoadCategorySearchTermsAsync()
    {
        try
        {
            var mappings = await _categoryMappingService.GetAllAsync();
            return mappings
                .Select(m => m.SourceCategory)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Select(text => text!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }
}
