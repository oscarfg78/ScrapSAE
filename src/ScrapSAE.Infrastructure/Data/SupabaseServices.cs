using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;
using System.Linq;
using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Interfaces;
using Newtonsoft.Json;

namespace ScrapSAE.Infrastructure.Data;

/// <summary>
/// Servicio de staging usando Supabase REST API
/// </summary>
public class SupabaseStagingService : IStagingService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SupabaseStagingService> _logger;
    private readonly string _baseUrl;
    private readonly JsonSerializerOptions _jsonOptions;

    public SupabaseStagingService(IConfiguration configuration, ILogger<SupabaseStagingService> logger)
    {
        _logger = logger;
        
        var url = configuration["Supabase:Url"] 
            ?? throw new ArgumentNullException("Supabase:Url not configured");
        var key = configuration["Supabase:ServiceKey"] 
            ?? throw new ArgumentNullException("Supabase:ServiceKey not configured");

        _baseUrl = url.TrimEnd('/');
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("apikey", key);
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {key}");
        _httpClient.DefaultRequestHeaders.Add("Prefer", "return=representation");
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };
    }

    public async Task<StagingProduct> CreateProductAsync(StagingProduct product)
    {
        try
        {
            if (product.Id == Guid.Empty) product.Id = Guid.NewGuid();
            product.CreatedAt = DateTime.UtcNow;
            product.UpdatedAt = DateTime.UtcNow;
            
            var response = await _httpClient.PostAsJsonAsync(
                $"{_baseUrl}/rest/v1/staging_products", 
                product,
                _jsonOptions);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("Error creating staging product. Status: {Status}. Response: {Response}",
                    response.StatusCode, error);
                response.EnsureSuccessStatusCode();
            }
            
            var result = await response.Content.ReadFromJsonAsync<StagingProduct[]>(_jsonOptions);
            return result?.FirstOrDefault() ?? product;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating staging product");
            throw;
        }
    }

    public async Task<StagingProduct> UpsertProductAsync(StagingProduct product)
    {
        var existing = await GetProductBySourceSkuAsync(product.SiteId, product.SkuSource ?? "");
        if (existing != null)
        {
            try
            {
                var update = new 
                { 
                    raw_data = product.RawData, 
                    ai_processed_json = product.AIProcessedJson, 
                    source_url = product.SourceUrl,
                    status = product.Status,
                    updated_at = DateTime.UtcNow 
                };
                
                var response = await _httpClient.PatchAsJsonAsync(
                    $"{_baseUrl}/rest/v1/staging_products?id=eq.{existing.Id}", 
                    update);
                
                response.EnsureSuccessStatusCode();
                
                existing.RawData = product.RawData;
                existing.AIProcessedJson = product.AIProcessedJson;
                existing.SourceUrl = product.SourceUrl;
                existing.Status = product.Status;
                existing.UpdatedAt = DateTime.UtcNow;
                
                return existing;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error upserting (updating) staging product {Id}", existing.Id);
                throw;
            }
        }

        return await CreateProductAsync(product);
    }

    public async Task<StagingProduct?> GetProductBySourceSkuAsync(Guid siteId, string skuSource)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"{_baseUrl}/rest/v1/staging_products?site_id=eq.{siteId}&sku_source=eq.{skuSource}");
            
            response.EnsureSuccessStatusCode();
            
            var products = await response.Content.ReadFromJsonAsync<StagingProduct[]>(_jsonOptions);
            return products?.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting product by source SKU {Sku}", skuSource);
            return null;
        }
    }

    public async Task<IEnumerable<StagingProduct>> GetPendingProductsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"{_baseUrl}/rest/v1/staging_products?status=eq.pending");
            
            response.EnsureSuccessStatusCode();
            
            var products = await response.Content.ReadFromJsonAsync<StagingProduct[]>(_jsonOptions);
            return products ?? Array.Empty<StagingProduct>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pending products");
            throw;
        }
    }

    public async Task UpdateProductStatusAsync(Guid id, string status, string? notes = null)
    {
        try
        {
            var update = new { status, validation_notes = notes, updated_at = DateTime.UtcNow };
            
            var response = await _httpClient.PatchAsJsonAsync(
                $"{_baseUrl}/rest/v1/staging_products?id=eq.{id}", 
                update);
            
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating product status for {Id}", id);
            throw;
        }
    }

    public async Task UpdateProductDataAsync(Guid id, string aiProcessedJson)
    {
        try
        {
            var update = new { ai_processed_json = aiProcessedJson, updated_at = DateTime.UtcNow };
            
            var response = await _httpClient.PatchAsJsonAsync(
                $"{_baseUrl}/rest/v1/staging_products?id=eq.{id}", 
                update);
            
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating product data for {Id}", id);
            throw;
        }
    }

    public async Task<IEnumerable<SiteProfile>> GetActiveSitesAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"{_baseUrl}/rest/v1/config_sites?is_active=eq.true");
            
            response.EnsureSuccessStatusCode();
            
            var sites = await response.Content.ReadFromJsonAsync<SiteProfile[]>(_jsonOptions);
            return sites ?? Array.Empty<SiteProfile>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active sites");
            throw;
        }
    }
}

/// <summary>
/// Servicio de logs usando Supabase REST API
/// </summary>
public class SupabaseSyncLogService : ISyncLogService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SupabaseSyncLogService> _logger;
    private readonly string _baseUrl;
    private readonly JsonSerializerOptions _jsonOptions;

    public SupabaseSyncLogService(IConfiguration configuration, ILogger<SupabaseSyncLogService> logger)
    {
        _logger = logger;
        
        var url = configuration["Supabase:Url"]!;
        var key = configuration["Supabase:ServiceKey"]!;

        _baseUrl = url.TrimEnd('/');
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("apikey", key);
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {key}");
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };
    }

    public async Task LogOperationAsync(SyncLog log)
    {
        try
        {
            log.Id = Guid.NewGuid();
            log.CreatedAt = DateTime.UtcNow;
            
            var response = await _httpClient.PostAsJsonAsync(
                $"{_baseUrl}/rest/v1/sync_logs", 
                log);
            
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging operation");
            throw;
        }
    }

    public async Task<IEnumerable<SyncLog>> GetLogsAsync(DateTime from, DateTime to)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"{_baseUrl}/rest/v1/sync_logs?created_at=gte.{from:O}&created_at=lte.{to:O}&order=created_at.desc");
            
            response.EnsureSuccessStatusCode();
            
            var logs = await response.Content.ReadFromJsonAsync<SyncLog[]>(_jsonOptions);
            return logs ?? Array.Empty<SyncLog>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting logs");
            throw;
        }
    }
}

/// <summary>
/// Servicio de reportes usando Supabase REST API
/// </summary>
public class SupabaseExecutionReportService : IExecutionReportService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SupabaseExecutionReportService> _logger;
    private readonly string _baseUrl;
    private readonly JsonSerializerOptions _jsonOptions;

    public SupabaseExecutionReportService(IConfiguration configuration, ILogger<SupabaseExecutionReportService> logger)
    {
        _logger = logger;
        
        var url = configuration["Supabase:Url"]!;
        var key = configuration["Supabase:ServiceKey"]!;

        _baseUrl = url.TrimEnd('/');
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("apikey", key);
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {key}");
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };
    }

    public async Task CreateReportAsync(ExecutionReport report)
    {
        try
        {
            report.Id = Guid.NewGuid();
            report.CreatedAt = DateTime.UtcNow;
            
            var response = await _httpClient.PostAsJsonAsync(
                $"{_baseUrl}/rest/v1/execution_reports", 
                report);
            
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating execution report");
            throw;
        }
    }

    public async Task<IEnumerable<ExecutionReport>> GetReportsAsync(DateTime from, DateTime to)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"{_baseUrl}/rest/v1/execution_reports?execution_date=gte.{from:yyyy-MM-dd}&execution_date=lte.{to:yyyy-MM-dd}&order=execution_date.desc");
            
            response.EnsureSuccessStatusCode();
            
            var reports = await response.Content.ReadFromJsonAsync<ExecutionReport[]>(_jsonOptions);
            return reports ?? Array.Empty<ExecutionReport>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting execution reports");
            throw;
        }
    }
}
