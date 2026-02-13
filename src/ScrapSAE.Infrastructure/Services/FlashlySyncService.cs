using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Interfaces;

namespace ScrapSAE.Infrastructure.Services;

public class FlashlySyncService : IFlashlySyncService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly SyncOptionsConfig _syncOptions;
    private readonly ILogger<FlashlySyncService> _logger;

    public FlashlySyncService(
        HttpClient httpClient,
        IOptions<SyncOptionsConfig> syncOptions,
        ILogger<FlashlySyncService> logger)
    {
        _httpClient = httpClient;
        _syncOptions = syncOptions.Value;
        _logger = logger;
    }

    public async Task<FlashlySyncResult> SyncProductsAsync(IEnumerable<StagingProduct> products, CancellationToken cancellationToken = default)
    {
        var input = products
            .Where(p => !string.IsNullOrWhiteSpace(p.SkuSource))
            .ToList();

        if (input.Count == 0)
        {
            return new FlashlySyncResult
            {
                Success = true,
                Message = "No products to sync."
            };
        }

        _logger.LogInformation("Starting Flashly sync for {Count} products", input.Count);

        var batchSize = _syncOptions.BatchSize <= 0 ? 50 : _syncOptions.BatchSize;
        var created = 0;
        var updated = 0;
        var allErrors = new List<FlashlySyncError>();
        string? lastJobId = null;

        foreach (var batch in Batch(input, batchSize))
        {
            var dtoBatch = batch.Select(FlashlyProductMapper.ToFlashlyDto).ToList();
            var requestBody = new { products = dtoBatch };
            var requestJson = JsonSerializer.Serialize(requestBody, JsonOptions);

            var response = await ExecuteWithRetriesAsync(async () =>
            {
                using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
                return await _httpClient.PostAsync("/api/v1/products/sync", content, cancellationToken);
            }, cancellationToken);

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Flashly sync failed with status {StatusCode}. Body: {Body}", response.StatusCode, payload);
                return new FlashlySyncResult
                {
                    Success = false,
                    Message = $"HTTP {(int)response.StatusCode}: {payload}",
                    Errors = allErrors
                };
            }

            var parsed = JsonSerializer.Deserialize<FlashlySyncResponse>(payload, JsonOptions);
            if (parsed?.Results != null)
            {
                created += parsed.Results.Created;
                updated += parsed.Results.Updated;
                if (parsed.Results.Errors.Count > 0)
                {
                    allErrors.AddRange(parsed.Results.Errors);
                }
            }

            if (!string.IsNullOrWhiteSpace(parsed?.JobId))
            {
                lastJobId = parsed.JobId;
            }
        }

        var result = new FlashlySyncResult
        {
            Success = true,
            Created = created,
            Updated = updated,
            Errors = allErrors,
            JobId = lastJobId,
            Message = $"Flashly sync completed. Created: {created}, Updated: {updated}, Errors: {allErrors.Count}."
        };

        _logger.LogInformation(result.Message);
        return result;
    }

    private async Task<HttpResponseMessage> ExecuteWithRetriesAsync(
        Func<Task<HttpResponseMessage>> operation,
        CancellationToken cancellationToken)
    {
        var retries = Math.Max(0, _syncOptions.RetryAttempts);
        var baseDelay = Math.Max(1, _syncOptions.RetryDelaySeconds);
        Exception? lastException = null;

        for (var attempt = 0; attempt <= retries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var response = await operation();
                if ((int)response.StatusCode >= 500 && attempt < retries)
                {
                    var delaySeconds = Math.Pow(2, attempt + 1) * baseDelay;
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                    continue;
                }

                return response;
            }
            catch (HttpRequestException ex) when (attempt < retries)
            {
                lastException = ex;
                var delaySeconds = Math.Pow(2, attempt + 1) * baseDelay;
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            }
        }

        throw lastException ?? new HttpRequestException("Flashly sync failed after retries.");
    }

    private static IEnumerable<List<T>> Batch<T>(List<T> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
        {
            yield return source.Skip(i).Take(size).ToList();
        }
    }
}
