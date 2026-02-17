namespace ScrapSAE.Desktop.Models;

public sealed class OnlineStoreSendSummary
{
    public int Total { get; set; }
    public int Sent { get; set; }
    public int Failed { get; set; }
    public string? Message { get; set; }
    public List<OnlineStoreSendItem> Results { get; set; } = new();
}

public sealed class OnlineStoreSendItem
{
    public Guid ProductId { get; set; }
    public string? SourceSku { get; set; }
    public bool Success { get; set; }
    public string? Message { get; set; }
}
