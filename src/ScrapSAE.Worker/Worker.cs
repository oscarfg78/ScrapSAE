using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Interfaces;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace ScrapSAE.Worker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IScrapingService _scrapingService;
    private readonly IStagingService _stagingService;
    private readonly IAIProcessorService _aiProcessorService;
    private readonly ISyncLogService _syncLogService;
    private readonly IFlashlySyncService _flashlySyncService;
    private readonly ICsvExportService _csvExportService;
    private readonly SyncOptionsConfig _syncOptions;
    private readonly CsvExportConfig _csvExportConfig;
    // We use a simple tracking dictionary to avoid running the same site multiple times in the same minute
    private readonly Dictionary<Guid, DateTime> _lastRunTimes = new();
    // Random instance for human-like delays
    private readonly Random _random = new();

    public Worker(
        ILogger<Worker> logger,
        IScrapingService scrapingService,
        IStagingService stagingService,
        IAIProcessorService aiProcessorService,
        ISyncLogService syncLogService,
        IFlashlySyncService flashlySyncService,
        ICsvExportService csvExportService,
        IOptions<SyncOptionsConfig> syncOptions,
        IOptions<CsvExportConfig> csvExportConfig)
    {
        _logger = logger;
        _scrapingService = scrapingService;
        _stagingService = stagingService;
        _aiProcessorService = aiProcessorService;
        _syncLogService = syncLogService;
        _flashlySyncService = flashlySyncService;
        _csvExportService = csvExportService;
        _syncOptions = syncOptions.Value;
        _csvExportConfig = csvExportConfig.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker starting loop at: {time}", DateTimeOffset.Now);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var sites = await _stagingService.GetActiveSitesAsync();
                _logger.LogDebug("Found {SiteCount} active sites", sites.Count());

                foreach (var site in sites)
                {
                    if (ShouldRun(site))
                    {
                        _logger.LogInformation("Starting scraping for site: {SiteName}", site.Name);
                        var siteStartedAt = DateTime.UtcNow;
                        await LogSyncAsync(site, "scrape", "start", "Inicio de scraping.");

                        // Apply site-specific defaults if max products not configured
                        if (site.MaxProductsPerScrape == 0)
                        {
                            if (site.Name.Equals("Festo", StringComparison.OrdinalIgnoreCase))
                            {
                                site.MaxProductsPerScrape = 10;
                            }
                        }

                        try 
                        {
                            // Random delay before scraping to avoid detection (3-8 seconds)
                            var delayBeforeScraping = _random.Next(3000, 8000);
                            await Task.Delay(delayBeforeScraping, stoppingToken);

                            var products = await _scrapingService.ScrapeAsync(site, stoppingToken);
                            int savedCount = 0;

                            foreach (var scrapedProduct in products)
                            {
                                // Check if we've reached the max products limit for this site
                                if (site.MaxProductsPerScrape > 0 && savedCount >= site.MaxProductsPerScrape)
                                {
                                    _logger.LogInformation("Reached max products limit ({Max}) for site {SiteName}", site.MaxProductsPerScrape, site.Name);
                                    break;
                                }

                                var stagingProduct = new StagingProduct
                                {
                                    SiteId = site.Id,
                                    SkuSource = scrapedProduct.SkuSource,
                                    RawData = JsonSerializer.Serialize(scrapedProduct),
                                    SourceUrl = scrapedProduct.SourceUrl,
                                    Status = "pending"
                                };

                                stagingProduct.AIProcessedJson = await BuildAiJsonAsync(scrapedProduct, stoppingToken);

                                await _stagingService.UpsertProductAsync(stagingProduct);
                                savedCount++;

                                // Small random delay between saving products (100-500ms) to avoid hammering the database
                                if (savedCount < products.Count() && (site.MaxProductsPerScrape == 0 || savedCount < site.MaxProductsPerScrape))
                                {
                                    var delayBetweenProducts = _random.Next(100, 500);
                                    await Task.Delay(delayBetweenProducts, stoppingToken);
                                }
                            }

                            // Update last run time after successful completion
                            _lastRunTimes[site.Id] = DateTime.UtcNow;
                            _logger.LogInformation("Finished scraping site {SiteName}. Saved {Count} products.", site.Name, savedCount);
                            var durationMs = (int)(DateTime.UtcNow - siteStartedAt).TotalMilliseconds;
                            await LogSyncAsync(site, "scrape", "success", $"Scraping finalizado. Productos guardados: {savedCount}.", durationMs);

                            if (_syncOptions.AutoSync)
                            {
                                await ExecuteOutboundSyncAsync(site, stoppingToken);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing site {SiteName}", site.Name);
                            var durationMs = (int)(DateTime.UtcNow - siteStartedAt).TotalMilliseconds;
                            await LogSyncAsync(site, "scrape", "error", ex.Message, durationMs);
                        }

                        // Random delay between processing sites (5-12 seconds) to appear more human-like
                        var delayBetweenSites = _random.Next(5000, 12000);
                        await Task.Delay(delayBetweenSites, stoppingToken);
                    }
                    else
                    {
                        _logger.LogDebug("Skipping site {SiteName}: cron condition not met", site.Name);
                    }
                }

                // Check again in 1 minute
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in worker main loop");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    private async Task ExecuteOutboundSyncAsync(SiteProfile site, CancellationToken cancellationToken)
    {
        var target = (_syncOptions.TargetSystem ?? "Flashly").Trim();
        var isFlashlyTarget = target.Equals("Flashly", StringComparison.OrdinalIgnoreCase) ||
                              target.Equals("Both", StringComparison.OrdinalIgnoreCase);
        var isCsvTarget = target.Equals("CSV", StringComparison.OrdinalIgnoreCase);

        var validatedForSite = (await _stagingService.GetProductsByStatusAsync("validated"))
            .Where(p => p.SiteId == site.Id)
            .ToList();

        if (validatedForSite.Count == 0)
        {
            _logger.LogInformation("No validated products for site {SiteName}; skipping outbound sync/export.", site.Name);
            return;
        }

        if (isFlashlyTarget)
        {
            await LogSyncAsync(site, "flashly_sync", "start", $"Iniciando sincronización Flashly de {validatedForSite.Count} productos.");
            var result = await _flashlySyncService.SyncProductsAsync(validatedForSite, cancellationToken);
            var details = JsonSerializer.Serialize(new
            {
                result.Created,
                result.Updated,
                Errors = result.Errors.Select(e => new { e.SourceSku, e.Error }),
                result.JobId
            });

            await _syncLogService.LogOperationAsync(new SyncLog
            {
                OperationType = "flashly_sync",
                SiteId = site.Id,
                Status = result.Success ? "success" : "error",
                Message = result.Message,
                Details = details
            });

            if (result.Success)
            {
                var errorsBySku = result.Errors
                    .Where(e => !string.IsNullOrWhiteSpace(e.SourceSku))
                    .ToDictionary(e => e.SourceSku.Trim(), e => e.Error, StringComparer.OrdinalIgnoreCase);

                foreach (var product in validatedForSite)
                {
                    if (!string.IsNullOrWhiteSpace(product.SkuSource) &&
                        errorsBySku.TryGetValue(product.SkuSource.Trim(), out var errorMessage))
                    {
                        await _stagingService.UpdateFlashlySyncInfoAsync(
                            product.Id,
                            "error",
                            product.FlashlyProductId,
                            product.FlashlySyncedAt,
                            errorMessage);
                        await _stagingService.UpdateProductStatusAsync(product.Id, "error", errorMessage);
                    }
                    else
                    {
                        await _stagingService.UpdateFlashlySyncInfoAsync(
                            product.Id,
                            "synced",
                            product.FlashlyProductId,
                            DateTime.UtcNow,
                            null);
                        await _stagingService.UpdateProductStatusAsync(product.Id, "synced");
                    }
                }

                await LogSyncAsync(site, "flashly_sync", "success", result.Message ?? "Sincronización Flashly completada.");
            }
            else
            {
                await LogSyncAsync(site, "flashly_sync", "error", result.Message ?? "Error en sincronización Flashly.");
            }
        }

        if (isCsvTarget)
        {
            var outputPath = GenerateCsvPath(site.Name);
            await LogSyncAsync(site, "csv_export", "start", $"Generando CSV con {validatedForSite.Count} productos.");
            await _csvExportService.ExportProductsToCsvAsync(validatedForSite, outputPath, cancellationToken);
            await _stagingService.UpdateProductsStatusAsync(validatedForSite.Select(p => p.Id), "synced");
            await LogSyncAsync(site, "csv_export", "success", $"CSV exportado en {outputPath}");
        }
    }

    private string GenerateCsvPath(string siteName)
    {
        var safeSiteName = string.Concat(siteName.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)));
        var pattern = _csvExportConfig.FileNamePattern;

        var fileName = pattern.Contains("{0", StringComparison.Ordinal)
            ? string.Format(pattern, DateTime.Now)
            : pattern;

        if (!string.IsNullOrWhiteSpace(safeSiteName))
        {
            fileName = $"{Path.GetFileNameWithoutExtension(fileName)}_{safeSiteName}{Path.GetExtension(fileName)}";
        }

        var outputDirectory = _csvExportConfig.OutputDirectory;
        return Path.Combine(outputDirectory, fileName);
    }

    private bool ShouldRun(SiteProfile site)
    {
        if (string.IsNullOrEmpty(site.CronExpression))
        {
            return false;
        }

        // Special case: ALWAYS expression runs every iteration
        if (site.CronExpression.Equals("ALWAYS", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            var now = DateTime.UtcNow;
            var components = site.CronExpression.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            
            // Expected format: "minute hour day month weekday"
            // Example: "0 9 * * *" = Every day at 9:00 AM
            // Example: "0 */6 * * *" = Every 6 hours
            if (components.Length < 5)
            {
                _logger.LogWarning("Invalid cron expression for site {SiteName}: {CronExpression}", site.Name, site.CronExpression);
                return false;
            }

            var minuteStr = components[0];
            var hourStr = components[1];
            
            // Parse minute
            var targetMinutes = ParseCronField(minuteStr, 0, 59, now.Minute);
            if (targetMinutes == null || !targetMinutes.Contains(now.Minute))
            {
                return false;
            }

            // Parse hour
            var targetHours = ParseCronField(hourStr, 0, 23, now.Hour);
            if (targetHours == null || !targetHours.Contains(now.Hour))
            {
                return false;
            }

            // Check if we've already run this site in the last minute to avoid duplicate runs
            if (_lastRunTimes.TryGetValue(site.Id, out var lastRun))
            {
                if ((now - lastRun).TotalSeconds < 60)
                {
                    return false;
                }
            }

            _logger.LogInformation("Cron check passed for site {SiteName}: {CronExpression}", site.Name, site.CronExpression);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evaluating cron expression for site {SiteName}: {CronExpression}", site.Name, site.CronExpression);
            return false;
        }
    }

    /// <summary>
    /// Parses a cron field and returns matching values.
    /// Supports: specific numbers, wildcards (*), ranges (1-5), steps (*/5, 1-10/2)
    /// </summary>
    private HashSet<int>? ParseCronField(string field, int minValue, int maxValue, int currentValue)
    {
        if (field == "*")
        {
            return new HashSet<int>(Enumerable.Range(minValue, maxValue - minValue + 1));
        }

        var result = new HashSet<int>();

        // Handle comma-separated values
        var parts = field.Split(',');
        foreach (var part in parts)
        {
            // Handle step values: "*/5" or "1-10/2"
            if (part.Contains('/'))
            {
                var (baseRange, step) = ParseStepField(part, minValue, maxValue);
                if (baseRange == null || step == null)
                    continue;

                for (int i = baseRange.Item1; i <= baseRange.Item2; i += step.Value)
                {
                    if (i <= maxValue)
                        result.Add(i);
                }
            }
            // Handle ranges: "1-5"
            else if (part.Contains('-'))
            {
                var rangeParts = part.Split('-');
                if (int.TryParse(rangeParts[0].Trim(), out int start) && 
                    int.TryParse(rangeParts[1].Trim(), out int end))
                {
                    for (int i = Math.Max(start, minValue); i <= Math.Min(end, maxValue); i++)
                    {
                        result.Add(i);
                    }
                }
            }
            // Handle specific numbers
            else if (int.TryParse(part.Trim(), out int value))
            {
                if (value >= minValue && value <= maxValue)
                {
                    result.Add(value);
                }
            }
        }

        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// Parses step field like "*/5" or "1-10/2"
    /// Returns tuple of (start, end) range and step value
    /// </summary>
    private (Tuple<int, int>?, int?) ParseStepField(string field, int minValue, int maxValue)
    {
        var parts = field.Split('/');
        if (parts.Length != 2 || !int.TryParse(parts[1].Trim(), out int step))
            return (null, null);

        if (step <= 0)
            return (null, null);

        if (parts[0].Trim() == "*")
        {
            return (new Tuple<int, int>(minValue, maxValue), step);
        }

        // Range format like "1-10/2"
        var rangeParts = parts[0].Split('-');
        if (rangeParts.Length == 2 &&
            int.TryParse(rangeParts[0].Trim(), out int start) &&
            int.TryParse(rangeParts[1].Trim(), out int end))
        {
            return (new Tuple<int, int>(start, end), step);
        }

        return (null, null);
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
            scrapedProduct.Brand,
            scrapedProduct.Category,
            scrapedProduct.Attributes,
            // Include these so the AI *could* use them, but more importantly so we don't lose them if we just use the rawPayload later
            scrapedProduct.ImageUrls,
            scrapedProduct.Attachments
        };

        var rawData = JsonSerializer.Serialize(rawPayload);

        try
        {
            var processed = await _aiProcessorService.ProcessProductAsync(rawData, cancellationToken);
            
            // Merge logic: Ensure we don't lose data found by the scraper even if AI misses it or doesn't return it
            processed.Sku ??= scrapedProduct.SkuSource;
            processed.Name = string.IsNullOrWhiteSpace(processed.Name) ? (scrapedProduct.Title ?? string.Empty) : processed.Name;
            processed.Description = string.IsNullOrWhiteSpace(processed.Description) ? (scrapedProduct.Description ?? string.Empty) : processed.Description;
            processed.Brand ??= scrapedProduct.Brand;
            processed.Price ??= scrapedProduct.Price;

            // Merge Images
            if (scrapedProduct.ImageUrls != null && scrapedProduct.ImageUrls.Any())
            {
                processed.Images ??= new List<string>();
                foreach (var img in scrapedProduct.ImageUrls)
                {
                    if (!processed.Images.Contains(img))
                    {
                        processed.Images.Add(img);
                    }
                }
            }
            
            // Merge Attachments
            if (scrapedProduct.Attachments != null && scrapedProduct.Attachments.Any())
            {
                processed.Attachments ??= new List<ProductAttachment>();
                // We assume filename/url is the unique key
                var existingUrls = new HashSet<string>(processed.Attachments.Select(a => a.FileUrl ?? ""));
                
                foreach (var att in scrapedProduct.Attachments)
                {
                    if (!existingUrls.Contains(att.FileUrl ?? ""))
                    {
                        processed.Attachments.Add(att);
                        existingUrls.Add(att.FileUrl ?? "");
                    }
                }
            }

            return JsonSerializer.Serialize(processed);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI processing failed for SKU {Sku}. Falling back to raw fields.", scrapedProduct.SkuSource);
            return JsonSerializer.Serialize(new
            {
                scrapedProduct.Title,
                scrapedProduct.Price,
                scrapedProduct.ImageUrl,
                scrapedProduct.Description,
                scrapedProduct.Attributes,
                scrapedProduct.ImageUrls,
                scrapedProduct.Attachments
            });
        }
    }

    private async Task LogSyncAsync(SiteProfile site, string operationType, string status, string message, int? durationMs = null)
    {
        try
        {
            await _syncLogService.LogOperationAsync(new SyncLog
            {
                OperationType = operationType,
                SiteId = site.Id,
                Status = status,
                Message = message,
                DurationMs = durationMs
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write sync log for site {SiteName}", site.Name);
        }
    }
}
