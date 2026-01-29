namespace ScrapSAE.Api.Models;

/// <summary>
/// Request para aprender de URLs de ejemplo
/// </summary>
public class LearnUrlsRequest
{
    public List<LearnUrlItem> Urls { get; set; } = new();
}

public class LearnUrlItem
{
    public string Url { get; set; } = string.Empty;
    /// <summary>
    /// Tipo de URL: "ProductDetail", "ProductListing", "Subcategory", "SearchResults"
    /// </summary>
    public string Type { get; set; } = "ProductDetail";
}
