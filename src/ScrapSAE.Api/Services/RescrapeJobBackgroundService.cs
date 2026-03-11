namespace ScrapSAE.Api.Services;

public sealed class RescrapeJobBackgroundService : BackgroundService
{
    private readonly IRescrapeJobService _rescrapeJobService;
    private readonly ILogger<RescrapeJobBackgroundService> _logger;
    private DateTime _nextConfigurationWarningUtc = DateTime.MinValue;
    private DateTime _nextConnectivityWarningUtc = DateTime.MinValue;

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
            var delay = TimeSpan.FromSeconds(2);
            try
            {
                await _rescrapeJobService.ProcessNextQueuedJobAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Ignore graceful cancellation.
            }
            catch (SupabaseConfigurationException ex)
            {
                if (DateTime.UtcNow >= _nextConfigurationWarningUtc)
                {
                    _logger.LogWarning("Cola de rescrape en pausa por configuración inválida de Supabase: {Message}", ex.Message);
                    _nextConfigurationWarningUtc = DateTime.UtcNow.AddMinutes(1);
                }

                delay = TimeSpan.FromSeconds(10);
            }
            catch (HttpRequestException ex) when (LooksLikeSupabaseConnectivityIssue(ex))
            {
                if (DateTime.UtcNow >= _nextConnectivityWarningUtc)
                {
                    _logger.LogWarning("No se pudo conectar a Supabase al procesar rescrape: {Message}", ex.Message);
                    _nextConnectivityWarningUtc = DateTime.UtcNow.AddSeconds(30);
                }

                delay = TimeSpan.FromSeconds(10);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error procesando cola de rescrape.");
            }

            await Task.Delay(delay, stoppingToken);
        }
    }

    private static bool LooksLikeSupabaseConnectivityIssue(HttpRequestException ex)
    {
        var message = ex.Message ?? string.Empty;
        return message.Contains("supabase", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("Host desconocido", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("No such host is known", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("Name or service not known", StringComparison.OrdinalIgnoreCase);
    }
}
