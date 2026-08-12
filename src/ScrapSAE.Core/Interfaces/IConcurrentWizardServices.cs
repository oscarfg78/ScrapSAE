using ScrapSAE.Core.DTOs;

namespace ScrapSAE.Core.Interfaces;

// ─────────────────────────────────────────────────────────────────────────────
// Concurrent Scraping Wizard — Service Interfaces
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Servicio de ingesta de archivos Excel para el wizard concurrente.
/// Opera en modo streaming para soportar archivos con miles de filas.
/// </summary>
public interface IExcelIngestionService
{
    /// <summary>
    /// Lee las cabeceras y los primeros <paramref name="maxPreviewRows"/> registros
    /// del archivo Excel para que el usuario mapee columnas.
    /// </summary>
    Task<ExcelPreviewResult> PreviewAsync(string filePath, int maxPreviewRows = 10, CancellationToken cancellationToken = default);

    /// <summary>
    /// Emite filas del Excel como <see cref="ExcelProductRecord"/> usando streaming row-by-row.
    /// Acepta <paramref name="startRowIndex"/> para reanudar desde una fila específica (resume).
    /// </summary>
    IAsyncEnumerable<ExcelProductRecord> StreamRowsAsync(
        string filePath,
        ExcelColumnMapping mapping,
        int startRowIndex = 0,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Servicio de descubrimiento de selectores CSS/XPath mediante IA.
/// SOLO debe usarse durante la configuración inicial del wizard (Steps 1-3).
/// El engine de ejecución en batch NO debe tomar este servicio como dependencia.
/// </summary>
public interface ISelectorDiscoveryService
{
    /// <summary>
    /// Obtiene el HTML de la URL objetivo, lo envía al servicio IA y devuelve
    /// los selectores inferidos. En caso de fallo devuelve un <see cref="SelectorConfig"/>
    /// vacío sin lanzar excepción.
    /// </summary>
    Task<SelectorConfig> DiscoverSelectorsAsync(
        string targetUrl,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Engine principal de scraping concurrente.
/// Consume filas del Excel via Channel y ejecuta búsqueda + extracción
/// en paralelo sobre hasta 2 fuentes objetivo por SKU.
/// </summary>
public interface IConcurrentScrapingEngine : IAsyncDisposable
{
    /// <summary>
    /// Stream de eventos de progreso. El ViewModel suscribe con ObserveOn(Dispatcher)
    /// para actualizar la UI de forma thread-safe.
    /// </summary>
    IObservable<ScrapingProgressEvent> Progress { get; }

    /// <summary>
    /// Inicia la ejecución del batch. Lee filas desde el Excel de la sesión
    /// y ejecuta el pipeline Channel → Workers → Consolidator.
    /// </summary>
    Task StartAsync(ConcurrentWizardSession session, CancellationToken cancellationToken = default);

    /// <summary>Pausa el procesamiento: los workers terminan el item actual y bloquean.</summary>
    void Pause();

    /// <summary>Reanuda el procesamiento después de una pausa.</summary>
    void Resume();

    /// <summary>Detiene el procesamiento y preserva los resultados parciales.</summary>
    Task StopAsync();
}

/// <summary>
/// Repositorio para persistir y recuperar sesiones del Concurrent Scraping Wizard.
/// </summary>
public interface IWizardSessionRepository
{
    /// <summary>Persiste el estado completo de la sesión (cabecera + configuración).</summary>
    Task SaveAsync(ConcurrentWizardSession session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Append incremental de resultados cada N filas procesadas.
    /// Usa rename atómico .tmp → .json para evitar corrupción.
    /// </summary>
    Task SaveTickAsync(
        ConcurrentWizardSession session,
        IReadOnlyList<ConsolidatedProductResult> newResults,
        CancellationToken cancellationToken = default);

    /// <summary>Lista las sesiones guardadas (solo cabecera, sin resultados).</summary>
    Task<IReadOnlyList<ConcurrentWizardSession>> ListSavedSessionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Carga una sesión completa con sus resultados.</summary>
    Task<(ConcurrentWizardSession? Session, List<ConsolidatedProductResult> Results)> LoadAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>Trunca (elimina) los resultados posteriores a un índice específico.</summary>
    Task TruncateResultsAsync(string sessionId, int lastIndexToKeep, CancellationToken cancellationToken = default);

    /// <summary>Elimina los archivos de sesión.</summary>
    Task DeleteAsync(string sessionId, CancellationToken cancellationToken = default);
}
