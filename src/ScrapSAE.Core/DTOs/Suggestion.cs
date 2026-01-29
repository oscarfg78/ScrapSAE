namespace ScrapSAE.Core.DTOs;

/// <summary>
/// Representa una sugerencia de mejora generada por el análisis post-ejecución
/// </summary>
public class Suggestion
{
    /// <summary>
    /// Tipo de sugerencia (ai_selector_update, strategy_reorder, etc.)
    /// </summary>
    public string Type { get; set; } = string.Empty;
    
    /// <summary>
    /// Mensaje descriptivo de la sugerencia
    /// </summary>
    public string Message { get; set; } = string.Empty;
    
    /// <summary>
    /// Nivel de confianza de la sugerencia (0.0 a 1.0)
    /// </summary>
    public double Confidence { get; set; }
    
    /// <summary>
    /// Datos adicionales específicos de la sugerencia
    /// </summary>
    public Dictionary<string, object>? Data { get; set; }
}
