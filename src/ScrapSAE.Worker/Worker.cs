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

                foreach (var site in sites)
                {
                    if (ShouldRun(site))
                    {
                        _logger.LogInformation("Starting scraping for site: {SiteName}", site.Name);
                        _lastRunTimes[site.Id] = DateTime.UtcNow;

                        try 
                        {
                            var products = await _scrapingService.ScrapeAsync(site, stoppingToken);
                            int savedCount = 0;

                            foreach (var scrapedProduct in products)
                            {
                                var stagingProduct = new StagingProduct
                                {
                                    SiteId = site.Id,
                                    SkuSource = scrapedProduct.SkuSource,
                                    RawData = scrapedProduct.RawHtml, // Or serialize the whole object if needed
                                    Status = "pending",
                                    // Serialize attributes or other data if needed
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
                            }

                            _logger.LogInformation("Finished scraping site {SiteName}. Saved {Count} products.", site.Name, savedCount);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing site {SiteName}", site.Name);
                        }
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
            // If no cron, run only if never run before (or handled differently)
            // For now, let's avoid infinite loops by checking if it ran in the last hour?
            // User requirement said "Check cron expression".
            return false; 
        }

        // TODO: Implement robust Cron parsing library (e.g. NCrontab).
        // For now, we implement a basic check or just return true for testing purposes if it matches current hour/minute?
        // Let's implement a very simple check that allows running once per day if formatted as "0 H * * *"
        
        // TEMPORARY: For demonstration/testing, run if we haven't run in the last hour
        // Ideally we should parse the cron string.
        // Assuming format "Minute Hour * * *"
        var components = site.CronExpression.Split(' ');
        if (components.Length >= 2) 
        {
             if (int.TryParse(components[1], out int hour) && int.TryParse(components[0], out int minute))
             {
                 var now = DateTime.UtcNow; 
                 _logger.LogDebug("Checking cron for {Site}: Target={Minute} {Hour}, Now={NowMin} {NowHour}", site.Name, minute, hour, now.Minute, now.Hour);
                 
                 // If current time matches target time (within reason) and we haven't run today
                 if (now.Hour == hour && now.Minute == minute)
                 {
                     if (_lastRunTimes.TryGetValue(site.Id, out var lastRun))
                     {
                         // Don't run again if run within the last 55 minutes
                         if ((now - lastRun).TotalMinutes < 55) return false;
                     }
                     return true;
                 }
             }
        }

        if (site.CronExpression == "ALWAYS") return true;
        
        return false;
    }
}
