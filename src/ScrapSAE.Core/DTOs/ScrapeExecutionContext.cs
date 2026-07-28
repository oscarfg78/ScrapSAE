namespace ScrapSAE.Core.DTOs;

/// <summary>
/// Parámetros inmutables de configuración para una ejecución de scraping.
/// Reemplaza el uso de environment variables para pasar configuración entre
/// el endpoint HTTP y el servicio de scraping, evitando condiciones de carrera.
/// </summary>
/// <remarks>
/// Arquitectura de capas:
/// Wizard (configura SiteProfile) → Endpoint (construye ScrapeExecutionContext)
///   → ScrapingRunner (usa StrategyType + Strategies[]) → StrategyOrchestrator → Estrategia
/// </remarks>
public sealed record ScrapeExecutionContext
{
    /// <summary>
    /// Indica si el browser debe ejecutarse en modo headless (sin ventana visible).
    /// Default: true
    /// </summary>
    public bool IsHeadless { get; init; } = true;

    /// <summary>
    /// Si es true, el browser se abre en modo visible para que el usuario haga login manual.
    /// </summary>
    public bool ManualLogin { get; init; } = false;

    /// <summary>
    /// Si es true, mantiene el browser abierto después del scrape (para depuración).
    /// </summary>
    public bool KeepBrowser { get; init; } = false;

    /// <summary>
    /// Si es true, usa screenshots de productos como fallback cuando el HTML no es suficiente.
    /// </summary>
    public bool ScreenshotFallback { get; init; } = false;

    /// <summary>
    /// Permite sobreescribir el MaxProductsPerScrape del SiteProfile para esta ejecución.
    /// Si es null, se usa el valor del SiteProfile.
    /// Útil para el test del wizard (limitar a 2 productos).
    /// </summary>
    public int? MaxProductsOverride { get; init; } = null;

    /// <summary>
    /// Tracker para recolectar logs de ejecución detallados.
    /// </summary>
    public ScrapingLogTracker? LogTracker { get; init; } = null;

    /// <summary>
    /// Contexto de ejecución por defecto (headless, sin login manual).
    /// </summary>
    public static ScrapeExecutionContext Default => new();

    /// <summary>
    /// Contexto de ejecución para la prueba del wizard (2 productos, headless).
    /// </summary>
    public static ScrapeExecutionContext WizardTest => new()
    {
        IsHeadless = true,
        ManualLogin = false,
        KeepBrowser = false,
        MaxProductsOverride = 2
    };
}
