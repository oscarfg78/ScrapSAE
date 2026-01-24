namespace ScrapSAE.Api.Models;

public sealed class ScrapeRunResult
{
    public Guid SiteId { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public int ProductsFound { get; set; }
    public int ProductsCreated { get; set; }
    public int ProductsUpdated { get; set; }
    public int ProductsSkipped { get; set; }
    public int DurationMs { get; set; }
}
