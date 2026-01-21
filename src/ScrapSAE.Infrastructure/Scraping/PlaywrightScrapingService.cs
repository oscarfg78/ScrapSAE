using Microsoft.Playwright;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Interfaces;

namespace ScrapSAE.Infrastructure.Scraping;

/// <summary>
/// Servicio de web scraping usando Playwright
/// </summary>
public class PlaywrightScrapingService : IScrapingService, IAsyncDisposable
{
    private readonly ILogger<PlaywrightScrapingService> _logger;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public PlaywrightScrapingService(ILogger<PlaywrightScrapingService> logger)
    {
        _logger = logger;
    }

    private async Task<IBrowser> GetBrowserAsync()
    {
        if (_browser == null)
        {
            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true
            });
        }
        return _browser;
    }

    public async Task<IEnumerable<ScrapedProduct>> ScrapeAsync(SiteProfile site, CancellationToken cancellationToken = default)
    {
        var products = new List<ScrapedProduct>();
        
        try
        {
            var selectorsJson = site.Selectors is string s ? s : JsonConvert.SerializeObject(site.Selectors);
            var selectors = JsonConvert.DeserializeObject<SiteSelectors>(selectorsJson);
            if (selectors == null)
            {
                _logger.LogError("Invalid selectors configuration for site {SiteName}", site.Name);
                return products;
            }

            var browser = await GetBrowserAsync();
            var context = await browser.NewContextAsync();
            var page = await context.NewPageAsync();

            // Navigate to site
            await page.GotoAsync(site.BaseUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60000
            });

            int currentPage = 1;
            var random = new Random();
            
            while (currentPage <= selectors.MaxPages && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Scraping page {Page} of {Site}", currentPage, site.Name);
                
                // Human simulation: Random pause before starting page processing
                await Task.Delay(random.Next(2000, 5000), cancellationToken);

                // Handle infinite scroll
                if (selectors.UsesInfiniteScroll)
                {
                    await ScrollToBottomAsync(page);
                }

                // Get product elements
                var productElements = await page.QuerySelectorAllAsync(selectors.ProductListSelector ?? "");
                
                foreach (var element in productElements)
                {
                    try
                    {
                        // Human simulation: Tiny random pause between items
                        if (random.Next(1, 10) > 7) // 30% chance of small extra delay
                        {
                            await Task.Delay(random.Next(500, 1500), cancellationToken);
                        }

                        var product = await ExtractProductAsync(page, element, selectors);
                        if (product != null)
                        {
                            products.Add(product);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error extracting product from element");
                    }
                }

                // Check for next page
                if (!string.IsNullOrEmpty(selectors.NextPageSelector))
                {
                    var nextButton = await page.QuerySelectorAsync(selectors.NextPageSelector);
                    if (nextButton != null)
                    {
                        // Human simulation: Random pause before clicking next
                        await Task.Delay(random.Next(3000, 7000), cancellationToken);
                        
                        await nextButton.ClickAsync();
                        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                        currentPage++;
                    }
                    else
                    {
                        break;
                    }
                }
                else
                {
                    break;
                }
            }

            await context.CloseAsync();
            
            _logger.LogInformation("Scraped {Count} products from {Site}", products.Count, site.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scraping site {SiteName}", site.Name);
        }

        return products;
    }

    private async Task<ScrapedProduct?> ExtractProductAsync(IPage page, IElementHandle element, SiteSelectors selectors)
    {
        var product = new ScrapedProduct();

        // Extract title
        if (!string.IsNullOrEmpty(selectors.TitleSelector))
        {
            var titleEl = await element.QuerySelectorAsync(selectors.TitleSelector);
            product.Title = titleEl != null ? await titleEl.InnerTextAsync() : null;
        }

        // Extract price
        if (!string.IsNullOrEmpty(selectors.PriceSelector))
        {
            var priceEl = await element.QuerySelectorAsync(selectors.PriceSelector);
            if (priceEl != null)
            {
                var priceText = await priceEl.InnerTextAsync();
                product.Price = ParsePrice(priceText);
            }
        }

        // Extract SKU
        if (!string.IsNullOrEmpty(selectors.SkuSelector))
        {
            var skuEl = await element.QuerySelectorAsync(selectors.SkuSelector);
            product.SkuSource = skuEl != null ? await skuEl.InnerTextAsync() : null;
        }

        // Extract image
        if (!string.IsNullOrEmpty(selectors.ImageSelector))
        {
            var imageEl = await element.QuerySelectorAsync(selectors.ImageSelector);
            if (imageEl != null)
            {
                product.ImageUrl = await imageEl.GetAttributeAsync("src");
            }
        }

        // Extract description
        if (!string.IsNullOrEmpty(selectors.DescriptionSelector))
        {
            var descEl = await element.QuerySelectorAsync(selectors.DescriptionSelector);
            product.Description = descEl != null ? await descEl.InnerTextAsync() : null;
        }

        // Get raw HTML
        product.RawHtml = await element.InnerHTMLAsync();
        product.ScrapedAt = DateTime.UtcNow;

        return product;
    }

    private async Task ScrollToBottomAsync(IPage page)
    {
        var previousHeight = 0L;
        var maxScrolls = 10;
        var scrollCount = 0;

        while (scrollCount < maxScrolls)
        {
            var currentHeight = await page.EvaluateAsync<long>("document.body.scrollHeight");
            
            if (currentHeight == previousHeight)
                break;

            await page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight)");
            await Task.Delay(1000);
            
            previousHeight = currentHeight;
            scrollCount++;
        }
    }

    private static decimal? ParsePrice(string priceText)
    {
        if (string.IsNullOrEmpty(priceText)) return null;
        
        // Remove currency symbols and text
        var cleaned = new string(priceText.Where(c => char.IsDigit(c) || c == '.' || c == ',').ToArray());
        
        // Handle different decimal separators
        cleaned = cleaned.Replace(",", ".");
        
        if (decimal.TryParse(cleaned, out var price))
            return price;
        
        return null;
    }

    public async Task<byte[]?> DownloadImageAsync(string imageUrl)
    {
        try
        {
            using var httpClient = new HttpClient();
            return await httpClient.GetByteArrayAsync(imageUrl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error downloading image from {Url}", imageUrl);
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser != null)
        {
            await _browser.CloseAsync();
        }
        _playwright?.Dispose();
    }
}
