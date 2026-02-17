namespace ScrapSAE.Desktop.Models;

public sealed class ApiOperationResult
{
    public bool Success { get; init; }
    public int? StatusCode { get; init; }
    public string? Message { get; init; }
}
