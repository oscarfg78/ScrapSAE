using ScrapSAE.Api.Models;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Interfaces;

namespace ScrapSAE.Api.Services;

public sealed class ScrapingRunner
{
    private readonly IScrapingService _scrapingService;
    private readonly ISupabaseRestClient _supabase;

    public ScrapingRunner(IScrapingService scrapingService, ISupabaseRestClient supabase)
    {
        _scrapingService = scrapingService;
        _supabase = supabase;
    }

    public async Task<ScrapeRunResult> RunForSiteAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow;
        var site = await GetSiteAsync(siteId);
        if (site == null)
        {
            throw new InvalidOperationException($"Site {siteId} not found.");
        }

        var scraped = (await _scrapingService.ScrapeAsync(site, cancellationToken)).ToList();
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
                await _supabase.PostAsync("staging_products", staging);
                created++;
            }
            else
            {
                var update = new
                {
                    raw_data = item.RawHtml,
                    updated_at = DateTime.UtcNow,
                    last_seen_at = DateTime.UtcNow
                };
                await _supabase.PatchAsync<StagingProduct>($"staging_products?id=eq.{existing.Id}", update);
                updated++;
            }
        }

        var duration = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds;
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
}
