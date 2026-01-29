namespace ScrapSAE.Api.Models;

/// <summary>
/// Request para scraping directo de URLs específicas
/// </summary>
public class DirectUrlsRequest
{
    /// <summary>Lista de URLs a inspeccionar/scrapear directamente</summary>
    public List<string> Urls { get; set; } = new();
    
    /// <summary>Si es true, solo inspecciona sin guardar productos</summary>
    public bool InspectOnly { get; set; } = false;
    
    /// <summary>Forzar login manual antes de comenzar</summary>
    public bool ManualLogin { get; set; } = false;
    
    /// <summary>Ejecutar en modo headless (sin ventana visible)</summary>
    public bool Headless { get; set; } = true;
}


