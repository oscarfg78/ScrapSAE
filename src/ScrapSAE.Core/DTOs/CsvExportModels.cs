namespace ScrapSAE.Core.DTOs;

public class ProductCsvRecord
{
    public string SourceSku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal PurchasePrice { get; set; }
    public string Currency { get; set; } = "MXN";
    public string Categories { get; set; } = string.Empty;
    public string? ProductUrl { get; set; }
    public string ImageUrls { get; set; } = string.Empty;
    public string? SupplierName { get; set; }
    public string? SpecificationsJson { get; set; }
}
