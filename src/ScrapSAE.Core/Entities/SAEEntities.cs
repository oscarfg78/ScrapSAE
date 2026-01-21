namespace ScrapSAE.Core.Entities;

/// <summary>
/// Producto de Aspel SAE (tabla INVE01)
/// </summary>
public class ProductSAE
{
    public string CVE_ART { get; set; } = string.Empty;  // SKU/Clave del artículo
    public string? DESCR { get; set; }                   // Descripción
    public string? LIN_PROD { get; set; }                // Línea de producto
    public decimal? EXIST { get; set; }                  // Existencia
    public decimal? PREC_X_MAY { get; set; }             // Precio mayoreo
    public decimal? PREC_X_MEN { get; set; }             // Precio menudeo
    public decimal? ULT_COSTO { get; set; }              // Último costo
    public string? CTRL_ALM { get; set; }                // Control almacén
    public string? STATUS { get; set; }                  // Estado
    public DateTime? FCH_ULTCOM { get; set; }            // Fecha última compra
    public DateTime? FCH_ULTVTA { get; set; }            // Fecha última venta
    
    // Campos libres para información adicional
    public string? CAMPO_LIBRE1 { get; set; }
    public string? CAMPO_LIBRE2 { get; set; }
    public string? CAMPO_LIBRE3 { get; set; }
}

/// <summary>
/// DTO para actualizar un producto en SAE
/// </summary>
public class ProductUpdate
{
    public string CVE_ART { get; set; } = string.Empty;
    public string? DESCR { get; set; }
    public decimal? EXIST { get; set; }
    public decimal? PREC_X_MAY { get; set; }
    public decimal? PREC_X_MEN { get; set; }
}

/// <summary>
/// DTO para crear un nuevo producto en SAE
/// </summary>
public class ProductCreate
{
    public string CVE_ART { get; set; } = string.Empty;
    public string DESCR { get; set; } = string.Empty;
    public string LIN_PROD { get; set; } = string.Empty;
    public decimal EXIST { get; set; }
    public decimal PREC_X_MAY { get; set; }
    public decimal PREC_X_MEN { get; set; }
    public decimal ULT_COSTO { get; set; }
}

/// <summary>
/// Línea de producto de SAE (tabla CLIN01)
/// </summary>
public class ProductLine
{
    public string CVE_LIN { get; set; } = string.Empty;
    public string? DESC_LIN { get; set; }
}
