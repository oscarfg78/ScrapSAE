namespace ScrapSAE.Core.DTOs;

// ─────────────────────────────────────────────────────────────────────────────
// Concurrent Scraping Wizard — Core Data Models
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Registro de una fila del Excel cargado por el usuario.
/// Contiene los campos obligatorios (SKU, CostoProveedor) y atributos opcionales.
/// <summary>
/// Fila de producto extraída del Excel.
/// Contiene obligatoriamente SKU y CostoProveedor.
/// </summary>
public class ExcelProductRecord
{
    public int RowIndex { get; set; }
    public string Sku { get; set; } = string.Empty;
    public decimal CostoProveedor { get; set; }
    /// <summary>Porcentaje opcional de margen bruto extraído del Excel (0-100).</summary>
    public decimal? MarginPercentage { get; set; }
    /// <summary>Atributos opcionales mapeados (url detalle, tamaño, imágenes, etc.)</summary>
    public Dictionary<string, string> OptionalAttributes { get; set; } = new();
}

/// <summary>Mapa de columnas seleccionadas por el usuario en Step 1.</summary>
public class ExcelColumnMapping
{
    /// <summary>Índice de columna base 0 para SKU.</summary>
    public int SkuColumnIndex { get; set; }
    /// <summary>Índice de columna base 0 para Costo del Proveedor.</summary>
    public int CostoColumnIndex { get; set; }
    /// <summary>Índice opcional de columna base 0 para Porcentaje de Margen de Ganancia (0-100).</summary>
    public int? MarginColumnIndex { get; set; }
    /// <summary>Índice opcional de columna base 0 para Categoría.</summary>
    public int? CategoryColumnIndex { get; set; }
    /// <summary>Columnas opcionales: key = nombre semántico, value = índice de columna.</summary>
    public Dictionary<string, int> OptionalColumns { get; set; } = new();
}

/// <summary>Resultado del preview de columnas del archivo Excel.</summary>
public class ExcelPreviewResult
{
    public string[] ColumnHeaders { get; set; } = [];
    public List<string[]> PreviewRows { get; set; } = new();
    public int TotalRowCount { get; set; }
    public string? ErrorMessage { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Selectors & Target Configuration
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Modo de búsqueda para una URL objetivo.</summary>
public enum SearchMode
{
    /// <summary>Escribe el SKU en un campo de búsqueda DOM y hace submit.</summary>
    DomInput,
    /// <summary>Construye la URL con el SKU como parámetro query (ej: ?q={sku}).</summary>
    QueryParam,
    /// <summary>Navega directamente a la URL del detalle del producto (ej: /model/{sku}).</summary>
    DirectDetail
}

/// <summary>Selectores CSS/XPath requeridos para interactuar con una página objetivo.</summary>
public class SelectorConfig
{
    public string? SearchInputSelector { get; set; }
    public string? SearchSubmitSelector { get; set; }

    /// <summary>Indica si se requiere validar/hacer click en la primera tarjeta de resultados.</summary>
    public bool RequireFirstResultCard { get; set; } = false;
    public string? FirstResultCardSelector { get; set; }
    public string? DetailLinkSelector { get; set; }

    /// <summary>Indica si el precio de venta debe extraerse del sitio web o calcularse por margen.</summary>
    public bool ExtractRetailPriceFromWeb { get; set; } = true;
    public string? RetailPriceSelector { get; set; }

    /// <summary>Porcentaje fijo opcional de margen bruto de ganancia (0-100%) cuando no se extrae de la web.</summary>
    public decimal? DefaultMarginPercentage { get; set; }
    /// <summary>Indica si se debe intentar leer el % de margen desde la columna del Excel.</summary>
    public bool UseExcelMarginColumn { get; set; } = false;

    public string? ImageGallerySelector { get; set; }
    public string? TitleSelector { get; set; }

    /// <summary>Selector para la Descripción (extrae viñetas o texto y une por comas).</summary>
    public string? DescriptionSelector { get; set; }

    /// <summary>Selector para Atributos / Características (extrae tabla o lista clave:valor y guarda en JSON).</summary>
    public string? AttributesSelector { get; set; }

    /// <summary>Selector para Categoría (extrae texto de breadcrumbs o clasificaciones).</summary>
    public string? CategorySelector { get; set; }

    /// <summary>Indica si se debe guardar la URL origen de la página en las características/atributos del producto.</summary>
    public bool IncludeSourceUrlInAttributes { get; set; } = true;

    public bool IsValid(SearchMode mode = SearchMode.QueryParam)
    {
        if (mode == SearchMode.DirectDetail)
        {
            return (ExtractRetailPriceFromWeb && !string.IsNullOrWhiteSpace(RetailPriceSelector)) ||
                   !string.IsNullOrWhiteSpace(ImageGallerySelector) ||
                   !string.IsNullOrWhiteSpace(DescriptionSelector) ||
                   !string.IsNullOrWhiteSpace(AttributesSelector) ||
                   !string.IsNullOrWhiteSpace(TitleSelector);
        }

        if (mode == SearchMode.DomInput && string.IsNullOrWhiteSpace(SearchInputSelector))
            return false;

        if (RequireFirstResultCard && string.IsNullOrWhiteSpace(FirstResultCardSelector))
            return false;

        // Se requiere al menos el link de detalle o el título si no es DirectDetail
        if (string.IsNullOrWhiteSpace(DetailLinkSelector) && string.IsNullOrWhiteSpace(TitleSelector))
            return false;

        if (ExtractRetailPriceFromWeb && string.IsNullOrWhiteSpace(RetailPriceSelector))
            return false;

        return true;
    }
}

/// <summary>Configuración completa de una fuente objetivo de búsqueda.</summary>
public class TargetSearchConfig
{
    public string Label { get; set; } = string.Empty;         // "Target 1" / "Target 2"
    public string BaseSearchUrl { get; set; } = string.Empty;
    public SearchMode SearchMode { get; set; } = SearchMode.QueryParam;
    /// <summary>Plantilla con {sku} para QueryParam mode. Ej: https://site.com/search?q={sku}</summary>
    public string SearchUrlTemplate { get; set; } = string.Empty;
    public SelectorConfig Selectors { get; set; } = new();
    /// <summary>Delay en ms entre requests para evitar bloqueos anti-bot.</summary>
    public int RequestDelayMs { get; set; } = 1500;
}

// ─────────────────────────────────────────────────────────────────────────────
// Scraping Results
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Estado del resultado de scraping de un target para un SKU.</summary>
public enum ScrapingResultStatus
{
    Found,
    NotFound
}

/// <summary>Motivo por el que no se encontró el registro.</summary>
public enum SkipReason
{
    None,
    SearchPageLoadFailed,
    SearchInputNotFound,
    SearchSubmitFailed,
    NoSearchResults,
    DetailLinkNotFound,
    DetailNavigationFailed,
    ExtractionTimeout,
    UnexpectedException
}

/// <summary>
/// Resultado del scraping de un target para un SKU específico.
/// En caso de fallo el Status es NotFound y se preserva el FailureReason.
/// </summary>
public class TargetScrapeResult
{
    public string TargetLabel { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public ScrapingResultStatus Status { get; set; } = ScrapingResultStatus.NotFound;
    public SkipReason FailureReason { get; set; } = SkipReason.None;
    public string? FailureMessage { get; set; }

    /// <summary>Precio de venta extraído del DOM o calculado por margen (null si no encontrado).</summary>
    public decimal? RetailPrice { get; set; }
    public List<string> ImageUrls { get; set; } = new();
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? SourceDetailUrl { get; set; }
    public Dictionary<string, string> OptionalAttributes { get; set; } = new();
    public string? WarningMessage { get; set; }

    public static TargetScrapeResult NotFound(string label, string sku, SkipReason reason, string? message = null) =>
        new() { TargetLabel = label, Sku = sku, Status = ScrapingResultStatus.NotFound, FailureReason = reason, FailureMessage = message };
}

// ─────────────────────────────────────────────────────────────────────────────
// Source Priority & Consolidation
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Identifica cuál target es fuente de un dato.</summary>
public enum DataSource
{
    Target1,
    Target2
}

/// <summary>Configura de cuál fuente tomar precio e imágenes.</summary>
public class SourcePriorityConfig
{
    public DataSource PriceSource { get; set; } = DataSource.Target1;
    public DataSource ImageSource { get; set; } = DataSource.Target1;
}

/// <summary>Estado del resultado consolidado por SKU.</summary>
public enum ConsolidatedStatus
{
    Matched,
    NotMatched
}

/// <summary>
/// Producto consolidado: une el costo del proveedor (Excel) con precio público e imágenes (web).
/// Los registros NotMatched se incluyen siempre en el output para auditoria completa.
/// </summary>
public class ConsolidatedProductResult
{
    public int RowIndex { get; set; }
    public string Sku { get; set; } = string.Empty;
    /// <summary>Siempre presente desde el Excel original.</summary>
    public decimal SupplierCost { get; set; }
    /// <summary>Precio de venta extraído de la fuente de precio configurada.</summary>
    public decimal? RetailPrice { get; set; }
    public List<string> ImageUrls { get; set; } = new();
    public string? Title { get; set; }
    public string? Description { get; set; }
    public List<string> SourceDetailUrls { get; set; } = new();
    public ConsolidatedStatus Status { get; set; } = ConsolidatedStatus.NotMatched;
    public DateTime ScrapedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, string> OptionalAttributes { get; set; } = new();
    public string? WarningMessage { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Wizard Session
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Estado completo de la sesión del Concurrent Scraping Wizard.
/// Serializado a JSON en %AppData%/ScrapSAE/sessions/{sessionId}.json.
/// </summary>
public class ConcurrentWizardSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSavedAt { get; set; } = DateTime.UtcNow;

    // Step 1 state
    public string ExcelFilePath { get; set; } = string.Empty;
    public ExcelColumnMapping ColumnMapping { get; set; } = new();
    
    // Database mapping
    public Guid? TargetSiteId { get; set; }
    public string? TargetSiteName { get; set; }
    public int TotalExcelRows { get; set; }

    // Step 2 state
    public TargetSearchConfig Target1 { get; set; } = new() { Label = "Target 1" };
    public TargetSearchConfig? Target2 { get; set; }

    // Step 3 state
    public SourcePriorityConfig SourcePriority { get; set; } = new();

    // Execution state
    public int LastCompletedRowIndex { get; set; } = -1;
    public int WorkerCount { get; set; } = 4;
    public int MaxConcurrentPages { get; set; } = 8;

    public bool HasTarget2 => Target2 != null && !string.IsNullOrWhiteSpace(Target2.BaseSearchUrl);
}

// ─────────────────────────────────────────────────────────────────────────────
// Progress Events
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Tipos de eventos emitidos por el engine al progress stream.</summary>
public enum ProgressEventType
{
    RowStarted,
    RowCompleted,
    RowSkipped,
    ExecutionPaused,
    ExecutionResumed,
    ExecutionStopped,
    ExecutionFinished
}

/// <summary>
/// Evento de progreso emitido por IConcurrentScrapingEngine.Progress.
/// El ViewModel lo recibe en el hilo UI via ObserveOn(Dispatcher).
/// </summary>
public class ScrapingProgressEvent
{
    public ProgressEventType EventType { get; set; }
    public int RowIndex { get; set; }
    public string? Sku { get; set; }
    public ConsolidatedProductResult? Result { get; set; }
    public int ProcessedCount { get; set; }
    public int SuccessCount { get; set; }
    public int SkippedCount { get; set; }
    public int TotalRows { get; set; }
    public double ProgressPercent => TotalRows > 0 ? (double)ProcessedCount / TotalRows * 100 : 0;
    public long ElapsedMs { get; set; }
    public string? Message { get; set; }
}
