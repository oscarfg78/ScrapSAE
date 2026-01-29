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

    public async Task<List<DirectUrlResult>?> InspectUrlsAsync(Guid siteId, List<string> urls)
    {
        var body = new { urls };
        var response = await _httpClient.PostAsJsonAsync($"api/scraping/inspect/{siteId}", body);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<DirectUrlResult>>(_jsonOptions);
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

    public async Task<bool> SendToSaeAsync(Guid productId)
    {
        var response = await _httpClient.PostAsync($"api/sae/send/{productId}", null);
        return response.IsSuccessStatusCode;
    }

    public async Task<SaeSendSummary?> SendPendingToSaeAsync()
    {
        var response = await _httpClient.PostAsync("api/sae/send-pending", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SaeSendSummary>(_jsonOptions);
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
}
