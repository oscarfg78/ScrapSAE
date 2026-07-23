using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Interfaces;
using ScrapSAE.Desktop.Infrastructure;
using ScrapSAE.Desktop.Models;

namespace ScrapSAE.Desktop.Services;

public sealed class ApiClient
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    public string BaseUrl { get; }

    public ApiClient(string baseUrl)
    {
        BaseUrl = baseUrl.TrimEnd('/');
        _httpClient = new HttpClient { BaseAddress = new Uri(BaseUrl + "/"), Timeout = TimeSpan.FromMinutes(30) };
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    public Task<List<SiteProfile>> GetSitesAsync() => GetAllAsync<SiteProfile>("api/sites");
    public Task<SiteProfile?> CreateSiteAsync(SiteProfile site) => PostAsync("api/sites", site);
    public Task<SiteProfile?> UpdateSiteAsync(Guid id, SiteProfile site) => PutAsync($"api/sites/{id}", site);
    public Task DeleteSiteAsync(Guid id) => DeleteAsync($"api/sites/{id}");

    /// <summary>
    /// Analiza la URL de un proveedor con IA para detectar la estructura del catálogo de productos.
    /// Puede tomar hasta 30 segundos (descarga HTML con Playwright + análisis GPT).
    /// </summary>
    public async Task<PageAnalysisResult?> AnalyzePageAsync(string url)
    {
        var body = new { url };
        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/sites/analyze", body);
            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                AppLogger.Error($"POST api/sites/analyze failed. Status={(int)response.StatusCode}. Body={content}");
                return null;
            }
            return await response.Content.ReadFromJsonAsync<PageAnalysisResult>(_jsonOptions);
        }
        catch (Exception ex)
        {
            AppLogger.Error("AnalyzePageAsync exception.", ex);
            throw;
        }
    }

    /// <summary>
    /// Elimina manualmente todos los SiteProfile prefijados [TEMP] (limpieza del wizard).
    /// </summary>
    public async Task<int> DeleteTempSitesAsync()
    {
        try
        {
            var response = await _httpClient.DeleteAsync("api/sites/temp");
            if (!response.IsSuccessStatusCode)
            {
                return 0;
            }
            var result = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
            return result.TryGetProperty("deleted", out var deleted) ? deleted.GetInt32() : 0;
        }
        catch (Exception ex)
        {
            AppLogger.Error("DeleteTempSitesAsync exception.", ex);
            return 0;
        }
    }

    public Task<List<StagingProduct>> GetStagingProductsAsync() => GetAllAsync<StagingProduct>("api/staging-products");
    public Task<StagingProduct?> CreateStagingProductAsync(StagingProduct product) => PostAsync("api/staging-products", product);
    public Task<StagingProduct?> UpsertStagingProductAsync(StagingProduct product) => PostAsync("api/staging-products/upsert", product);
    public Task<StagingProduct?> UpdateStagingProductAsync(Guid id, StagingProduct product) => PutAsync($"api/staging-products/{id}", product);
    public Task DeleteStagingProductAsync(Guid id) => DeleteAsync($"api/staging-products/{id}");

    public Task<List<CategoryMapping>> GetCategoryMappingsAsync() => GetAllAsync<CategoryMapping>("api/category-mappings");
    public Task<CategoryMapping?> CreateCategoryMappingAsync(CategoryMapping mapping) => PostAsync("api/category-mappings", mapping);
    public Task<CategoryMapping?> UpdateCategoryMappingAsync(Guid id, CategoryMapping mapping) => PutAsync($"api/category-mappings/{id}", mapping);
    public Task DeleteCategoryMappingAsync(Guid id) => DeleteAsync($"api/category-mappings/{id}");

    public Task<List<SyncLog>> GetSyncLogsAsync() => GetAllAsync<SyncLog>("api/sync-logs");
    public Task<SyncLog?> CreateSyncLogAsync(SyncLog log) => PostAsync("api/sync-logs", log);
    public Task<SyncLog?> UpdateSyncLogAsync(Guid id, SyncLog log) => PutAsync($"api/sync-logs/{id}", log);
    public Task DeleteSyncLogAsync(Guid id) => DeleteAsync($"api/sync-logs/{id}");

    public Task<List<ExecutionReport>> GetExecutionReportsAsync() => GetAllAsync<ExecutionReport>("api/execution-reports");
    public Task<ExecutionReport?> CreateExecutionReportAsync(ExecutionReport report) => PostAsync("api/execution-reports", report);
    public Task<ExecutionReport?> UpdateExecutionReportAsync(Guid id, ExecutionReport report) => PutAsync($"api/execution-reports/{id}", report);
    public Task DeleteExecutionReportAsync(Guid id) => DeleteAsync($"api/execution-reports/{id}");

    public async Task<ScrapeRunResult?> RunScrapingAsync(Guid siteId, bool manualLogin, bool headless, bool keepBrowser = false, bool screenshotFallback = false, string mode = "traditional")
    {
        var query = $"api/scraping/run/{siteId}?manualLogin={manualLogin.ToString().ToLowerInvariant()}&headless={headless.ToString().ToLowerInvariant()}&keepBrowser={keepBrowser.ToString().ToLowerInvariant()}&screenshotFallback={screenshotFallback.ToString().ToLowerInvariant()}&mode={Uri.EscapeDataString(mode)}";
        try
        {
            var response = await _httpClient.PostAsync(query, null);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<ScrapeRunResult>(_jsonOptions);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"RunScrapingAsync failed. Url={query}", ex);
            throw;
        }
    }

    public async Task<InspectUrlsResponse?> InspectUrlsAsync(Guid siteId, List<string> urls)
    {
        var body = new { urls };
        var response = await _httpClient.PostAsJsonAsync($"api/scraping/inspect/{siteId}", body);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<InspectUrlsResponse>(_jsonOptions);
    }

    public async Task<RescrapeJobResponse?> QueueRescrapeAsync(List<Guid> productIds, bool manualLogin = false)
    {
        var body = new RescrapeRequest
        {
            ProductIds = productIds,
            ManualLogin = manualLogin
        };
        var response = await _httpClient.PostAsJsonAsync("api/scraping/rescrape", body);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RescrapeJobResponse>(_jsonOptions);
    }

    public async Task<RescrapeJobStatusResponse?> GetRescrapeStatusAsync(Guid jobId)
    {
        var response = await _httpClient.GetAsync($"api/scraping/rescrape/{jobId}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RescrapeJobStatusResponse>(_jsonOptions);
    }

    public async Task<List<RescrapeJobItemResponse>> GetRescrapeItemsAsync(Guid jobId)
    {
        var response = await _httpClient.GetAsync($"api/scraping/rescrape/{jobId}/items");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<RescrapeJobItemResponse>>(_jsonOptions) ?? new List<RescrapeJobItemResponse>();
    }

    public async Task<List<RescrapeJobLogResponse>> GetRescrapeLogsAsync(Guid jobId, int take = 200)
    {
        var response = await _httpClient.GetAsync($"api/scraping/rescrape/{jobId}/logs?take={take}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<RescrapeJobLogResponse>>(_jsonOptions) ?? new List<RescrapeJobLogResponse>();
    }

    public async Task<bool> CancelRescrapeAsync(Guid jobId)
    {
        var response = await _httpClient.PostAsync($"api/scraping/rescrape/{jobId}/cancel", null);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> PauseRescrapeAsync(Guid jobId)
    {
        var response = await _httpClient.PostAsync($"api/scraping/rescrape/{jobId}/pause", null);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> ResumeRescrapeAsync(Guid jobId)
    {
        var response = await _httpClient.PostAsync($"api/scraping/rescrape/{jobId}/resume", null);
        return response.IsSuccessStatusCode;
    }


    public async Task<ScrapeStatus?> GetScrapeStatusAsync(Guid siteId)
    {
        var response = await _httpClient.GetAsync($"api/scraping/status/{siteId}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ScrapeStatus>(_jsonOptions);
    }

    public async Task PauseScrapingAsync(Guid siteId)
    {
        var response = await _httpClient.PostAsync($"api/scraping/pause/{siteId}", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task ResumeScrapingAsync(Guid siteId)
    {
        var response = await _httpClient.PostAsync($"api/scraping/resume/{siteId}", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task StopScrapingAsync(Guid siteId)
    {
        var response = await _httpClient.PostAsync($"api/scraping/stop/{siteId}", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task<SelectorSuggestion?> AnalyzeSelectorsAsync(SelectorAnalysisRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync("api/ai/analyze-selectors", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SelectorSuggestion>(_jsonOptions);
    }

    public async Task<ApiOperationResult> SendToSaeAsync(Guid productId)
    {
        var path = $"api/sae/send/{productId}";
        try
        {
            var response = await _httpClient.PostAsync(path, null);
            if (response.IsSuccessStatusCode)
            {
                return new ApiOperationResult { Success = true };
            }

            var message = await ExtractErrorMessageAsync(response);
            AppLogger.Error($"POST {path} failed. Status={(int)response.StatusCode}. Message={message}");
            return new ApiOperationResult
            {
                Success = false,
                StatusCode = (int)response.StatusCode,
                Message = message
            };
        }
        catch (Exception ex)
        {
            AppLogger.Error($"POST {path} exception.", ex);
            return new ApiOperationResult
            {
                Success = false,
                Message = ex.Message
            };
        }
    }

    public async Task<SaeSendSummary?> SendPendingToSaeAsync()
    {
        var response = await _httpClient.PostAsync("api/sae/send-pending", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SaeSendSummary>(_jsonOptions);
    }

    public async Task<OnlineStoreSendSummary?> SendPendingToOnlineStoreAsync()
    {
        var response = await _httpClient.PostAsync("api/online-store/send-pending", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OnlineStoreSendSummary>(_jsonOptions);
    }

    public async Task<ApiOperationResult> SendToOnlineStoreAsync(Guid productId)
    {
        var path = $"api/online-store/send/{productId}";
        try
        {
            var response = await _httpClient.PostAsync(path, null);
            if (response.IsSuccessStatusCode)
            {
                return new ApiOperationResult { Success = true };
            }

            var message = await ExtractErrorMessageAsync(response);
            AppLogger.Error($"POST {path} failed. Status={(int)response.StatusCode}. Message={message}");
            return new ApiOperationResult
            {
                Success = false,
                StatusCode = (int)response.StatusCode,
                Message = message
            };
        }
        catch (Exception ex)
        {
            AppLogger.Error($"POST {path} exception.", ex);
            return new ApiOperationResult
            {
                Success = false,
                Message = ex.Message
            };
        }
    }

    public async Task<AppSettingsDto?> GetSettingsAsync()
    {
        var response = await _httpClient.GetAsync("api/settings");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AppSettingsDto>(_jsonOptions);
    }

    public async Task<AppSettingsDto?> SaveSettingsAsync(AppSettingsDto settings)
    {
        var response = await _httpClient.PostAsJsonAsync("api/settings", settings);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AppSettingsDto>(_jsonOptions);
    }

    public string? GetSyncLogScreenshotUrl(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        return $"{BaseUrl}/api/sync-logs/screenshot/{Uri.EscapeDataString(fileName)}";
    }

    public async Task<DiagnosticsResult?> GetDiagnosticsAsync()
    {
        var response = await _httpClient.GetAsync("api/diagnostics");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DiagnosticsResult>(_jsonOptions);
    }

    public async Task<bool> TestBackendAsync()
    {
        var response = await _httpClient.GetAsync("api/health");
        return response.IsSuccessStatusCode;
    }

    public async Task<LearnedPatterns?> GetLearnedPatternsAsync(Guid siteId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"api/scraping/patterns/{siteId}");
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<LearnedPatterns>(_jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public async Task LearnUrlsAsync(Guid siteId, List<string> urls)
    {
        var body = new
        {
            urls = urls.Select(u => new { url = u, type = u.Contains("/a/") || u.Contains("/p/") ? "ProductDetail" : "ProductListing" })
        };
        var response = await _httpClient.PostAsJsonAsync($"api/scraping/learn/{siteId}", body);
        response.EnsureSuccessStatusCode();
    }

    public async Task ConfirmLoginAsync(Guid siteId)
    {
        var response = await _httpClient.PostAsync($"api/scraping/session/confirm/{siteId}", null);
        response.EnsureSuccessStatusCode();
    }


    private async Task<List<T>> GetAllAsync<T>(string path)
    {
        try
        {
            var response = await _httpClient.GetAsync(path);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                AppLogger.Error($"GET {path} failed. Status={(int)response.StatusCode}. Body={body}");
            }
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<T>>(_jsonOptions) ?? new List<T>();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"GET {path} exception.", ex);
            throw;
        }
    }

    private async Task<T?> PostAsync<T>(string path, T body)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(path, body);
            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                AppLogger.Error($"POST {path} failed. Status={(int)response.StatusCode}. Body={content}");
            }
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<T>(_jsonOptions);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"POST {path} exception.", ex);
            throw;
        }
    }

    private async Task<T?> PutAsync<T>(string path, T body)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync(path, body);
            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                AppLogger.Error($"PUT {path} failed. Status={(int)response.StatusCode}. Body={content}");
            }
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<T>(_jsonOptions);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"PUT {path} exception.", ex);
            throw;
        }
    }

    private async Task DeleteAsync(string path)
    {
        try
        {
            var response = await _httpClient.DeleteAsync(path);
            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                AppLogger.Error($"DELETE {path} failed. Status={(int)response.StatusCode}. Body={content}");
            }
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"DELETE {path} exception.", ex);
            throw;
        }
    }

    private async Task<string> ExtractErrorMessageAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body))
        {
            return $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var lines = new List<string>();

            if (TryGetJsonString(root, "message", out var message)) lines.Add(message);
            if (TryGetJsonString(root, "detail", out var detail)) lines.Add(detail);
            if (TryGetJsonString(root, "title", out var title)) lines.Add(title);
            if (TryGetJsonString(root, "endpoint", out var endpoint)) lines.Add($"endpoint: {endpoint}");
            if (TryGetJsonString(root, "payload", out var payload)) lines.Add($"payload: {payload}");
            if (TryGetJsonString(root, "upstreamResponseBody", out var upstreamBody)) lines.Add($"upstream_response: {upstreamBody}");
            if (TryGetJsonString(root, "upstream_status", out var upstreamStatusText)) lines.Add($"upstream_status: {upstreamStatusText}");
            if (root.TryGetProperty("upstreamStatusCode", out var upstreamStatusCode))
            {
                lines.Add($"upstream_status: {upstreamStatusCode}");
            }

            if (lines.Count > 0)
            {
                return string.Join(Environment.NewLine, lines);
            }
        }
        catch
        {
            // Keep original body if it is not JSON.
        }

        return body;
    }

    private static bool TryGetJsonString(JsonElement root, string property, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(property, out var element))
        {
            return false;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var parsed = element.GetString();
        if (string.IsNullOrWhiteSpace(parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }
}
