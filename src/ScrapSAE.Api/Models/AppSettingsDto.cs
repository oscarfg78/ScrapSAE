namespace ScrapSAE.Api.Models;

public sealed class AppSettingsDto
{
    public string? SupabaseUrl { get; set; }
    public string? SupabaseServiceKey { get; set; }
    public string? SaeSdkPath { get; set; }
    public string? SaeUser { get; set; }
    public string? SaePassword { get; set; }
    public string? SaeDbHost { get; set; }
    public string? SaeDbPath { get; set; }
    public string? SaeDbUser { get; set; }
    public string? SaeDbPassword { get; set; }
    public int? SaeDbPort { get; set; }
    public string? SaeDbCharset { get; set; }
    public int? SaeDbDialect { get; set; }
    public string? SaeDefaultLineCode { get; set; }
}
