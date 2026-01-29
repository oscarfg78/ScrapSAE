using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Newtonsoft.Json;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;

namespace ScrapSAE.Infrastructure.Services;

/// <summary>
/// Orchestrates scraping strategies with weighted progress, fault tolerance, and detailed reporting.
/// </summary>
public class ScrapingProcessManager
{
    private readonly ILogger<ScrapingProcessManager> _logger;
    private readonly Random _random = new Random();

    public ScrapingProcessManager(ILogger<ScrapingProcessManager> logger)
    {
        _logger = logger;
    }

    // --- STRATEGY A: CATEGORY / LIST NAVIGATION ---
    public async Task<List<ScrapedProduct>> ExecuteCategoryStrategyAsync(
        IPage page,
        SiteProfile site,
        SiteSelectors selectors,
        Func<Task<List<ScrapedProduct>>> listCollectionCallback,
        Func<string, Task> stepLogger,
        CancellationToken cancellationToken)
    {
         var products = new List<ScrapedProduct>();
         try 
         {
             await stepLogger("Strategy: CATEGORY NAVIGATION - Starting...");
             
             // 1. Setup & Hydration (10%)
             await WaitForHydrationAsync(page, stepLogger);
             
             // 2. Page Analysis (10%)
             if (!await IsCategoryPageAsync(page))
             {
                 await stepLogger("Warning: Page does not look like a category list. Attempting fallback...");
             }

             // 3. Pagination Loop (70%)
             // The callback handles the actual loop (CollectProductsFromListAsync)
             // We wrap it to track result count.
             await stepLogger("Executing Pagination Loop...");
             var result = await listCollectionCallback();
             
             if (result != null) products.AddRange(result);

             // 4. Completion (10%)
             await stepLogger($"Strategy Complete. Total Items: {products.Count}");
         }
         catch (Exception ex)
         {
             _logger.LogError(ex, "Category Strategy Failed");
             await stepLogger($"STRATEGY FAILED: {ex.Message}");
         }
         return products;
    }

    // --- STRATEGY B: AI EXTRACTION (Fallback) ---
    public async Task<List<ScrapedProduct>> ExecuteAiStrategyAsync(
        IPage page,
        Func<Task<List<ScrapedProduct>>> aiCallback,
        Func<string, Task> stepLogger,
        CancellationToken cancellationToken)
    {
        var products = new List<ScrapedProduct>();
        try
        {
            await stepLogger("Strategy: AI EXTRACTION - Starting...");
            await WaitForHydrationAsync(page, stepLogger);

            // 1. Content Capture (20%)
            await stepLogger("Capturing Page Content...");
            
            // 2. AI Analysis (60%)
            await stepLogger("Invoking AI Model...");
            var result = await aiCallback();
            
            // 3. Parsing (20%)
            if (result != null) products.AddRange(result);
            await stepLogger($"AI Strategy Complete. Items Extracted: {products.Count}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI Strategy Failed");
            await stepLogger($"AI STRATEGY FAILED: {ex.Message}");
        }
        return products;
    }

    // --- STRATEGY C: BREADCRUMB TRAVERSAL ---
    public async Task<List<string>> ExecuteBreadcrumbStrategyAsync(
        IPage page,
        Func<Task<List<string>>> breadcrumbCallback,
         Func<string, Task> stepLogger,
        CancellationToken cancellationToken)
    {
        var links = new List<string>();
        try
        {
             await stepLogger("Strategy: BREADCRUMB TRAVERSAL - Starting...");
             
             // 1. Extraction (30%)
             var rawLinks = await breadcrumbCallback();
             
             // 2. Validation (20%)
             foreach(var link in rawLinks)
             {
                 if (IsValidFestoUrl(link)) links.Add(link);
             }
             
             // 3. Recursive Trigger (50%) -> Handled by caller (Process Orchestrator)
             await stepLogger($"Breadcrumb Strategy Complete. Found {links.Count} valid paths.");
        }
        catch (Exception ex)
        {
             _logger.LogError(ex, "Breadcrumb Strategy Failed");
             await stepLogger($"BREADCRUMB STRATEGY FAILED: {ex.Message}");
        }
        return links;
    }

    // --- STRATEGY D: DEEP DISCOVERY (Widgets/Recommendations) ---
    public async Task<List<string>> ExecuteDeepDiscoveryStrategyAsync(
        IPage page,
        Func<Task<List<string>>> discoveryCallback,
        Func<string, Task> stepLogger,
        CancellationToken cancellationToken)
    {
        var links = new List<string>();
        try
        {
            await stepLogger("Strategy: DEEP DISCOVERY - Starting...");
            await WaitForHydrationAsync(page, stepLogger);

            // 1. Page Analysis (20%)
            // Check for widgets
            
            // 2. Harvesting (30%)
            var found = await discoveryCallback();
            if (found != null) links.AddRange(found);
            
            await stepLogger($"Deep Discovery Complete. Found {links.Count} potential targets.");
        }
        catch (Exception ex)
        {
             _logger.LogError(ex, "Deep Discovery Strategy Failed");
             await stepLogger($"DEEP DISCOVERY FAILED: {ex.Message}");
            
        }
        return links;
    }

    // --- STRATEGY E: HYBRID AUTOMATION (The "One Mode") ---
    public async Task<List<ScrapedProduct>> ExecuteHybridStrategyAsync(
        IPage page,
        SiteProfile site,
        SiteSelectors selectors,
        Func<Task<List<ScrapedProduct>>> categoryCallback,
        Func<Task<List<ScrapedProduct>>> productCallback,
        Func<Task<List<string>>> discoveryCallback,
        Func<string, Task> stepLogger,
        CancellationToken cancellationToken)
    {
        var allProducts = new List<ScrapedProduct>();
        try 
        {
            await stepLogger("Strategy: HYBRID AUTOMATION - Analyzing Context...");
            await WaitForHydrationAsync(page, stepLogger);

            // 1. Context Switch
            if (await IsCategoryPageAsync(page))
            {
                await stepLogger("Context: CATEGORY detected. Switching to Category Strategy.");
                var output = await ExecuteCategoryStrategyAsync(page, site, selectors, categoryCallback, stepLogger, cancellationToken);
                allProducts.AddRange(output);
            }
            else if (await IsProductPageAsync(page))
            {
                await stepLogger("Context: PRODUCT detected. Extracting & Discovering.");
                
                // Extract Product
                var output = await productCallback();
                if (output != null) allProducts.AddRange(output);

                // Run Discovery to find neighbors
                var neighbors = await ExecuteDeepDiscoveryStrategyAsync(page, discoveryCallback, stepLogger, cancellationToken);
                await stepLogger($"Hybrid Info: Found {neighbors.Count} related links to queue.");
                // NOTE: Queueing is handled by the caller (Recursive Crawler), we just return data/logs here.
            }
            else 
            {
                await stepLogger("Context: UNKNOWN. Attempting Generic Discovery...");
                 var neighbors = await ExecuteDeepDiscoveryStrategyAsync(page, discoveryCallback, stepLogger, cancellationToken);
            }
            
            await stepLogger($"Hybrid Strategy Cycle Complete. Total Extracted: {allProducts.Count}");

        }
        catch (Exception ex)
        {
             _logger.LogError(ex, "Hybrid Strategy Failed");
             await stepLogger($"HYBRID FAILED: {ex.Message}");
        }
        return allProducts;
    }


    // --- HELPERS ---

    private async Task WaitForHydrationAsync(IPage page, Func<string, Task> stepLogger)
    {
         try 
        {
            // Wait for critical UI elements that indicate React has hydrated
            // We combine product, list, and generic structural elements
            await page.WaitForSelectorAsync("header, footer, [class*='product-page-headline--'], [class*='categories-list-grid--'], .main-navigation", 
                new PageWaitForSelectorOptions { Timeout = 15000 });
        }
        catch (TimeoutException)
        {
             await stepLogger("⚠️ Hydration Warning: Page load slow or crucial elements missing. Proceeding with caution.");
        }
    }

    private async Task<bool> IsProductPageAsync(IPage page)
    {
        if (await page.Locator("[class*='product-page-headline--']").CountAsync() > 0) return true;
        if (await page.Locator("[class*='price-display-text--']").CountAsync() > 0) return true;
        
        var url = page.Url;
        if (url.Contains("/p/") || url.Contains("/a/")) return true;
        return false;
    }

    private async Task<bool> IsCategoryPageAsync(IPage page)
    {
        if (await page.Locator("[class*='categories-list-grid--']").CountAsync() > 0) return true;
        if (await page.Locator("[class*='article-card--']").CountAsync() > 0) return true;
        if (await page.Locator("[class*='Pagination_arrowButton']").CountAsync() > 0) return true;
        if (await page.Locator("div[class*='product-list--']").CountAsync() > 0) return true;

        // Fallback: Check if URL looks like a category path (Festo specific)
        var url = page.Url;
        if (url.Contains("/c/") || url.Contains("/cat/")) return true;

        return false;
    }

    private bool IsValidFestoUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (url.Contains("login")) return false;
        if (url.Contains("cart")) return false;
        if (!url.Contains("festo.com")) return false;
        return true;
    }

     private async Task HumanDelayAsync(int min, int max, CancellationToken token)
    {
        await Task.Delay(_random.Next(min, max), token);
    }

}
