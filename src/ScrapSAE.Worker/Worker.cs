using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Interfaces;
using Newtonsoft.Json;

namespace ScrapSAE.Worker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IScrapingService _scrapingService;
    private readonly IStagingService _stagingService;
    // We use a simple tracking dictionary to avoid running the same site multiple times in the same minute
    private readonly Dictionary<Guid, DateTime> _lastRunTimes = new();
    // Random instance for human-like delays
    private readonly Random _random = new();

    public Worker(
        ILogger<Worker> logger,
        IScrapingService scrapingService,
        IStagingService stagingService)
    {
        _logger = logger;
        _scrapingService = scrapingService;
        _stagingService = stagingService;
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
                                    RawData = scrapedProduct.RawHtml,
                                    Status = "pending",
                                    AIProcessedJson = JsonConvert.SerializeObject(new { 
                                        scrapedProduct.Title, 
                                        scrapedProduct.Price, 
                                        scrapedProduct.ImageUrl,
                                        scrapedProduct.Description,
                                        scrapedProduct.Attributes
                                    })
                                };

                                await _stagingService.CreateProductAsync(stagingProduct);
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
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing site {SiteName}", site.Name);
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
}
