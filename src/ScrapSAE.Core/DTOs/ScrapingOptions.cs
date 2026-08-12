namespace ScrapSAE.Core.DTOs;

/// <summary>
/// Opciones de configuración de scraping dinámicas.
/// </summary>
public class ScrapingOptions
{
    /// <summary>
    /// Indica si la Inteligencia Artificial debe utilizarse durante el proceso de scrap.
    /// </summary>
    public bool UseAI { get; set; } = true;

    /// <summary>
    /// Modo headless para el navegador.
    /// </summary>
    public bool IsHeadless { get; set; } = true;

    /// <summary>
    /// Login manual por el usuario.
    /// </summary>
    public bool ManualLogin { get; set; } = false;

    /// <summary>
    /// Límite opcional de productos a procesar.
    /// </summary>
    public int? MaxProducts { get; set; }
}
