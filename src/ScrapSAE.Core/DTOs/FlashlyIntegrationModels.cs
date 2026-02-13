namespace ScrapSAE.Core.DTOs;

public class FlashlyProductSyncDto
{
    public string SourceSku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal PurchasePrice { get; set; }
    public string Currency { get; set; } = "MXN";
    public List<string> Categories { get; set; } = new();
    public string? ProductUrl { get; set; }
    public List<string> ImageUrls { get; set; } = new();
    public string? SupplierName { get; set; }
    public string? SpecificationsJson { get; set; }
}

public class FlashlySyncResponse
{
    public string? Status { get; set; }
    public string? Message { get; set; }
    public FlashlySyncResponseResults? Results { get; set; }
    public string? JobId { get; set; }
}

public class FlashlySyncResponseResults
{
    public int Created { get; set; }
    public int Updated { get; set; }
    public List<FlashlySyncError> Errors { get; set; } = new();
}

public class FlashlySyncError
{
    public string SourceSku { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
}

public class FlashlySyncResult
{
    public bool Success { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public List<FlashlySyncError> Errors { get; set; } = new();
    public string? Message { get; set; }
    public string? JobId { get; set; }
}
