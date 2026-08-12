namespace ScrapSAE.Infrastructure.Scraping;

using Microsoft.Extensions.Logging;

public interface IAIEfficiencyMonitor
{
    bool IsAIEnabled { get; set; }
    int ConsecutiveUnproductiveCount { get; }
    int Threshold { get; }
    bool WarningTriggered { get; }
    event EventHandler? EfficiencyWarningTriggered;

    void RecordExtractionResult(bool aiAttempted, int fieldsExtractedByAI, int totalFieldsExtracted);
    void DisableAI();
    void Reset();
}

public class AIEfficiencyMonitor : IAIEfficiencyMonitor
{
    private readonly ILogger<AIEfficiencyMonitor>? _logger;
    private int _consecutiveUnproductiveCount = 0;
    private bool _warningTriggered = false;

    public bool IsAIEnabled { get; set; } = true;
    public int ConsecutiveUnproductiveCount => _consecutiveUnproductiveCount;
    public int Threshold { get; }
    public bool WarningTriggered => _warningTriggered;

    public event EventHandler? EfficiencyWarningTriggered;

    public AIEfficiencyMonitor(int threshold = 3, ILogger<AIEfficiencyMonitor>? logger = null)
    {
        Threshold = threshold;
        _logger = logger;
    }

    public void RecordExtractionResult(bool aiAttempted, int fieldsExtractedByAI, int totalFieldsExtracted)
    {
        if (!IsAIEnabled || !aiAttempted)
        {
            return;
        }

        // Si la IA no aportó ningún campo adicional
        if (fieldsExtractedByAI == 0)
        {
            _consecutiveUnproductiveCount++;
            _logger?.LogWarning("IA intentada pero 0 campos adicionales extraídos por IA. Consecutivos inefectivos: {Count}/{Threshold}",
                _consecutiveUnproductiveCount, Threshold);

            if (_consecutiveUnproductiveCount >= Threshold && !_warningTriggered)
            {
                _warningTriggered = true;
                _logger?.LogWarning("Alerta de ineficiencia de IA activada tras {Threshold} intentos inefectivos consecutivos.", Threshold);
                EfficiencyWarningTriggered?.Invoke(this, EventArgs.Empty);
            }
        }
        else
        {
            // La IA aportó campos útiles, reiniciamos el contador consecutivo
            _consecutiveUnproductiveCount = 0;
        }
    }

    public void DisableAI()
    {
        IsAIEnabled = false;
        _consecutiveUnproductiveCount = 0;
        _logger?.LogInformation("Uso de IA desactivado por el usuario o monitor de eficiencia.");
    }

    public void Reset()
    {
        _consecutiveUnproductiveCount = 0;
        _warningTriggered = false;
    }
}
