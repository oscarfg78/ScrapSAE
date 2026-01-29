namespace ScrapSAE.Core.DTOs;

/// <summary>
/// Paquete de diagnóstico que encapsula toda la información de un fallo para su posterior análisis
/// </summary>
public class DiagnosticPackage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExecutionId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Url { get; set; } = string.Empty;
    public string SelectorAttempted { get; set; } = string.Empty;
    public string FailureType { get; set; } = string.Empty; // "ElementNotFound", "Timeout", "NavigationError"
    public string? HtmlSnapshot { get; set; } // Fragmento de HTML del área de fallo
    public string? ScreenshotPath { get; set; } // Ruta al archivo de captura de pantalla
    public List<ElementAnnotation> Annotations { get; set; } = new();
    public List<string> BrowserLogs { get; set; } = new();
}

/// <summary>
/// Anotación de un elemento en la captura de pantalla
/// </summary>
public class ElementAnnotation
{
    public string Selector { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // "Found", "NotFound"
    public BoundingBox? BoundingBox { get; set; } // Coordenadas si se encontró
}

/// <summary>
/// Coordenadas de un elemento en la página
/// </summary>
public class BoundingBox
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}
