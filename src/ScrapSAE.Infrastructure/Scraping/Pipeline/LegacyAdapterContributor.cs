using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Interfaces;

namespace ScrapSAE.Infrastructure.Scraping.Pipeline;

public class LegacyAdapterContributor : IContributor
{
    private readonly IScrapingService _legacyScrapingService;
    private readonly IServiceProvider? _serviceProvider;

    public ContributorDescriptor Descriptor => new ContributorDescriptor
    {
        Id = "LegacyPlaywrightScraper",
        Name = "Legacy Playwright Scraper Adapter",
        Type = "Primary"
    };

    public LegacyAdapterContributor(IScrapingService legacyScrapingService, IServiceProvider? serviceProvider = null)
    {
        _legacyScrapingService = legacyScrapingService ?? throw new ArgumentNullException(nameof(legacyScrapingService));
        _serviceProvider = serviceProvider;
    }

    public async Task<ContributorResult> ExecuteAsync(ExtractionExecutionRequest request, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var result = new ContributorResult { ContributorId = Descriptor.Id };

        try
        {
            var strategyType = !string.IsNullOrWhiteSpace(request.ProviderConfig.StrategyType)
                ? request.ProviderConfig.StrategyType
                : "Generic";

            var strategies = new List<ScrapSAE.Core.Entities.ScrapingStrategyDefinition>();
            if (strategyType.Equals("Shopify", StringComparison.OrdinalIgnoreCase) || strategyType.Equals("ShopifyApi", StringComparison.OrdinalIgnoreCase))
            {
                strategies.Add(new ScrapSAE.Core.Entities.ScrapingStrategyDefinition { StrategyName = "Shopify", Priority = 1, IsEnabled = true });
            }
            else
            {
                strategies.Add(new ScrapSAE.Core.Entities.ScrapingStrategyDefinition { StrategyName = "Direct", Priority = 1, IsEnabled = true });
                strategies.Add(new ScrapSAE.Core.Entities.ScrapingStrategyDefinition { StrategyName = "List", Priority = 2, IsEnabled = true });
            }

            var siteProfile = new ScrapSAE.Core.Entities.SiteProfile
            {
                Id = Guid.TryParse(request.ProviderConfig.ProviderId, out var id) ? id : Guid.NewGuid(),
                Name = "[TEMP] Wizard Demo",
                BaseUrl = request.ProviderConfig.CatalogUrl,
                Selectors = System.Text.Json.JsonSerializer.Serialize(request.ProviderConfig.Selectors),
                MaxProductsPerScrape = request.ProductLimit,
                StrategyType = strategyType,
                Strategies = strategies
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

            if ((strategyType.Equals("Shopify", StringComparison.OrdinalIgnoreCase) ||
                 strategyType.Equals("ShopifyApi", StringComparison.OrdinalIgnoreCase)) &&
                _serviceProvider != null)
            {
                var shopifyStrategy = _serviceProvider.GetKeyedService<IProviderScraperStrategy>("Shopify");
                if (shopifyStrategy != null)
                {
                    products = (await shopifyStrategy.ScrapeAsync(siteProfile, context, cancellationToken)).ToList();
                }
                else
                {
                    products = (await _legacyScrapingService.ScrapeAsync(siteProfile, context, cancellationToken)).ToList();
                }
            }
            else if (!string.IsNullOrWhiteSpace(request.ProviderConfig.DetailUrl) && !hasListContainer && siteProfile.StrategyType == "Generic")
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
                var sku = p.SkuSource;
                if (string.IsNullOrWhiteSpace(sku))
                {
                    sku = InferSkuFromTitleOrUrl(p.Title, url);
                }

                if (!string.IsNullOrWhiteSpace(p.Title))
                    result.Observations.Add(new ProductObservation { Field = "Title", RawValue = p.Title, SourceUrl = url, Timestamp = DateTime.UtcNow, ContributorId = Descriptor.Id });
                if (!string.IsNullOrWhiteSpace(sku))
                    result.Observations.Add(new ProductObservation { Field = "Sku", RawValue = sku, SourceUrl = url, Timestamp = DateTime.UtcNow, ContributorId = Descriptor.Id });
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

    private static string? InferSkuFromTitleOrUrl(string? title, string? url)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            var match = System.Text.RegularExpressions.Regex.Match(title, @"\b[A-Za-z0-9]+(?:[-_][A-Za-z0-9]+){2,}\b");
            if (match.Success) return match.Value.ToUpperInvariant();
        }

        if (!string.IsNullOrWhiteSpace(url))
        {
            var match = System.Text.RegularExpressions.Regex.Match(url, @"\b[A-Za-z0-9]+(?:[-_][A-Za-z0-9]+){2,}\b");
            if (match.Success) return match.Value.ToUpperInvariant();
        }

        return null;
    }
}
