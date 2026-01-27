using ScrapSAE.Api.Models;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Interfaces;
using System.Text.Json;

namespace ScrapSAE.Api.Services;

public sealed class ScrapingRunner
{
    private readonly IScrapingService _scrapingService;
    private readonly ISupabaseRestClient _supabase;
    private readonly IAIProcessorService _aiProcessorService;
    private readonly SupabaseTableService<SyncLog> _syncLogService;
    private readonly IScrapeControlService _scrapeControl;

    public ScrapingRunner(
        IScrapingService scrapingService,
        ISupabaseRestClient supabase,
        IAIProcessorService aiProcessorService,
        SupabaseTableService<SyncLog> syncLogService,
        IScrapeControlService scrapeControl)
    {
        _scrapingService = scrapingService;
        _supabase = supabase;
        _aiProcessorService = aiProcessorService;
        _syncLogService = syncLogService;
        _scrapeControl = scrapeControl;
    }

    public async Task<ScrapeRunResult> RunForSiteAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow;
        var site = await GetSiteAsync(siteId);
        if (site == null)
        {
            throw new InvalidOperationException($"Site {siteId} not found.");
        }

        await LogAsync(site, "scrape", "start", "Inicio de scraping.");
        var controlToken = _scrapeControl.Start(siteId);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, controlToken);
        List<ScrapedProduct> scraped;
        try
        {
            scraped = (await _scrapingService.ScrapeAsync(site, linkedCts.Token)).ToList();
        }
        catch (Exception ex)
        {
            _scrapeControl.MarkError(siteId, ex.Message);
            await LogAsync(site, "scrape", "error", ex.Message);
            throw;
        }
        var created = 0;
        var updated = 0;
        var skipped = 0;

        foreach (var item in scraped)
        {
            if (string.IsNullOrWhiteSpace(item.SkuSource))
            {
                skipped++;
                continue;
            }

            var existing = await GetStagingBySkuAsync(siteId, item.SkuSource);
            if (existing == null)
            {
                var staging = MapToStaging(siteId, item);
                staging.AIProcessedJson = await BuildAiJsonAsync(item, cancellationToken);
                await _supabase.PostAsync("staging_products", staging);
                created++;
            }
            else
            {
                var update = new
                {
                    raw_data = item.RawHtml,
                    ai_processed_json = await BuildAiJsonAsync(item, cancellationToken),
                    updated_at = DateTime.UtcNow,
                    last_seen_at = DateTime.UtcNow
                };
                await _supabase.PatchAsync<StagingProduct>($"staging_products?id=eq.{existing.Id}", update);
                updated++;
            }
        }

        var duration = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds;
        await LogAsync(site, "scrape", "success", $"Scraping finalizado. Productos creados: {created}. Actualizados: {updated}.", duration);
        _scrapeControl.MarkCompleted(siteId, "Scraping completado.");
        return new ScrapeRunResult
        {
            SiteId = siteId,
            StartedAtUtc = startedAt,
            ProductsFound = scraped.Count,
            ProductsCreated = created,
            ProductsUpdated = updated,
            ProductsSkipped = skipped,
            DurationMs = duration
        };
    }

    private async Task<SiteProfile?> GetSiteAsync(Guid siteId)
    {
        var sites = await _supabase.GetAsync<SiteProfile>($"config_sites?id=eq.{siteId}&select=*");
        return sites.FirstOrDefault();
    }

    private async Task<StagingProduct?> GetStagingBySkuAsync(Guid siteId, string skuSource)
    {
        var query = $"staging_products?site_id=eq.{siteId}&sku_source=eq.{Uri.EscapeDataString(skuSource)}&select=*";
        var results = await _supabase.GetAsync<StagingProduct>(query);
        return results.FirstOrDefault();
    }

    private static StagingProduct MapToStaging(Guid siteId, ScrapedProduct item)
    {
        return new StagingProduct
        {
            Id = Guid.NewGuid(),
            SiteId = siteId,
            SkuSource = item.SkuSource,
            RawData = item.RawHtml,
            Status = "pending",
            Attempts = 0,
            LastSeenAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private async Task LogAsync(SiteProfile site, string operationType, string status, string message, int? durationMs = null)
    {
        try
        {
            var log = new SyncLog
            {
                OperationType = operationType,
                SiteId = site.Id,
                Status = status,
                Message = message,
                DurationMs = durationMs,
                CreatedAt = DateTime.UtcNow
            };
            await _syncLogService.CreateAsync(log);
        }
        catch
        {
            // Avoid breaking scraping flow if logging fails.
        }
    }

    private async Task<string?> BuildAiJsonAsync(ScrapedProduct scrapedProduct, CancellationToken cancellationToken)
    {
        var rawPayload = new
        {
            scrapedProduct.SkuSource,
            scrapedProduct.Title,
            scrapedProduct.Description,
            scrapedProduct.Price,
            scrapedProduct.ImageUrl,
            scrapedProduct.Brand,
            scrapedProduct.Category,
            scrapedProduct.Attributes
        };

        var rawData = JsonSerializer.Serialize(rawPayload);

        try
        {
            var processed = await _aiProcessorService.ProcessProductAsync(rawData, cancellationToken);
            processed.Sku ??= scrapedProduct.SkuSource;
            processed.Name = string.IsNullOrWhiteSpace(processed.Name) ? (scrapedProduct.Title ?? string.Empty) : processed.Name;
            processed.Description = string.IsNullOrWhiteSpace(processed.Description) ? (scrapedProduct.Description ?? string.Empty) : processed.Description;
            processed.Brand ??= scrapedProduct.Brand;
            processed.Price ??= scrapedProduct.Price;

            return JsonSerializer.Serialize(processed);
        }
        catch
        {
            return JsonSerializer.Serialize(new
            {
                scrapedProduct.Title,
                scrapedProduct.Price,
                scrapedProduct.ImageUrl,
                scrapedProduct.Description,
                scrapedProduct.Attributes
            });
        }
    }
}
