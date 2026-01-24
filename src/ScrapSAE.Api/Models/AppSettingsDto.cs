namespace ScrapSAE.Api.Models;

public sealed class AppSettingsDto
{
    public string? SupabaseUrl { get; set; }
    public string? SupabaseServiceKey { get; set; }
    public string? SaeSdkPath { get; set; }
    public string? SaeUser { get; set; }
    public string? SaePassword { get; set; }
}
