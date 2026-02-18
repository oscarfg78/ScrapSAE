namespace ScrapSAE.Api.Services;

public sealed class RescrapeJobBackgroundService : BackgroundService
{
    private readonly IRescrapeJobService _rescrapeJobService;
    private readonly ILogger<RescrapeJobBackgroundService> _logger;

    public RescrapeJobBackgroundService(
        IRescrapeJobService rescrapeJobService,
        ILogger<RescrapeJobBackgroundService> logger)
    {
        _rescrapeJobService = rescrapeJobService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RescrapeJobBackgroundService iniciado.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _rescrapeJobService.ProcessNextQueuedJobAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Ignore graceful cancellation.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error procesando cola de rescrape.");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}
