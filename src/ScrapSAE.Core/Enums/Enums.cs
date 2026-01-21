namespace ScrapSAE.Core.Enums;

/// <summary>
/// Estados posibles de un producto en staging
/// </summary>
public enum ProductStatus
{
    /// <summary>Pendiente de validación</summary>
    Pending,
    
    /// <summary>Validado y listo para sincronizar</summary>
    Validated,
    
    /// <summary>Sincronizado con SAE</summary>
    Synced,
    
    /// <summary>Error en procesamiento</summary>
    Error,
    
    /// <summary>Producto descontinuado</summary>
    Discontinued
}

/// <summary>
/// Tipos de operación para logs
/// </summary>
public enum OperationType
{
    Scrape,
    AIProcess,
    SAESync,
    Webhook
}

/// <summary>
/// Estados de operación
/// </summary>
public enum OperationStatus
{
    Success,
    Error,
    Retry
}
