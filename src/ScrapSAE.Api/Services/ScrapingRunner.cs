using ScrapSAE.Api.Models;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Interfaces;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace ScrapSAE.Api.Services;

public sealed class ScrapingRunner
{
    private readonly IServiceProvider _serviceProvider;
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
        IServiceProvider serviceProvider,
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
        _serviceProvider = serviceProvider;
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
                        if (site.MaxProductsPerScrape > 0 && learnedUrls.Count > site.MaxProductsPerScrape)
                        {
                            learnedUrls = learnedUrls.Take(site.MaxProductsPerScrape).ToList();
                        }
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

        // ── FASE DE DESCUBRIMIENTO ADITIVA ──────────────────────────────────────
        // Invoca el mismo motor de descubrimiento que usa el Wizard para encontrar
        // URLs de productos dinámicamente. Los resultados se SUMAN al pool de URLs
        // existentes (learned/direct). Si ya existen URLs configuradas manualmente o
        // aprendidas, las descubiertas se añaden al mismo conjunto de forma deduplicada.
        // Si hay un error, se ignora y el scraping continúa normalmente.
        var existingDirectUrls = Environment.GetEnvironmentVariable("SCRAPSAE_DIRECT_URLS");
        var existingLearnedUrls = Environment.GetEnvironmentVariable("SCRAPSAE_LEARNED_URLS");
        // Only run discovery if the site doesn't have a forced direct URL override set
        if (string.IsNullOrEmpty(existingDirectUrls))
        {
            try
            {
                await LogAsync(site, "scrape", "info", "🔍 Fase 1: Descubrimiento de catálogo en progreso...");
                var discoveredUrls = await _scrapingService.DiscoverProductUrlsAsync(site, linkedCts.Token);
                if (discoveredUrls.Count > 0)
                {
                    // Merge with any existing learned URLs
                    var mergedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (!string.IsNullOrEmpty(existingLearnedUrls))
                    {
                        var existing = JsonSerializer.Deserialize<List<string>>(existingLearnedUrls) ?? new List<string>();
                        foreach (var u in existing) mergedUrls.Add(u);
                    }
                    foreach (var u in discoveredUrls) mergedUrls.Add(u);

                    var poolList = mergedUrls.ToList();
                    if (site.MaxProductsPerScrape > 0 && poolList.Count > site.MaxProductsPerScrape)
                    {
                        poolList = poolList.Take(site.MaxProductsPerScrape).ToList();
                    }

                    Environment.SetEnvironmentVariable("SCRAPSAE_LEARNED_URLS", JsonSerializer.Serialize(poolList));
                    await LogAsync(site, "scrape", "success",
                        $"🔍 Descubrimiento completado: {discoveredUrls.Count} URL(s) nuevas encontradas. Total en pool: {poolList.Count}.");
                    _logger.LogInformation("[Discovery] {Count} URLs discovered and merged for site {SiteId}", discoveredUrls.Count, siteId);
                }
                else
                {
                    await LogAsync(site, "scrape", "info", "🔍 Descubrimiento no encontró URLs adicionales. Continuando con flujo estándar.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Discovery] Error en fase de descubrimiento para {SiteId}; continuando con scraping normal.", siteId);
                await LogAsync(site, "scrape", "warn", $"[Discovery] Error ignorado: {ex.Message}. Continuando.");
            }
        }
        // ── FIN FASE DE DESCUBRIMIENTO ───────────────────────────────────────────

        List<ScrapedProduct> scraped;
        try
        {
            IProviderScraperStrategy? strategy = null;
            try
            {
                var strategyKey = string.IsNullOrWhiteSpace(site.StrategyType) ? "Generic" : site.StrategyType;
                strategy = _serviceProvider.GetKeyedService<IProviderScraperStrategy>(strategyKey)
                           ?? _serviceProvider.GetKeyedService<IProviderScraperStrategy>("Generic");
            }
            catch (InvalidOperationException)
            {
                // ServiceProvider does not support keyed services (e.g. in unit tests or custom containers)
            }

            if (strategy != null)
            {
                scraped = (await strategy.ScrapeAsync(site, linkedCts.Token)).ToList();
            }
            else
            {
                scraped = (await _scrapingService.ScrapeAsync(site, linkedCts.Token)).ToList();
            }
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

        var isTempSite = site.Name.StartsWith("[TEMP]", StringComparison.OrdinalIgnoreCase);

        foreach (var item in scraped)
        {
            ApplyProviderBrandAndCategory(item, site.Name);

            if (string.IsNullOrWhiteSpace(item.SkuSource))
            {
                skipped++;
                _logger.LogWarning("Producto omitido por SKU vacío. Título: {Title}", item.Title);
                continue;
            }

            // Auto-aprendizaje: si extraemos con éxito un producto, la URL es válida (omitir en pruebas [TEMP])
            if (!isTempSite && _learningService != null && !string.IsNullOrEmpty(item.SourceUrl))
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
            var effectiveProduct = existing == null
                ? CloneScrapedProduct(item)
                : await EnrichScrapedProductAsync(siteId, item, existing, cancellationToken);
            ApplyProviderBrandAndCategory(effectiveProduct, site.Name);
            var rawSnapshotJson = SerializeScrapedSnapshot(effectiveProduct);
            var incomingAiJson = await BuildAiJsonAsync(effectiveProduct, isTempSite, cancellationToken) ?? "{}";
            var pdfSpecs = isTempSite ? null : await ExtractPdfSpecificationsAsync(effectiveProduct, cancellationToken);
            var finalAiJson = MergeConservative(existing?.AIProcessedJson, incomingAiJson, pdfSpecs ?? new Dictionary<string, string>());
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
        var site = sites.FirstOrDefault();
        return site == null ? null : SiteProfileSchemaCompatibility.NormalizeFromStorage(site);
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

    private async Task<string?> BuildAiJsonAsync(ScrapedProduct scrapedProduct, bool skipAi, CancellationToken cancellationToken)
    {
        var rawData = SerializeScrapedSnapshot(scrapedProduct);

        if (skipAi)
        {
            var fallback = BuildFallbackProcessedProduct(scrapedProduct, rawData);
            return JsonSerializer.Serialize(fallback);
        }

        try
        {
            var processed = await _aiProcessorService.ProcessProductAsync(rawData, cancellationToken);
            processed.Sku ??= scrapedProduct.SkuSource;
            processed.Name = string.IsNullOrWhiteSpace(processed.Name) ? (scrapedProduct.Title ?? string.Empty) : processed.Name;
            processed.Description = string.IsNullOrWhiteSpace(processed.Description) ? (scrapedProduct.Description ?? string.Empty) : processed.Description;
            if (!string.IsNullOrWhiteSpace(scrapedProduct.Brand))
            {
                processed.Brand = scrapedProduct.Brand;
            }
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
        return BuildAiJsonAsync(scrapedProduct, skipAi: false, cancellationToken);
    }

    private static string SerializeScrapedSnapshot(ScrapedProduct scrapedProduct)
    {
        var rawPayload = new
        {
            scrapedProduct.SkuSource,
            scrapedProduct.Title,
            scrapedProduct.Description,
            scrapedProduct.RawHtml,
            scrapedProduct.Price,
            scrapedProduct.ImageUrl,
            scrapedProduct.ImageUrls,
            scrapedProduct.Attachments,
            scrapedProduct.ScreenshotBase64,
            scrapedProduct.Brand,
            scrapedProduct.Category,
            scrapedProduct.SourceUrl,
            scrapedProduct.NavigationUrls,
            scrapedProduct.Attributes,
            scrapedProduct.ScrapedAt
        };

        return JsonSerializer.Serialize(rawPayload);
    }

    private async Task<ScrapedProduct> EnrichScrapedProductAsync(
        Guid siteId,
        ScrapedProduct incoming,
        StagingProduct? existing,
        CancellationToken cancellationToken)
    {
        var enriched = CloneScrapedProduct(incoming);

        var existingCoverage = GetCoverageFromStoredJson(existing?.AIProcessedJson);
        var currentCoverage = GetCoverageFromScraped(enriched);
        var candidateUrls = CollectEnrichmentUrls(enriched, existing);

        // Nuevo flujo incremental:
        // 1) aprovechar links ya encontrados para generar adjuntos sin navegación extra.
        AddAttachmentCandidatesFromLinks(enriched, candidateUrls);
        currentCoverage = GetCoverageFromScraped(enriched);

        if (!ShouldAttemptDeepEnrichment(currentCoverage, existingCoverage) || candidateUrls.Count == 0)
        {
            return enriched;
        }

        var detailUrls = candidateUrls
            .Where(IsLikelyProductDetailUrl)
            .Take(4)
            .ToList();
        if (detailUrls.Count == 0)
        {
            return enriched;
        }

        _logger.LogInformation(
            "Enrichment flow para SKU {Sku}: intentando {Count} URL(s) para completar adjuntos/specs.",
            enriched.SkuSource,
            detailUrls.Count);

        foreach (var url in detailUrls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var deepProducts = await _scrapingService.ScrapeDirectUrlsAsync(
                    new List<string> { url },
                    siteId,
                    new DirectUrlScrapeOptions
                    {
                        InspectOnly = false,
                        SingleProductOnly = true,
                        ExpandRelated = false
                    },
                    cancellationToken);

                var candidate = deepProducts
                    .FirstOrDefault(p => IsLikelySameProduct(enriched, p));
                if (candidate == null)
                {
                    continue;
                }

                enriched = MergeScrapedProducts(enriched, candidate);
                AddAttachmentCandidatesFromLinks(enriched, candidate.NavigationUrls ?? Enumerable.Empty<string>());

                currentCoverage = GetCoverageFromScraped(enriched);
                if (!ShouldAttemptDeepEnrichment(currentCoverage, existingCoverage))
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "Enrichment flow fallo para SKU {Sku} en URL {Url}",
                    enriched.SkuSource,
                    url);
            }
        }

        return enriched;
    }

    private async Task<Dictionary<string, string>> ExtractPdfSpecificationsAsync(
        ScrapedProduct product,
        CancellationToken cancellationToken)
    {
        if (_pdfAttachmentAnalyzer == null || product.Attachments.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            return await _pdfAttachmentAnalyzer.ExtractSpecificationsAsync(product.Attachments, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No se pudieron extraer specs de PDFs para SKU {Sku}", product.SkuSource);
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string? ResolvePreferredSourceUrl(ScrapedProduct product, string? existingSourceUrl)
    {
        if (!string.IsNullOrWhiteSpace(product.SourceUrl))
        {
            return product.SourceUrl!.Trim();
        }

        if (product.Attributes.TryGetValue("product_url", out var productUrl) &&
            !string.IsNullOrWhiteSpace(productUrl))
        {
            return productUrl.Trim();
        }

        if (product.Attributes.TryGetValue("variant_url", out var variantUrl) &&
            !string.IsNullOrWhiteSpace(variantUrl))
        {
            return variantUrl.Trim();
        }

        return string.IsNullOrWhiteSpace(existingSourceUrl) ? null : existingSourceUrl.Trim();
    }

    private static ScrapedProduct CloneScrapedProduct(ScrapedProduct source)
    {
        return new ScrapedProduct
        {
            SkuSource = source.SkuSource,
            Title = source.Title,
            Description = source.Description,
            RawHtml = source.RawHtml,
            ScreenshotBase64 = source.ScreenshotBase64,
            ImageUrl = source.ImageUrl,
            ImageUrls = new List<string>(source.ImageUrls ?? new List<string>()),
            Price = source.Price,
            Category = source.Category,
            Brand = source.Brand,
            SourceUrl = source.SourceUrl,
            Attributes = new Dictionary<string, string>(source.Attributes ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase),
            NavigationUrls = new List<string>(source.NavigationUrls ?? new List<string>()),
            ScrapedAt = source.ScrapedAt,
            AiEnriched = source.AiEnriched,
            Attachments = source.Attachments?
                .Select(a => new ProductAttachment
                {
                    FileName = a.FileName,
                    FileUrl = a.FileUrl,
                    FileType = a.FileType,
                    FileSizeBytes = a.FileSizeBytes
                })
                .ToList() ?? new List<ProductAttachment>()
        };
    }

    private static bool ShouldAttemptDeepEnrichment(DataCoverage current, DataCoverage existing)
    {
        var totalAttachments = Math.Max(current.Attachments, existing.Attachments);
        var totalImages = Math.Max(current.Images, existing.Images);
        var totalSpecs = Math.Max(current.Specifications, existing.Specifications);

        return totalAttachments == 0 || totalImages == 0 || totalSpecs < 8;
    }

    private static DataCoverage GetCoverageFromScraped(ScrapedProduct product)
    {
        var images = (product.ImageUrls ?? new List<string>())
            .Concat(string.IsNullOrWhiteSpace(product.ImageUrl) ? Enumerable.Empty<string>() : new[] { product.ImageUrl! })
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var attachments = (product.Attachments ?? new List<ProductAttachment>())
            .Count(a => !string.IsNullOrWhiteSpace(a.FileUrl));

        var specCount = (product.Attributes ?? new Dictionary<string, string>())
            .Count(kv =>
                !string.IsNullOrWhiteSpace(kv.Key) &&
                !string.IsNullOrWhiteSpace(kv.Value) &&
                kv.Key.StartsWith("tech_", StringComparison.OrdinalIgnoreCase));

        return new DataCoverage(images, attachments, specCount);
    }

    private static DataCoverage GetCoverageFromStoredJson(string? aiJson)
    {
        if (string.IsNullOrWhiteSpace(aiJson))
        {
            return default;
        }

        try
        {
            using var document = JsonDocument.Parse(aiJson);
            var root = document.RootElement;

            var images = ReadJsonStringArray(
                    root,
                    "images", "Images",
                    "imageUrls", "image_urls", "ImageUrls",
                    "primaryImageUrls", "primary_image_urls", "PrimaryImageUrls")
                .Count;

            var attachments = 0;
            if (TryGetPropertyIgnoreCase(root, "attachments", out var attachmentsElement) &&
                attachmentsElement.ValueKind == JsonValueKind.Array)
            {
                attachments = attachmentsElement.GetArrayLength();
            }

            var specs = 0;
            if (TryGetPropertyIgnoreCase(root, "specifications", out var specsElement) &&
                specsElement.ValueKind == JsonValueKind.Object)
            {
                specs = specsElement
                    .EnumerateObject()
                    .Count(p =>
                        !string.IsNullOrWhiteSpace(p.Name) &&
                        !string.Equals(p.Name, "source_url", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(p.Name, "product_url", StringComparison.OrdinalIgnoreCase) &&
                        p.Value.ValueKind != JsonValueKind.Null &&
                        !(p.Value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(p.Value.GetString())));
            }

            return new DataCoverage(images, attachments, specs);
        }
        catch
        {
            return default;
        }
    }

    private static HashSet<string> CollectEnrichmentUrls(ScrapedProduct product, StagingProduct? existing)
    {
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddIfValid(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var trimmed = value.Trim();
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                urls.Add(uri.ToString());
            }
        }

        AddIfValid(product.SourceUrl);
        AddIfValid(existing?.SourceUrl);
        AddIfValid(ReadAttribute(product, "product_url"));
        AddIfValid(ReadAttribute(product, "variant_url"));
        AddIfValid(ReadAttribute(product, "source_url"));

        foreach (var navigationUrl in product.NavigationUrls ?? Enumerable.Empty<string>())
        {
            AddIfValid(navigationUrl);
        }

        CollectUrlsFromJson(existing?.AIProcessedJson, urls);
        CollectUrlsFromJson(existing?.RawData, urls);
        return urls;
    }

    private static void CollectUrlsFromJson(string? json, ISet<string> target)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var directCandidates = new[]
            {
                ReadJsonString(root, "sourceUrl", "source_url", "productUrl", "product_url", "url", "Url"),
                ReadJsonString(root, "variant_url", "variantUrl", "detail_url", "detailUrl")
            };

            foreach (var candidate in directCandidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate) &&
                    Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    target.Add(uri.ToString());
                }
            }

            var navUrls = ReadJsonStringArray(root, "navigationUrls", "NavigationUrls", "navigation_urls");
            foreach (var nav in navUrls)
            {
                if (Uri.TryCreate(nav, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    target.Add(uri.ToString());
                }
            }
        }
        catch
        {
            // Ignore malformed JSON.
        }
    }

    private static bool IsLikelyProductDetailUrl(string url)
    {
        var lower = url.ToLowerInvariant();
        if (lower.Contains("download-document") || lower.EndsWith(".pdf"))
        {
            return false;
        }

        return lower.Contains("/a/") || lower.Contains("/p/") || lower.Contains("/product/");
    }

    private static bool IsLikelySameProduct(ScrapedProduct left, ScrapedProduct right)
    {
        if (string.IsNullOrWhiteSpace(right.SkuSource))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(left.SkuSource) &&
            string.Equals(left.SkuSource, right.SkuSource, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(left.SourceUrl) &&
            !string.IsNullOrWhiteSpace(right.SourceUrl) &&
            string.Equals(left.SourceUrl, right.SourceUrl, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var leftProductUrl = ReadAttribute(left, "product_url");
        var rightProductUrl = ReadAttribute(right, "product_url");
        return !string.IsNullOrWhiteSpace(leftProductUrl) &&
               !string.IsNullOrWhiteSpace(rightProductUrl) &&
               string.Equals(leftProductUrl, rightProductUrl, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadAttribute(ScrapedProduct product, string key)
    {
        if (product.Attributes.TryGetValue(key, out var value))
        {
            return value;
        }

        return product.Attributes
            .FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
            .Value;
    }

    private static ScrapedProduct MergeScrapedProducts(ScrapedProduct baseProduct, ScrapedProduct enrichment)
    {
        if (string.IsNullOrWhiteSpace(baseProduct.Title))
        {
            baseProduct.Title = enrichment.Title;
        }

        if (string.IsNullOrWhiteSpace(baseProduct.Description) ||
            (baseProduct.Description?.Length ?? 0) < (enrichment.Description?.Length ?? 0))
        {
            if (!string.IsNullOrWhiteSpace(enrichment.Description))
            {
                baseProduct.Description = enrichment.Description;
            }
        }

        if (!baseProduct.Price.HasValue && enrichment.Price.HasValue)
        {
            baseProduct.Price = enrichment.Price;
        }

        if (string.IsNullOrWhiteSpace(baseProduct.Brand))
        {
            baseProduct.Brand = enrichment.Brand;
        }

        if (string.IsNullOrWhiteSpace(baseProduct.Category))
        {
            baseProduct.Category = enrichment.Category;
        }

        if (string.IsNullOrWhiteSpace(baseProduct.SourceUrl))
        {
            baseProduct.SourceUrl = enrichment.SourceUrl;
        }

        if (string.IsNullOrWhiteSpace(baseProduct.RawHtml) && !string.IsNullOrWhiteSpace(enrichment.RawHtml))
        {
            baseProduct.RawHtml = enrichment.RawHtml;
        }

        var mergedImages = (baseProduct.ImageUrls ?? new List<string>())
            .Concat(enrichment.ImageUrls ?? Enumerable.Empty<string>())
            .Concat(string.IsNullOrWhiteSpace(baseProduct.ImageUrl) ? Enumerable.Empty<string>() : new[] { baseProduct.ImageUrl! })
            .Concat(string.IsNullOrWhiteSpace(enrichment.ImageUrl) ? Enumerable.Empty<string>() : new[] { enrichment.ImageUrl! })
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        baseProduct.ImageUrls = mergedImages;
        if (string.IsNullOrWhiteSpace(baseProduct.ImageUrl) && mergedImages.Count > 0)
        {
            baseProduct.ImageUrl = mergedImages[0];
        }

        baseProduct.Attachments ??= new List<ProductAttachment>();
        var existingAttachments = new HashSet<string>(
            baseProduct.Attachments
                .Where(a => !string.IsNullOrWhiteSpace(a.FileUrl))
                .Select(a => a.FileUrl.Trim()),
            StringComparer.OrdinalIgnoreCase);

        foreach (var attachment in enrichment.Attachments ?? Enumerable.Empty<ProductAttachment>())
        {
            if (string.IsNullOrWhiteSpace(attachment.FileUrl))
            {
                continue;
            }

            var key = attachment.FileUrl.Trim();
            if (existingAttachments.Contains(key))
            {
                continue;
            }

            baseProduct.Attachments.Add(new ProductAttachment
            {
                FileName = string.IsNullOrWhiteSpace(attachment.FileName) ? GuessAttachmentName(key) : attachment.FileName.Trim(),
                FileUrl = key,
                FileType = attachment.FileType ?? GuessAttachmentMimeType(key),
                FileSizeBytes = attachment.FileSizeBytes
            });
            existingAttachments.Add(key);
        }

        baseProduct.Attributes ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var attribute in enrichment.Attributes ?? new Dictionary<string, string>())
        {
            if (string.IsNullOrWhiteSpace(attribute.Key) || string.IsNullOrWhiteSpace(attribute.Value))
            {
                continue;
            }

            if (!baseProduct.Attributes.ContainsKey(attribute.Key) ||
                string.IsNullOrWhiteSpace(baseProduct.Attributes[attribute.Key]))
            {
                baseProduct.Attributes[attribute.Key] = attribute.Value;
            }
        }

        baseProduct.NavigationUrls = (baseProduct.NavigationUrls ?? new List<string>())
            .Concat(enrichment.NavigationUrls ?? Enumerable.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return baseProduct;
    }

    private static void AddAttachmentCandidatesFromLinks(ScrapedProduct product, IEnumerable<string> links)
    {
        product.Attachments ??= new List<ProductAttachment>();
        var existingUrls = new HashSet<string>(
            product.Attachments
                .Where(a => !string.IsNullOrWhiteSpace(a.FileUrl))
                .Select(a => a.FileUrl.Trim()),
            StringComparer.OrdinalIgnoreCase);

        foreach (var link in links)
        {
            if (string.IsNullOrWhiteSpace(link))
            {
                continue;
            }

            var normalized = link.Trim();
            if (!LooksLikeAttachmentUrl(normalized) || existingUrls.Contains(normalized))
            {
                continue;
            }

            product.Attachments.Add(new ProductAttachment
            {
                FileName = GuessAttachmentName(normalized),
                FileUrl = normalized,
                FileType = GuessAttachmentMimeType(normalized)
            });
            existingUrls.Add(normalized);
        }
    }

    private static bool LooksLikeAttachmentUrl(string url)
    {
        var lower = url.ToLowerInvariant();
        return lower.Contains(".pdf") ||
               lower.Contains(".zip") ||
               lower.Contains(".docx") ||
               lower.Contains(".doc") ||
               lower.Contains(".xlsx") ||
               lower.Contains(".xls") ||
               lower.Contains("download-document") ||
               lower.Contains("datasheet") ||
               lower.Contains("manual");
    }

    private static string GuessAttachmentName(string url)
    {
        try
        {
            var uri = new Uri(url);
            var fileName = Path.GetFileName(uri.AbsolutePath);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                return fileName;
            }
        }
        catch
        {
            // Ignore and fallback below.
        }

        return "attachment";
    }

    private static string? GuessAttachmentMimeType(string url)
    {
        var lower = url.ToLowerInvariant();
        if (lower.Contains(".pdf") || lower.Contains("datasheet"))
            return "application/pdf";
        if (lower.Contains(".zip"))
            return "application/zip";
        if (lower.Contains(".docx"))
            return "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
        if (lower.Contains(".doc"))
            return "application/msword";
        if (lower.Contains(".xlsx"))
            return "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        if (lower.Contains(".xls"))
            return "application/vnd.ms-excel";
        return null;
    }

    private static string MergeConservative(string? existingJson, string? incomingJson, IReadOnlyDictionary<string, string> pdfSpecs)
    {
        var baseNode = ParseJsonObject(existingJson);
        var incomingNode = ParseJsonObject(incomingJson);
        MergeObject(baseNode, incomingNode);

        if (pdfSpecs.Count > 0)
        {
            if (baseNode["specifications"] is not JsonObject specsObj)
            {
                specsObj = new JsonObject();
                baseNode["specifications"] = specsObj;
            }

            foreach (var (key, value) in pdfSpecs)
            {
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (!specsObj.ContainsKey(key) || IsNullOrEmpty(specsObj[key]))
                {
                    specsObj[key] = value;
                }
            }
        }

        return baseNode.ToJsonString();
    }

    private static JsonObject ParseJsonObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new JsonObject();
        }

        try
        {
            var parsed = JsonNode.Parse(json);
            return parsed as JsonObject ?? new JsonObject();
        }
        catch
        {
            return new JsonObject();
        }
    }

    private static void MergeObject(JsonObject target, JsonObject incoming)
    {
        foreach (var incomingProperty in incoming)
        {
            if (!target.ContainsKey(incomingProperty.Key))
            {
                target[incomingProperty.Key] = incomingProperty.Value?.DeepClone();
                continue;
            }

            var existingValue = target[incomingProperty.Key];
            var incomingValue = incomingProperty.Value;
            if (IsNullOrEmpty(existingValue))
            {
                target[incomingProperty.Key] = incomingValue?.DeepClone();
                continue;
            }

            if (existingValue is JsonObject existingObj && incomingValue is JsonObject incomingObj)
            {
                MergeObject(existingObj, incomingObj);
                continue;
            }

            if (existingValue is JsonArray existingArray && incomingValue is JsonArray incomingArray)
            {
                MergeArray(existingArray, incomingArray);
            }
        }
    }

    private static void MergeArray(JsonArray target, JsonArray incoming)
    {
        var seen = new HashSet<string>(target.Select(GetArrayItemKey), StringComparer.OrdinalIgnoreCase);
        foreach (var incomingValue in incoming)
        {
            var key = GetArrayItemKey(incomingValue);
            if (!seen.Contains(key))
            {
                target.Add(incomingValue?.DeepClone());
                seen.Add(key);
            }
        }
    }

    private static string GetArrayItemKey(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            if (obj["fileUrl"] is JsonValue v1 && v1.TryGetValue<string>(out var fileUrl) && !string.IsNullOrWhiteSpace(fileUrl))
            {
                return $"fileurl:{fileUrl}";
            }

            if (obj["url"] is JsonValue v2 && v2.TryGetValue<string>(out var url) && !string.IsNullOrWhiteSpace(url))
            {
                return $"url:{url}";
            }
        }

        return node?.ToJsonString() ?? string.Empty;
    }

    private static bool IsNullOrEmpty(JsonNode? node)
    {
        if (node == null)
        {
            return true;
        }

        if (node is JsonValue value && value.TryGetValue<string>(out var stringValue))
        {
            return string.IsNullOrWhiteSpace(stringValue);
        }

        if (node is JsonArray array)
        {
            return array.Count == 0;
        }

        if (node is JsonObject obj)
        {
            return obj.Count == 0;
        }

        return false;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement root, string name, out JsonElement value)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? ReadJsonString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetPropertyIgnoreCase(root, name, out var element))
            {
                continue;
            }

            if (element.ValueKind == JsonValueKind.String)
            {
                return element.GetString();
            }

            if (element.ValueKind == JsonValueKind.Number ||
                element.ValueKind == JsonValueKind.True ||
                element.ValueKind == JsonValueKind.False)
            {
                return element.ToString();
            }
        }

        return null;
    }

    private static List<string> ReadJsonStringArray(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetPropertyIgnoreCase(root, name, out var element) || element.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            return element.EnumerateArray()
                .Select(item =>
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        return item.GetString();
                    }

                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        return ReadJsonString(item, "url", "Url", "src", "Src", "href", "Href", "fileUrl", "file_url");
                    }

                    return item.ToString();
                })
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return new List<string>();
    }

    private readonly record struct DataCoverage(int Images, int Attachments, int Specifications);

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
                var isSearchaniseListing =
                    (!string.IsNullOrWhiteSpace(selectors.ProductListSelector) &&
                     selectors.ProductListSelector.Contains("snize", StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(selectors.CategoryLandingUrl) &&
                     selectors.CategoryLandingUrl.Contains("search-results-page", StringComparison.OrdinalIgnoreCase)) ||
                    site.BaseUrl.Contains("search-results-page", StringComparison.OrdinalIgnoreCase);

                if (isSearchaniseListing)
                {
                    return site;
                }

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

        var scrapedCategories = ResolveScrapedCategories(scrapedProduct);
        if (scrapedCategories.Count > 0)
        {
            processed.Categories = scrapedCategories;
        }

        processed.SuggestedCategory = processed.Categories.LastOrDefault() ?? processed.SuggestedCategory;

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
            .Split(new[] { '>', '|', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(v => v.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();
    }

    private static List<string> ResolveScrapedCategories(ScrapedProduct scrapedProduct)
    {
        if (scrapedProduct.Attributes.TryGetValue("category_path", out var categoryPathValue) &&
            !string.IsNullOrWhiteSpace(categoryPathValue))
        {
            var fromPath = SplitCategoryPath(categoryPathValue);
            if (fromPath.Count > 0)
            {
                return fromPath;
            }
        }

        if (!string.IsNullOrWhiteSpace(scrapedProduct.Category))
        {
            var fromCategory = SplitCategoryPath(scrapedProduct.Category);
            if (fromCategory.Count > 0)
            {
                return fromCategory;
            }
        }

        return new List<string>();
    }

    private static void ApplyProviderBrandAndCategory(ScrapedProduct product, string? providerName)
    {
        product.Attributes ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(providerName))
        {
            var normalizedProvider = providerName.Trim();
            if (!string.IsNullOrWhiteSpace(normalizedProvider))
            {
                if (normalizedProvider.Contains("idsupply", StringComparison.OrdinalIgnoreCase))
                {
                    product.Brand = "Festo";
                    product.Attributes["brand"] = "Festo";
                }
                else
                {
                    product.Brand = normalizedProvider;
                    product.Attributes["brand"] = normalizedProvider;
                    product.Attributes["supplier_name"] = normalizedProvider;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(product.Category) &&
            product.Attributes.TryGetValue("category_path", out var categoryPath) &&
            !string.IsNullOrWhiteSpace(categoryPath))
        {
            var splitPath = SplitCategoryPath(categoryPath);
            if (splitPath.Count > 0)
            {
                product.Category = splitPath[^1];
            }
        }

        if (!string.IsNullOrWhiteSpace(product.Category))
        {
            product.Category = product.Category.Trim();
            product.Attributes["category"] = product.Category;
        }
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
