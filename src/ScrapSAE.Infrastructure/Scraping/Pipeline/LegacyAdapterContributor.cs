using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Interfaces;

namespace ScrapSAE.Infrastructure.Scraping.Pipeline;

public class LegacyAdapterContributor : IContributor
{
    private readonly IScrapingService _legacyScrapingService;

    public ContributorDescriptor Descriptor => new ContributorDescriptor
    {
        Id = "LegacyPlaywrightScraper",
        Name = "Legacy Playwright Scraper Adapter",
        Type = "Primary"
    };

    public LegacyAdapterContributor(IScrapingService legacyScrapingService)
    {
        _legacyScrapingService = legacyScrapingService ?? throw new ArgumentNullException(nameof(legacyScrapingService));
    }

    public async Task<ContributorResult> ExecuteAsync(ExtractionExecutionRequest request, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var result = new ContributorResult { ContributorId = Descriptor.Id };

        try
        {
            var siteProfile = new ScrapSAE.Core.Entities.SiteProfile
            {
                Id = Guid.TryParse(request.ProviderConfig.ProviderId, out var id) ? id : Guid.NewGuid(),
                Name = "[TEMP] Wizard Demo",
                BaseUrl = request.ProviderConfig.CatalogUrl,
                Selectors = System.Text.Json.JsonSerializer.Serialize(request.ProviderConfig.Selectors),
                MaxProductsPerScrape = request.ProductLimit,
                StrategyType = "Generic"
            };

            var context = new ScrapSAE.Core.DTOs.ScrapeExecutionContext
            {
                IsHeadless = true,
                KeepBrowser = false,
                ManualLogin = false
            };

            List<ScrapedProduct> products;
            
            bool hasListContainer = request.ProviderConfig.Selectors != null && 
                                    !string.IsNullOrWhiteSpace(request.ProviderConfig.Selectors.ProductListSelector);
            
            if (!string.IsNullOrWhiteSpace(request.ProviderConfig.DetailUrl) && !hasListContainer && siteProfile.StrategyType == "Generic")
            {
                _legacyScrapingService.RegisterSite(siteProfile);
                
                var options = new ScrapSAE.Core.DTOs.DirectUrlScrapeOptions
                {
                    InspectOnly = false,
                    SingleProductOnly = false,
                    ExpandRelated = true
                };

                products = await _legacyScrapingService.ScrapeDirectUrlsAsync(
                    new List<string> { request.ProviderConfig.DetailUrl },
                    siteProfile.Id,
                    options,
                    cancellationToken);
            }
            else
            {
                products = (await _legacyScrapingService.ScrapeAsync(siteProfile, context, cancellationToken)).ToList();
            }
            
            result.Status = ContributorStatus.Success;
            
            foreach (var p in products)
            {
                var url = p.SourceUrl ?? request.ProviderConfig.CatalogUrl;
                if (!string.IsNullOrWhiteSpace(p.Title))
                    result.Observations.Add(new ProductObservation { Field = "Title", RawValue = p.Title, SourceUrl = url, Timestamp = DateTime.UtcNow, ContributorId = Descriptor.Id });
                if (!string.IsNullOrWhiteSpace(p.SkuSource))
                    result.Observations.Add(new ProductObservation { Field = "Sku", RawValue = p.SkuSource, SourceUrl = url, Timestamp = DateTime.UtcNow, ContributorId = Descriptor.Id });
                if (!string.IsNullOrWhiteSpace(p.ImageUrl))
                    result.Observations.Add(new ProductObservation { Field = "ImageUrl", RawValue = p.ImageUrl, SourceUrl = url, Timestamp = DateTime.UtcNow, ContributorId = Descriptor.Id });
                if (p.Price.HasValue)
                    result.Observations.Add(new ProductObservation { Field = "Price", RawValue = p.Price.Value.ToString(), SourceUrl = url, Timestamp = DateTime.UtcNow, ContributorId = Descriptor.Id });
                if (!string.IsNullOrWhiteSpace(p.Description))
                    result.Observations.Add(new ProductObservation { Field = "Description", RawValue = p.Description, SourceUrl = url, Timestamp = DateTime.UtcNow, ContributorId = Descriptor.Id });
            }
        }
        catch (Exception ex)
        {
            result.Status = ContributorStatus.FatalFailure;
            result.ErrorMessage = ex.Message;
        }
        finally
        {
            sw.Stop();
            result.DurationMs = (int)sw.ElapsedMilliseconds;
        }

        return result;
    }
}
