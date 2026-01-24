namespace ScrapSAE.Desktop.Models;

public sealed class DiagnosticsResult
{
    public bool BackendOk { get; set; }
    public bool SupabaseOk { get; set; }
    public string? SupabaseMessage { get; set; }
    public int? SupabaseSampleCount { get; set; }
    public bool SaeSdkOk { get; set; }
    public string? SaeMessage { get; set; }
    public DateTime CheckedAtUtc { get; set; }
}
