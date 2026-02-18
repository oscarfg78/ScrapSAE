using ScrapSAE.Api.Models;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Interfaces;
using System.Text.Json;
using System.Text.Json.Nodes;

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
    private readonly IPdfAttachmentAnalyzer? _pdfAttachmentAnalyzer;
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
        ILearningService? learningService = null,
        IPdfAttachmentAnalyzer? pdfAttachmentAnalyzer = null)
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
        _pdfAttachmentAnalyzer = pdfAttachmentAnalyzer;
    }



    public async Task<ScrapeRunResult> RunForSiteAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow;
        var site = await GetSiteAsync(siteId);
        if (site == null)
        {
            throw new InvalidOperationException($"Site {siteId} not found.");
        }

        await LogAsync(site, "scrape", "success", $"🚀 Iniciando scraping para {site.Name}...");
        var scrapingMode = Environment.GetEnvironmentVariable("SCRAPSAE_MODE") ?? "traditional";
        await LogAsync(site, "scrape", "success", $"⚙️ Modo detectado: {(scrapingMode == "families" ? "Familias (Festo)" : "Tradicional")}");
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
                        await LogAsync(site, "scrape", "success", 
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
        var (created, updated, skipped) = await ProcessScrapedProductsAsync(siteId, scraped, cancellationToken);

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


    public async Task<(int created, int updated, int skipped)> ProcessScrapedProductsAsync(
        Guid siteId, 
        List<ScrapedProduct> scraped, 
        CancellationToken cancellationToken)
    {
        var site = await GetSiteAsync(siteId);
        if (site == null) return (0, 0, 0);

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

            // Auto-aprendizaje: si extraemos con éxito un producto, la URL es válida
            if (_learningService != null && !string.IsNullOrEmpty(item.SourceUrl))
            {
                try
                {
                    await _learningService.LearnFromUrlAsync(siteId, item.SourceUrl, UrlType.ProductDetail, cancellationToken);
                }
                catch 
                { 
                    // No bloquear el flujo si falla el aprendizaje
                }
            }

            var existing = await GetStagingBySkuAsync(siteId, item.SkuSource);
            var effectiveProduct = await EnrichScrapedProductAsync(siteId, item, existing, cancellationToken);
            var rawSnapshotJson = SerializeScrapedSnapshot(effectiveProduct);
            var incomingAiJson = await BuildAiJsonAsync(effectiveProduct, cancellationToken) ?? "{}";
            var pdfSpecs = await ExtractPdfSpecificationsAsync(effectiveProduct, cancellationToken);
            var finalAiJson = MergeConservative(existing?.AIProcessedJson, incomingAiJson, pdfSpecs);
            var sourceUrl = ResolvePreferredSourceUrl(effectiveProduct, existing?.SourceUrl);

            if (existing == null)
            {
                var staging = MapToStaging(siteId, effectiveProduct);
                staging.RawData = rawSnapshotJson;
                staging.SourceUrl = sourceUrl;
                staging.AIProcessedJson = finalAiJson;
                await _supabase.PostAsync("staging_products", staging);
                created++;
            }
            else
            {
                var update = new
                {
                    raw_data = rawSnapshotJson,
                    ai_processed_json = finalAiJson,
                    source_url = sourceUrl,
                    updated_at = DateTime.UtcNow,
                    last_seen_at = DateTime.UtcNow
                };
                await _supabase.PatchAsync<StagingProduct>($"staging_products?id=eq.{existing.Id}", update);
                updated++;
            }
        }

        return (created, updated, skipped);
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
            RawData = SerializeScrapedSnapshot(item),
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
            scrapedProduct.ImageUrls,
            scrapedProduct.Attachments,
            scrapedProduct.ScreenshotBase64,
            scrapedProduct.Brand,
            scrapedProduct.Category,
            scrapedProduct.SourceUrl,
            scrapedProduct.NavigationUrls,
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
            MergeScrapedDataIntoProcessed(processed, scrapedProduct);
            processed.OriginalRawData ??= rawData;

            return JsonSerializer.Serialize(processed);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Fallo procesamiento IA para SKU {Sku}. Se aplicara fallback estructurado.",
                scrapedProduct.SkuSource);

            var fallback = BuildFallbackProcessedProduct(scrapedProduct, rawData);
            return JsonSerializer.Serialize(fallback);
        }
    }

    public Task<string?> BuildAiJsonFromScrapedAsync(ScrapedProduct scrapedProduct, CancellationToken cancellationToken)
    {
        return BuildAiJsonAsync(scrapedProduct, cancellationToken);
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
                    await LogAsync(site, "scrape", "success", $"Categorias cargadas: {terms.Count}.");
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

    private static ProcessedProduct BuildFallbackProcessedProduct(ScrapedProduct scrapedProduct, string rawData)
    {
        var fallback = new ProcessedProduct
        {
            Sku = scrapedProduct.SkuSource,
            Name = scrapedProduct.Title ?? scrapedProduct.SkuSource ?? string.Empty,
            Description = scrapedProduct.Description ?? scrapedProduct.Title ?? string.Empty,
            Brand = scrapedProduct.Brand,
            Price = scrapedProduct.Price,
            OriginalRawData = rawData
        };

        MergeScrapedDataIntoProcessed(fallback, scrapedProduct);
        return fallback;
    }

    private static void MergeScrapedDataIntoProcessed(ProcessedProduct processed, ScrapedProduct scrapedProduct)
    {
        processed.Specifications ??= new Dictionary<string, string>();
        processed.Images ??= new List<string>();
        processed.Attachments ??= new List<ProductAttachment>();
        processed.Categories ??= new List<string>();

        if (!string.IsNullOrWhiteSpace(scrapedProduct.SourceUrl) &&
            !processed.Specifications.ContainsKey("source_url"))
        {
            processed.Specifications["source_url"] = scrapedProduct.SourceUrl!;
        }

        if (string.IsNullOrWhiteSpace(processed.Currency) &&
            scrapedProduct.Attributes.TryGetValue("currency", out var currencyValue) &&
            !string.IsNullOrWhiteSpace(currencyValue))
        {
            processed.Currency = currencyValue.Trim();
        }

        if (!processed.Stock.HasValue &&
            scrapedProduct.Attributes.TryGetValue("stock", out var stockValue) &&
            int.TryParse(stockValue, out var parsedStock))
        {
            processed.Stock = parsedStock;
        }

        foreach (var imageUrl in scrapedProduct.ImageUrls ?? Enumerable.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(imageUrl) &&
                !processed.Images.Contains(imageUrl, StringComparer.OrdinalIgnoreCase))
            {
                processed.Images.Add(imageUrl.Trim());
            }
        }

        if (!string.IsNullOrWhiteSpace(scrapedProduct.ImageUrl) &&
            !processed.Images.Contains(scrapedProduct.ImageUrl, StringComparer.OrdinalIgnoreCase))
        {
            processed.Images.Insert(0, scrapedProduct.ImageUrl.Trim());
        }

        var existingAttachmentUrls = new HashSet<string>(
            processed.Attachments.Select(a => a.FileUrl ?? string.Empty),
            StringComparer.OrdinalIgnoreCase);
        foreach (var attachment in scrapedProduct.Attachments ?? Enumerable.Empty<ProductAttachment>())
        {
            if (string.IsNullOrWhiteSpace(attachment.FileUrl) ||
                existingAttachmentUrls.Contains(attachment.FileUrl))
            {
                continue;
            }

            processed.Attachments.Add(new ProductAttachment
            {
                FileName = attachment.FileName ?? string.Empty,
                FileUrl = attachment.FileUrl.Trim(),
                FileType = attachment.FileType,
                FileSizeBytes = attachment.FileSizeBytes
            });
            existingAttachmentUrls.Add(attachment.FileUrl);
        }

        if (!processed.Categories.Any() && !string.IsNullOrWhiteSpace(scrapedProduct.Category))
        {
            processed.Categories = SplitCategoryPath(scrapedProduct.Category);
        }

        processed.SuggestedCategory ??= processed.Categories.FirstOrDefault();

        foreach (var attribute in scrapedProduct.Attributes)
        {
            if (string.IsNullOrWhiteSpace(attribute.Key) || string.IsNullOrWhiteSpace(attribute.Value))
            {
                continue;
            }

            if (IgnoredAttributeKeys.Contains(attribute.Key))
            {
                continue;
            }

            var key = NormalizeSpecificationKey(attribute.Key);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (!processed.Specifications.ContainsKey(key))
            {
                processed.Specifications[key] = attribute.Value.Trim();
            }
        }
    }

    private static string NormalizeSpecificationKey(string rawKey)
    {
        var key = rawKey.Trim();
        if (key.StartsWith("tech_", StringComparison.OrdinalIgnoreCase))
        {
            key = key.Substring(5);
        }

        key = key.Replace("_", " ").Trim();
        return key;
    }

    private static List<string> SplitCategoryPath(string rawCategory)
    {
        return rawCategory
            .Split(new[] { '>', '|', ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(v => v.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();
    }

    private static readonly HashSet<string> IgnoredAttributeKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "product_url",
        "variant_url",
        "price_text",
        "stock",
        "currency",
        "source_url"
    };
}
