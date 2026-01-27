using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;

namespace ScrapSAE.Core.Interfaces;

/// <summary>
/// Interface para el servicio de integración con Aspel SAE
/// </summary>
public interface ISAEIntegrationService
{
    /// <summary>
    /// Prueba la conexión con la base de datos de SAE
    /// </summary>
    Task<bool> TestConnectionAsync();
    
    /// <summary>
    /// Obtiene todos los productos del inventario
    /// </summary>
    Task<IEnumerable<ProductSAE>> GetAllProductsAsync();
    
    /// <summary>
    /// Obtiene un producto por su SKU
    /// </summary>
    Task<ProductSAE?> GetProductBySkuAsync(string sku);
    
    /// <summary>
    /// Obtiene las líneas de producto disponibles
    /// </summary>
    Task<IEnumerable<ProductLine>> GetProductLinesAsync();
    
    /// <summary>
    /// Actualiza un producto existente
    /// </summary>
    Task<bool> UpdateProductAsync(ProductUpdate product);
    
    /// <summary>
    /// Crea un nuevo producto
    /// </summary>
    Task<bool> CreateProductAsync(ProductCreate product);
    
    /// <summary>
    /// Actualiza el stock de un producto
    /// </summary>
    Task<bool> UpdateStockAsync(string sku, decimal quantity);
    
    /// <summary>
    /// Actualiza el precio de un producto
    /// </summary>
    Task<bool> UpdatePriceAsync(string sku, decimal price);
    
    /// <summary>
    /// Verifica si un SKU existe en SAE
    /// </summary>
    Task<bool> ValidateSkuExistsAsync(string sku);
}

/// <summary>
/// Interface para el servicio de scraping
/// </summary>
public interface IScrapingService
{
    /// <summary>
    /// Ejecuta el scraping de un sitio proveedor
    /// </summary>
    Task<IEnumerable<ScrapedProduct>> ScrapeAsync(SiteProfile site, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Descarga una imagen de producto
    /// </summary>
    Task<byte[]?> DownloadImageAsync(string imageUrl);
}

/// <summary>
/// Interface para el procesador de IA
/// </summary>
public interface IAIProcessorService
{
    /// <summary>
    /// Procesa datos crudos de un producto y los estructura
    /// </summary>
    Task<ProcessedProduct> ProcessProductAsync(string rawData, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Sugiere una categoría de SAE para un producto
    /// </summary>
    Task<CategorySuggestion> SuggestCategoryAsync(string productDescription, IEnumerable<ProductLine> availableLines);

    /// <summary>
    /// Analiza selectores de scraping con apoyo de IA e imágenes.
    /// </summary>
    Task<SelectorSuggestion> AnalyzeSelectorsAsync(SelectorAnalysisRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface para el servicio de staging (Supabase)
/// </summary>
public interface IStagingService
{
    Task<StagingProduct> CreateProductAsync(StagingProduct product);
    Task<StagingProduct?> GetProductBySourceSkuAsync(Guid siteId, string skuSource);
    Task<IEnumerable<StagingProduct>> GetPendingProductsAsync();
    Task UpdateProductStatusAsync(Guid id, string status, string? notes = null);
    Task UpdateProductDataAsync(Guid id, string aiProcessedJson);
    /// <summary>
    /// Obtiene los sitios configurados y activos
    /// </summary>
    Task<IEnumerable<SiteProfile>> GetActiveSitesAsync();
}

/// <summary>
/// Interface para el servicio de logs (Supabase)
/// </summary>
public interface ISyncLogService
{
    Task LogOperationAsync(SyncLog log);
    Task<IEnumerable<SyncLog>> GetLogsAsync(DateTime from, DateTime to);
}

/// <summary>
/// Interface para el servicio de reportes (Supabase)
/// </summary>
public interface IExecutionReportService
{
    Task CreateReportAsync(ExecutionReport report);
    Task<IEnumerable<ExecutionReport>> GetReportsAsync(DateTime from, DateTime to);
}

/// <summary>
/// Interface para el servicio de webhooks
/// </summary>
public interface IWebhookService
{
    /// <summary>
    /// Envía notificación a la tienda en línea
    /// </summary>
    Task<bool> NotifyProductUpdateAsync(ProductWebhookPayload payload);
}

/// <summary>
/// Interface para el planificador de tareas
/// </summary>
public interface ISchedulerService
{
    /// <summary>
    /// Registra un sitio para ejecución programada
    /// </summary>
    void ScheduleSite(SiteProfile site);
    
    /// <summary>
    /// Ejecuta inmediatamente el scraping de un sitio
    /// </summary>
    Task ExecuteNowAsync(Guid siteId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Obtiene el próximo tiempo de ejecución para un sitio
    /// </summary>
    DateTime? GetNextExecutionTime(Guid siteId);
}

public enum ScrapeRunState
{
    Idle,
    Running,
    Paused,
    Stopped,
    Completed,
    Error
}

public sealed class ScrapeStatus
{
    public Guid SiteId { get; set; }
    public ScrapeRunState State { get; set; } = ScrapeRunState.Idle;
    public string? Message { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

public interface IScrapeControlService
{
    ScrapeStatus GetStatus(Guid siteId);
    CancellationToken Start(Guid siteId);
    void MarkCompleted(Guid siteId, string? message = null);
    void MarkError(Guid siteId, string message);
    void Pause(Guid siteId);
    void Resume(Guid siteId);
    void Stop(Guid siteId);
    Task WaitIfPausedAsync(Guid siteId, CancellationToken cancellationToken);
}
