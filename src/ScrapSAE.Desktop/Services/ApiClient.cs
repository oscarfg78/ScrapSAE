using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using ScrapSAE.Core.Entities;
using ScrapSAE.Desktop.Models;

namespace ScrapSAE.Desktop.Services;

public sealed class ApiClient
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    public ApiClient(string baseUrl)
    {
        _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
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

    public async Task<ScrapeRunResult?> RunScrapingAsync(Guid siteId)
    {
        var response = await _httpClient.PostAsync($"api/scraping/run/{siteId}", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ScrapeRunResult>(_jsonOptions);
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

    private async Task<List<T>> GetAllAsync<T>(string path)
    {
        var response = await _httpClient.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<T>>(_jsonOptions) ?? new List<T>();
    }

    private async Task<T?> PostAsync<T>(string path, T body)
    {
        var response = await _httpClient.PostAsJsonAsync(path, body);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(_jsonOptions);
    }

    private async Task<T?> PutAsync<T>(string path, T body)
    {
        var response = await _httpClient.PutAsJsonAsync(path, body);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(_jsonOptions);
    }

    private async Task DeleteAsync(string path)
    {
        var response = await _httpClient.DeleteAsync(path);
        response.EnsureSuccessStatusCode();
    }
}
