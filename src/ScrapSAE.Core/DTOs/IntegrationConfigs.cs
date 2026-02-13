namespace ScrapSAE.Core.DTOs;

public class FlashlyApiConfig
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}

public class SyncOptionsConfig
{
    public string TargetSystem { get; set; } = "Flashly";
    public bool AutoSync { get; set; } = true;
    public int BatchSize { get; set; } = 50;
    public int RetryAttempts { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 5;
}

public class CsvExportConfig
{
    public string OutputDirectory { get; set; } = "exports";
    public string FileNamePattern { get; set; } = "products_export_{0:yyyyMMdd_HHmmss}.csv";
}
