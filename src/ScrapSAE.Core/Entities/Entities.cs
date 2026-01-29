namespace ScrapSAE.Core.Entities;

using System.Text.Json.Serialization;

/// <summary>
/// Configuración de un sitio proveedor para scraping
/// </summary>
public class SiteProfile
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string? LoginUrl { get; set; }
    public object? Selectors { get; set; } // JSONB from Supabase
    public string? CronExpression { get; set; }
    public bool RequiresLogin { get; set; }
    public string? CredentialsEncrypted { get; set; }
    public bool IsActive { get; set; } = true;
    public int MaxProductsPerScrape { get; set; } = 0; // 0 = unlimited
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    // Nuevas propiedades para el sistema adaptativo
    public Dictionary<string, List<string>> SecondarySelectors { get; set; } = new();
    public List<ScrapingStrategyDefinition> Strategies { get; set; } = new();
}

/// <summary>
/// Definición de una estrategia de scraping
/// </summary>
public class ScrapingStrategyDefinition
{
    public string StrategyName { get; set; } = string.Empty; // "Direct", "List", "Families"
    public int Priority { get; set; }
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// Producto en estado de staging
/// </summary>
public class StagingProduct
{
    public Guid Id { get; set; }
    public Guid SiteId { get; set; }
    public string? SkuSource { get; set; }
    public string? SkuSae { get; set; }
    public string? RawData { get; set; }
    public string? AIProcessedJson { get; set; }
    public string Status { get; set; } = "pending";
    public bool ExcludeFromSae { get; set; }
    public string? ValidationNotes { get; set; }
    /// <summary>URL de donde se extrajo este producto</summary>
    public string? SourceUrl { get; set; }
    public int Attempts { get; set; }

    public DateTime? LastSeenAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    // Navigation
    [JsonIgnore]
    public virtual SiteProfile? Site { get; set; }
}

/// <summary>
/// Mapeo de categorías proveedor → SAE
/// </summary>
public class CategoryMapping
{
    public Guid Id { get; set; }
    public string? SourceCategory { get; set; }
    public string SaeLineCode { get; set; } = string.Empty;
    public string? SaeLineName { get; set; }
    public bool AutoMapped { get; set; }
    public decimal? ConfidenceScore { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Log de sincronización
/// </summary>
public class SyncLog
{
    public Guid Id { get; set; }
    public string OperationType { get; set; } = string.Empty;
    public Guid? SiteId { get; set; }
    public Guid? ProductId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? Details { get; set; } // JSON
    public int? DurationMs { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Reporte de ejecución
/// </summary>
public class ExecutionReport
{
    public Guid Id { get; set; }
    public DateTime ExecutionDate { get; set; }
    public Guid? SiteId { get; set; }
    public int ProductsFound { get; set; }
    public int ProductsNew { get; set; }
    public int ProductsUpdated { get; set; }
    public int ProductsDiscontinued { get; set; }
    public int ProductsError { get; set; }
    public int AITokensUsed { get; set; }
    public int? TotalDurationMs { get; set; }
    public string? Summary { get; set; } // JSON
    public DateTime CreatedAt { get; set; }
}
