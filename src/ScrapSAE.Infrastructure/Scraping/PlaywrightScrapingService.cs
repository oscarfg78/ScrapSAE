using Microsoft.Playwright;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Text.Json;
using System.Text;
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
    private readonly IScrapeControlService _scrapeControl;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;
    private bool _isPersistentContext;
    private readonly Random _random = new();

    public PlaywrightScrapingService(ILogger<PlaywrightScrapingService> logger, IScrapeControlService scrapeControl)
    {
        _logger = logger;
        _scrapeControl = scrapeControl;
    }

    private async Task<IBrowserContext> GetContextAsync(BrowserNewContextOptions? options = null)
    {
        if (_context != null)
        {
            return _context;
        }

        _playwright = await Playwright.CreateAsync();
        var headless = true;
        var headlessEnv = Environment.GetEnvironmentVariable("SCRAPSAE_HEADLESS");
        if (!string.IsNullOrWhiteSpace(headlessEnv) &&
            bool.TryParse(headlessEnv, out var parsedHeadless))
        {
            headless = parsedHeadless;
        }

        var manualEnv = Environment.GetEnvironmentVariable("SCRAPSAE_MANUAL_LOGIN_ACTIVE");
        if (!string.IsNullOrWhiteSpace(manualEnv) && manualEnv == "true")
        {
            headless = false;
        }

        var manualLoginEnv = Environment.GetEnvironmentVariable("SCRAPSAE_MANUAL_LOGIN");
        var forceManualLoginEnv = Environment.GetEnvironmentVariable("SCRAPSAE_FORCE_MANUAL_LOGIN");
        var festoManualLoginEnv = Environment.GetEnvironmentVariable("SCRAPSAE_MANUAL_LOGIN_FESTO");
        if ((!string.IsNullOrWhiteSpace(manualLoginEnv) && bool.TryParse(manualLoginEnv, out var manualLogin) && manualLogin) ||
            (!string.IsNullOrWhiteSpace(forceManualLoginEnv) && bool.TryParse(forceManualLoginEnv, out var forceManual) && forceManual) ||
            (!string.IsNullOrWhiteSpace(festoManualLoginEnv) && bool.TryParse(festoManualLoginEnv, out var forceFesto) && forceFesto))
        {
            headless = false;
        }

        var userDataDir = Environment.GetEnvironmentVariable("SCRAPSAE_PROFILE_DIR");
        if (!string.IsNullOrWhiteSpace(userDataDir))
        {
            var persistentOptions = new BrowserTypeLaunchPersistentContextOptions
            {
                Headless = headless
            };
            
            if (options?.StorageStatePath != null)
            {
                // Note: LaunchPersistentContextAsync doesn't support StorageStatePath directly in the same way 
                // as NewContextAsync, it relies on userDataDir. 
                // We might need to manually load if we are mixing strategies, but for now let's assume one strategy.
            }

            _context = await _playwright.Chromium.LaunchPersistentContextAsync(userDataDir, persistentOptions);
            _isPersistentContext = true;
            return _context;
        }

        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = headless
        });

        _context = await _browser.NewContextAsync(options ?? new BrowserNewContextOptions());
        _isPersistentContext = false;
        return _context;
    }

    private async Task<IBrowser> GetBrowserAsync()
    {
        if (_browser == null)
        {
            var context = await GetContextAsync();
            _browser = context.Browser;
        }
        return _browser;
    }

    public async Task<IEnumerable<ScrapedProduct>> ScrapeAsync(SiteProfile site, CancellationToken cancellationToken = default)
    {
        var products = new List<ScrapedProduct>();
        
        try
        {
            string selectorsJson;
            if (site.Selectors is JsonElement jsonElement)
            {
                selectorsJson = jsonElement.GetRawText();
            }
            else if (site.Selectors is string s)
            {
                selectorsJson = s;
            }
            else
            {
                selectorsJson = JsonConvert.SerializeObject(site.Selectors);
            }
            var selectors = JsonConvert.DeserializeObject<SiteSelectors>(selectorsJson);
            if (selectors == null)
            {
                _logger.LogError("Invalid selectors configuration for site {SiteName}", site.Name);
                return products;
            }
            FillSelectorsFromJson(selectors, selectorsJson);
            _logger.LogInformation("Using selectors for {SiteName}: {SelectorsJson}", site.Name, selectorsJson);
            if (string.IsNullOrWhiteSpace(selectors.ProductListSelector))
            {
                _logger.LogError("Missing ProductListSelector for site {SiteName}. SelectorsJson: {SelectorsJson}", site.Name, selectorsJson);
                return products;
            }

            var storageStatePath = GetStorageStatePath(site.Name);
            var contextOptions = new BrowserNewContextOptions();
            if (File.Exists(storageStatePath))
            {
                _logger.LogInformation("Found saved storage state for {SiteName}, loading session...", site.Name);
                contextOptions.StorageStatePath = storageStatePath;
            }

            var context = await GetContextAsync(contextOptions);
            var page = await context.NewPageAsync();

            var initialUrl = site.BaseUrl;
            if (site.RequiresLogin && !string.IsNullOrEmpty(site.LoginUrl))
            {
                initialUrl = site.LoginUrl;
            }

            // Navigate to site (or login URL if provided)
            await _scrapeControl.WaitIfPausedAsync(site.Id, cancellationToken);
            await page.GotoAsync(initialUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 90000
            });
            await AcceptCookiesAsync(page, cancellationToken);
            await SaveDebugScreenshotAsync(page, $"{site.Name}_initial");

            // Handle login if required
            if (site.RequiresLogin && !string.IsNullOrEmpty(site.CredentialsEncrypted))
            {
                var skipLoginEnv = Environment.GetEnvironmentVariable("SCRAPSAE_SKIP_LOGIN");
                if (!string.IsNullOrWhiteSpace(skipLoginEnv) &&
                    bool.TryParse(skipLoginEnv, out var skipLogin) &&
                    skipLogin)
                {
                    _logger.LogInformation("Skipping login for site {SiteName} due to SCRAPSAE_SKIP_LOGIN", site.Name);
                }
                else if (!await IsLoggedInAsync(page))
                {
                    _logger.LogInformation("Site {SiteName} requires login. Attempting to authenticate...", site.Name);
                    
                    var loginSuccess = false;
                    try 
                    {
                        await HandleLoginAsync(page, site, cancellationToken);
                        loginSuccess = await IsLoggedInAsync(page);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Automated login failed.");
                    }

                    if (!loginSuccess)
                    {
                        _logger.LogWarning("Automated login failed or session not established. Initiating manual login fallback...");
                        await ManualLoginFallbackAsync(site, cancellationToken);
                        
                        // Re-initialize context with new state
                        await DisposeAsync(); // Close current
                        
                        // Reload with state
                        context = await GetContextAsync(new BrowserNewContextOptions { StorageStatePath = GetStorageStatePath(site.Name) });
                        page = await context.NewPageAsync();
                        
                        // Navigate again
                        await page.GotoAsync(initialUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 90000 });
                    }
                }
                else
                {
                    _logger.LogInformation("Detected existing login session for site {SiteName}", site.Name);
                }
            }
            
            if (site.RequiresLogin && !string.IsNullOrEmpty(site.LoginUrl))
            {
                if (!page.Url.StartsWith(site.BaseUrl, StringComparison.OrdinalIgnoreCase))
                {
                    await _scrapeControl.WaitIfPausedAsync(site.Id, cancellationToken);
                    await page.GotoAsync(site.BaseUrl, new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = 90000
                    });
                    await AcceptCookiesAsync(page, cancellationToken);
                    await SaveDebugScreenshotAsync(page, $"{site.Name}_post_login");
                }
            }
            
            await SaveDebugHtmlAsync(await page.ContentAsync(), $"{site.Name}_after_login_check");

            var startUrl = Environment.GetEnvironmentVariable("SCRAPSAE_START_URL");
            if (!string.IsNullOrWhiteSpace(startUrl))
            {
                _logger.LogInformation("Using start URL override: {StartUrl}", startUrl);
                await _scrapeControl.WaitIfPausedAsync(site.Id, cancellationToken);
                var startProducts = await TryScrapeProductDetailWithVariationsAsync(
                    page,
                    startUrl,
                    selectors,
                    cancellationToken);
                if (startProducts.Count > 0)
                {
                    return startProducts;
                }
            }

            var categoryProducts = await TryScrapeCategoriesAsync(page, site, selectors, cancellationToken);
            if (categoryProducts.Count > 0)
            {
                return categoryProducts;
            }

            int currentPage = 1;
            
            while (currentPage <= selectors.MaxPages && !cancellationToken.IsCancellationRequested)
            {
                await _scrapeControl.WaitIfPausedAsync(site.Id, cancellationToken);
                _logger.LogInformation("Scraping page {Page} of {Site}", currentPage, site.Name);
                
                // Human simulation: Random pause before starting page processing
                await Task.Delay(_random.Next(2000, 5000), cancellationToken);

                // Handle infinite scroll
                if (selectors.UsesInfiniteScroll)
                {
                    await _scrapeControl.WaitIfPausedAsync(site.Id, cancellationToken);
                    await ScrollToBottomAsync(page);
                }

                // Get product elements
                var productElements = await page.QuerySelectorAllAsync(selectors.ProductListSelector ?? "");
                var usedFallbackSelector = string.Empty;
                if (productElements.Count == 0)
                {
                    (productElements, usedFallbackSelector) = await TryFindProductElementsAsync(page, selectors);
                    if (productElements.Count > 0)
                    {
                        _logger.LogInformation("Fallback product selector matched: {Selector}", usedFallbackSelector);
                    }
                }
                
                await SaveDebugHtmlAsync(await page.ContentAsync(), $"{site.Name}_page_{currentPage}");
                
                foreach (var element in productElements)
                {
                    try
                    {
                        await _scrapeControl.WaitIfPausedAsync(site.Id, cancellationToken);
                        // Human simulation: Tiny random pause between items
                        if (_random.Next(1, 10) > 7) // 30% chance of small extra delay
                        {
                            await Task.Delay(_random.Next(500, 1500), cancellationToken);
                        }

                        var product = await ExtractProductAsync(page, element, selectors);
                        if (product != null)
                        {
                            if (string.IsNullOrWhiteSpace(product.Title))
                            {
                                var fallbackTitle = await element.InnerTextAsync();
                                if (!string.IsNullOrWhiteSpace(fallbackTitle))
                                {
                                    product.Title = fallbackTitle.Trim();
                                }
                            }
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
                        await Task.Delay(_random.Next(3000, 7000), cancellationToken);
                        
                        await _scrapeControl.WaitIfPausedAsync(site.Id, cancellationToken);
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

            if (!_isPersistentContext)
            {
                await context.CloseAsync();
            }
            
            _logger.LogInformation("Scraped {Count} products from {Site}", products.Count, site.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scraping site {SiteName}", site.Name);
        }

        return products;
    }

    private async Task HandleLoginAsync(IPage page, SiteProfile site, CancellationToken cancellationToken)
    {
        try
        {
            // Parse credentials format: "email|password"
            if (string.IsNullOrEmpty(site.CredentialsEncrypted))
            {
                _logger.LogWarning("No credentials provided for site {SiteName}", site.Name);
                return;
            }

            var credentials = site.CredentialsEncrypted.Split('|');
            if (credentials.Length < 2)
            {
                _logger.LogWarning("Invalid credentials format for site {SiteName}", site.Name);
                return;
            }

            var email = credentials[0].Trim();
            var password = credentials[1].Trim();

            _logger.LogInformation("Logging in to {SiteName} as {Email}", site.Name, email);

            var manualLogin = Environment.GetEnvironmentVariable("SCRAPSAE_MANUAL_LOGIN");
            var forceManualLogin = Environment.GetEnvironmentVariable("SCRAPSAE_FORCE_MANUAL_LOGIN");
            var forceManualLoginFesto = Environment.GetEnvironmentVariable("SCRAPSAE_MANUAL_LOGIN_FESTO");
            var shouldManual =
                (!string.IsNullOrWhiteSpace(manualLogin) && bool.TryParse(manualLogin, out var useManual) && useManual) ||
                (!string.IsNullOrWhiteSpace(forceManualLogin) && bool.TryParse(forceManualLogin, out var forceManual) && forceManual) ||
                (site.Name.Equals("Festo", StringComparison.OrdinalIgnoreCase) &&
                 !string.IsNullOrWhiteSpace(forceManualLoginFesto) &&
                 bool.TryParse(forceManualLoginFesto, out var forceFesto) && forceFesto);

            if (shouldManual)
            {
                _logger.LogInformation("Manual login enabled. Complete login in the browser window.");
                await WaitForManualLoginAsync(page, site, cancellationToken);
                _logger.LogInformation("Manual login completed.");
                return;
            }

            // Handle Festo-specific login
            if (site.Name.Equals("Festo", StringComparison.OrdinalIgnoreCase))
            {
                await FestoLoginAsync(page, site, email, password, cancellationToken);
            }
            else
            {
                // Generic login attempt
                await GenericLoginAsync(page, email, password, cancellationToken);
            }

            _logger.LogInformation("Successfully logged in to {SiteName}", site.Name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Login failed for site {SiteName}. Continuing with public access.", site.Name);
        }
    }

    private async Task FestoLoginAsync(IPage page, SiteProfile site, string email, string password, CancellationToken cancellationToken)
    {
        try
        {
            await AcceptCookiesAsync(page, cancellationToken);
            _logger.LogInformation("Login page title: {Title}", await page.TitleAsync());
            // Check if we're already logged in by looking for common logged-in indicators
            var loggedInIndicators = await page.QuerySelectorAsync(".user-menu, [data-testid='user-menu'], .account-menu");
            if (loggedInIndicators != null)
            {
                _logger.LogInformation("Appears to be already logged in to Festo");
                return;
            }

            // Look for login link or button - try multiple selectors
            _logger.LogInformation("Looking for Festo login button...");
            var loginButton = page.Locator(
                "a[href*='login'], a[href*='signin'], a[href*='login.aspx'], " +
                "button:has-text('Login'), button:has-text('Sign in'), " +
                "a:has-text('Login'), a:has-text('Sign in'), a:has-text('Iniciar sesión'), " +
                "[data-testid='login-button']");
            
            if (await loginButton.CountAsync() > 0)
            {
                _logger.LogInformation("Found login button, clicking...");
                var clicked = await VerifyAndClickAsync(loginButton.First, page, "festo_login_button", cancellationToken);
                if (!clicked)
                {
                    _logger.LogWarning("Login button found but not clickable.");
                }
                
                // Wait for navigation
                try
                {
                    await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                }
                catch
                {
                    _logger.LogWarning("Network idle timeout after clicking login button");
                }
                
                await Task.Delay(_random.Next(2000, 4000), cancellationToken);
            }
            else
            {
                _logger.LogInformation("No specific login button found, checking if login form is on current page");
            }

            // Wait for login form - check current URL to see if we navigated
            var currentUrl = page.Url;
            _logger.LogInformation("Current URL: {Url}", currentUrl);

            // Try to find email/login field with extended wait
            _logger.LogInformation("Waiting for login form fields...");
            await SaveDebugHtmlAsync(await page.ContentAsync(), $"{site.Name}_login_page_debug");
            var loginInputsReady = await WaitForLoginInputsAsync(page, cancellationToken);
            if (!loginInputsReady && !string.IsNullOrWhiteSpace(site.BaseUrl))
            {
                _logger.LogInformation("Login inputs not found. Navigating to base URL to trigger login flow.");
                await page.GotoAsync(site.BaseUrl, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 90000
                });
                await AcceptCookiesAsync(page, cancellationToken);
                await TryClickLoginLinkAsync(page, cancellationToken);
                await WaitForLoginInputsAsync(page, cancellationToken);
            }
            
            // List of possible email input selectors for Festo
            var emailSelectors = new string[]
            {
                "input[type='email']",
                "input[name*='email']", 
                "input[id*='email']",
                "input[placeholder*='email']",
                "input[placeholder*='Email']",
                "input[name*='login']",
                "input[name*='username']",
                "input[id*='txtEmail']",
                "input[id*='Email']",
                "#email",
                "#login"
            };

            var emailFilled = await TryFillEmailAsync(page, email, cancellationToken);
            IElementHandle? emailField = null;
            try
            {
                var emailLabel = page.GetByLabel("Dirección de correo electrónico", new() { Exact = false });
                if (await emailLabel.CountAsync() > 0)
                {
                    await emailLabel.First.FillAsync(email);
                    _logger.LogInformation("Filled email field via label");
                    emailFilled = true;
                }
            }
            catch
            {
                // Ignore label lookup failures and fall back to selector search.
            }

            foreach (var selector in emailSelectors)
            {
                try
                {
                    emailField = await page.QuerySelectorAsync(selector);
                    if (emailField != null)
                    {
                        _logger.LogInformation("Found email input with selector: {Selector}", selector);
                        break;
                    }
                }
                catch { }
            }

            if (emailField != null && !emailFilled)
            {
                await emailField.FillAsync(email);
                _logger.LogInformation("Filled email field");
                await Task.Delay(_random.Next(800, 1500), cancellationToken);
            }
            else if (!emailFilled && emailField == null)
            {
                await LogInputSummaryAsync(page, "festo_login_email_inputs");
                _logger.LogWarning("Could not find email input field with any known selectors");
                await SaveDebugScreenshotAsync(page, "festo_login_missing_email");
            }

            // Step 1: continue to password step
            var continueClicked = await TryClickButtonInFramesAsync(
                page,
                new[]
                {
                    "#btnContinue",
                    "button[data-skbuttonvalue='CONTINUE']",
                    "button:has-text('Continuar')",
                    "button:has-text('Continue')"
                },
                "continue");
            if (continueClicked)
            {
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
                await Task.Delay(_random.Next(1200, 2000), cancellationToken);
            }

            // Fill password (step 2)
            var passwordFilled = await TryFillPasswordAsync(page, password, cancellationToken);
            IElementHandle? passwordField = null;
            try
            {
                var passwordLabel = page.GetByLabel("Contraseña", new() { Exact = false });
                if (await passwordLabel.CountAsync() > 0)
                {
                    await passwordLabel.First.FillAsync(password);
                    _logger.LogInformation("Filled password field via label");
                    passwordFilled = true;
                }
            }
            catch
            {
                // Ignore label lookup failures and fall back to selector search.
            }

            passwordField = await page.QuerySelectorAsync(
                "input[type='password'], input[name*='password'], input[autocomplete='current-password']");
            
            if (passwordField != null && !passwordFilled)
            {
                await passwordField.FillAsync(password);
                _logger.LogInformation("Filled password field");
                await Task.Delay(_random.Next(800, 1500), cancellationToken);
            }
            else if (!passwordFilled && passwordField == null)
            {
                await LogInputSummaryAsync(page, "festo_login_password_inputs");
                _logger.LogWarning("Could not find password input field");
                await SaveDebugScreenshotAsync(page, "festo_login_missing_password");
            }

            // Submit login form - try multiple submit button selectors
            _logger.LogInformation("Looking for submit button...");
            var submitButton = page.Locator(
                "button[type='submit'], button:has-text('Sign in'), button:has-text('Login'), " +
                "button:has-text('Iniciar sesión'), button:has-text('Registrarse'), input[type='submit'], " +
                "[data-testid='login-submit'], button[name='submit']");
            
            if (await submitButton.CountAsync() > 0)
            {
                _logger.LogInformation("Found submit button, clicking...");
                await VerifyAndClickAsync(submitButton.First, page, "festo_login_submit", cancellationToken);
                
                // Wait for navigation with longer timeout
                try
                {
                    await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                }
                catch
                {
                    _logger.LogWarning("Network idle timeout after login submit");
                }
                
                await Task.Delay(_random.Next(3000, 5000), cancellationToken);
                _logger.LogInformation("Login completed, waiting for products page...");
            }
            else
            {
                var submitClicked = await TryClickButtonInFramesAsync(
                    page,
                    new[]
                    {
                        "#btnLogin",
                        "button[data-skbuttonvalue='LOGIN']",
                        "button[type='submit']",
                        "button:has-text('Sign in')",
                        "button:has-text('Login')",
                        "button:has-text('Iniciar sesión')",
                        "button:has-text('Registrarse')",
                        "input[type='submit']",
                        "[data-testid='login-submit']",
                        "button[name='submit']"
                    },
                    "submit");
                if (!submitClicked)
                {
                    _logger.LogWarning("No submit button found");
                    await SaveDebugScreenshotAsync(page, "festo_login_missing_submit");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Festo-specific login handling failed");
            throw;
        }
    }

    private async Task GenericLoginAsync(IPage page, string email, string password, CancellationToken cancellationToken)
    {
        try
        {
            // Generic login: find email/username and password fields
            var emailField = await page.QuerySelectorAsync("input[type='email'], input[name='email'], input[name='username']");
            if (emailField != null)
            {
                await emailField.FillAsync(email);
                await Task.Delay(_random.Next(500, 1000), cancellationToken);
            }

            var passwordField = await page.QuerySelectorAsync("input[type='password']");
            if (passwordField != null)
            {
                await passwordField.FillAsync(password);
                await Task.Delay(_random.Next(500, 1000), cancellationToken);
            }

            // Try to find and click submit button
            var submitButton = page.Locator("button[type='submit'], input[type='submit']");
            if (await submitButton.CountAsync() > 0)
            {
                await VerifyAndClickAsync(submitButton.First, page, "generic_login_submit", cancellationToken);
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await Task.Delay(_random.Next(2000, 3000), cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Generic login handling failed");
            throw;
        }
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
        if (_context != null)
        {
            await _context.CloseAsync();
            _context = null;
        }
        if (_browser != null)
        {
            await _browser.CloseAsync();
            _browser = null;
        }
        _playwright?.Dispose();
        _playwright = null;
    }

    private async Task SaveDebugScreenshotAsync(IPage page, string suffix)
    {
        try
        {
            var filename = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{suffix}.png";
            var screenshotPath = Path.Combine(Path.GetTempPath(), filename);
            await page.ScreenshotAsync(new() { Path = screenshotPath, FullPage = true });
            _logger.LogInformation("Saved debug screenshot to: {Path}", screenshotPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not save debug screenshot");
        }
    }

    private async Task SaveElementScreenshotAsync(ILocator locator, string suffix)
    {
        try
        {
            var filename = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{suffix}.png";
            var screenshotPath = Path.Combine(Path.GetTempPath(), filename);
            await locator.ScreenshotAsync(new() { Path = screenshotPath });
            _logger.LogInformation("Saved element screenshot to: {Path}", screenshotPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not save element screenshot");
        }
    }

    private async Task<bool> VerifyAndClickAsync(ILocator locator, IPage page, string label, CancellationToken cancellationToken = default)
    {
        try
        {
            await locator.ScrollIntoViewIfNeededAsync();
            var isVisible = await locator.IsVisibleAsync();
            var isEnabled = await locator.IsEnabledAsync();
            var box = await locator.BoundingBoxAsync();
            var text = await SafeReadLocatorTextAsync(locator);

            _logger.LogInformation(
                "Verify click target {Label}: visible={Visible} enabled={Enabled} box={Box} text={Text}",
                label,
                isVisible,
                isEnabled,
                box == null ? "null" : $"{box.Width:F0}x{box.Height:F0}",
                string.IsNullOrWhiteSpace(text) ? "(empty)" : text);

            if (!isVisible || !isEnabled || box == null || box.Width < 5 || box.Height < 5)
            {
                await SaveDebugScreenshotAsync(page, $"click_verify_{label}_not_ready");
                await SaveElementScreenshotAsync(locator, $"click_verify_{label}_element_not_ready");
                return false;
            }

            await HighlightLocatorAsync(locator);
            await SaveDebugScreenshotAsync(page, $"click_verify_{label}_before");
            await SaveElementScreenshotAsync(locator, $"click_verify_{label}_element_before");

            try
            {
                await locator.ClickAsync(new LocatorClickOptions { Timeout = 15000 });
            }
            catch
            {
                await locator.ClickAsync(new LocatorClickOptions { Timeout = 15000, Force = true });
            }

            await Task.Delay(_random.Next(300, 900), cancellationToken);
            await SaveDebugScreenshotAsync(page, $"click_verify_{label}_after");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "VerifyAndClick failed for {Label}", label);
            return false;
        }
    }

    private static async Task<string?> SafeReadLocatorTextAsync(ILocator locator)
    {
        try
        {
            var text = await locator.InnerTextAsync();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }
        }
        catch
        {
            // Ignore
        }

        try
        {
            var text = await locator.TextContentAsync();
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static async Task HighlightLocatorAsync(ILocator locator)
    {
        try
        {
            var handle = await locator.ElementHandleAsync();
            if (handle == null)
            {
                return;
            }

            await handle.EvaluateAsync(
                "el => { el.style.outline = '2px solid #ff9800'; el.style.outlineOffset = '2px'; }");
        }
        catch
        {
            // Ignore highlight failures
        }
    }

    private static async Task SaveDebugHtmlAsync(string html, string suffix)
    {
        try
        {
            var filename = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{suffix}.html";
            var htmlPath = Path.Combine(Path.GetTempPath(), filename);
            await File.WriteAllTextAsync(htmlPath, html, Encoding.UTF8);
        }
        catch
        {
            // Ignore debug write failures.
        }
    }

    private static async Task SaveDebugCardHtmlAsync(ILocator card, string suffix)
    {
        try
        {
            var handle = await card.ElementHandleAsync();
            if (handle == null)
            {
                return;
            }
            var html = await handle.EvaluateAsync<string>("el => el.outerHTML");
            await SaveDebugHtmlAsync(html, suffix);
        }
        catch
        {
            // Ignore debug write failures.
        }
    }

    private async Task AcceptCookiesAsync(IPage page, CancellationToken cancellationToken)
    {
        var selectors = new[]
        {
            "#didomi-notice-agree-button",
            "button:has-text('Aceptar todas las cookies')",
            "button:has-text('Aceptar solo lo necesario')"
        };

        foreach (var selector in selectors)
        {
            try
            {
                var locator = page.Locator(selector);
                if (await locator.CountAsync() > 0)
                {
                    await locator.First.ClickAsync();
                    await Task.Delay(_random.Next(500, 900), cancellationToken);
                    _logger.LogInformation("Accepted cookies with selector {Selector}", selector);
                    return;
                }
            }
            catch
            {
                // Ignore and try next selector.
            }
        }
    }

    private async Task<bool> WaitForLoginInputsAsync(IPage page, CancellationToken cancellationToken)
    {
        var selectors = new[]
        {
            "#username",
            "input[name*='username']",
            "input[type='email']",
            "input[name*='email']"
        };

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            foreach (var frame in page.Frames)
            {
                foreach (var selector in selectors)
                {
                    try
                    {
                        var locator = frame.Locator(selector);
                        if (await locator.CountAsync() > 0)
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        // Ignore and keep waiting.
                    }
                }
            }

            await Task.Delay(500, cancellationToken);
        }

        return false;
    }

    private async Task<bool> TryClickLoginLinkAsync(IPage page, CancellationToken cancellationToken)
    {
        var selectors = new[]
        {
            "[data-testid='navigation-login-link']",
            "button:has-text('Inicio de sesiÃ³n')",
            "a:has-text('Inicio de sesiÃ³n')"
        };

        foreach (var selector in selectors)
        {
            try
            {
                var locator = page.Locator(selector);
                if (await locator.CountAsync() > 0)
                {
                    var clicked = await VerifyAndClickAsync(locator.First, page, $"login_link_{selector}", cancellationToken);
                    if (clicked)
                    {
                        await Task.Delay(_random.Next(800, 1400), cancellationToken);
                        _logger.LogInformation("Clicked login link with selector {Selector}", selector);
                        return true;
                    }
                }
            }
            catch
            {
                // Ignore and try next selector.
            }
        }

        return false;
    }

    private async Task<bool> TryFillEmailAsync(IPage page, string email, CancellationToken cancellationToken)
    {
        var selectors = new[]
        {
            "#username",
            "input[type='email']",
            "input[name*='email']",
            "input[id*='email']",
            "input[name*='username']",
            "input[id*='username']",
            "input[autocomplete='username']"
        };

        return await TryFillInputInFramesAsync(page, selectors, email, "email", cancellationToken);
    }

    private async Task<bool> TryFillPasswordAsync(IPage page, string password, CancellationToken cancellationToken)
    {
        var selectors = new[]
        {
            "#password",
            "input[type='password']",
            "input[name*='password']",
            "input[id*='password']",
            "input[autocomplete='current-password']"
        };

        return await TryFillInputInFramesAsync(page, selectors, password, "password", cancellationToken);
    }

    private async Task<bool> TryFillInputInFramesAsync(
        IPage page,
        IEnumerable<string> selectors,
        string value,
        string label,
        CancellationToken cancellationToken)
    {
        foreach (var frame in page.Frames)
        {
            foreach (var selector in selectors)
            {
                try
                {
                    var locator = frame.Locator(selector);
                    if (await locator.CountAsync() > 0)
                    {
                        await locator.First.FillAsync(value);
                        _logger.LogInformation("Filled {Label} field in frame {FrameUrl} with selector {Selector}",
                            label, frame.Url, selector);
                        await Task.Delay(_random.Next(800, 1500), cancellationToken);
                        return true;
                    }
                }
                catch
                {
                    // Ignore and try next selector/frame.
                }
            }
        }

        return false;
    }

    private async Task<bool> TryClickButtonInFramesAsync(
        IPage page,
        IEnumerable<string> selectors,
        string label)
    {
        foreach (var frame in page.Frames)
        {
            foreach (var selector in selectors)
            {
                try
                {
                    var locator = frame.Locator(selector);
                    if (await locator.CountAsync() > 0)
                    {
                        var clicked = await VerifyAndClickAsync(locator.First, page, $"{label}_{selector}");
                        if (clicked)
                        {
                            _logger.LogInformation("Clicked {Label} button in frame {FrameUrl} with selector {Selector}",
                                label, frame.Url, selector);
                            return true;
                        }
                    }
                }
                catch
                {
                    // Ignore and try next selector/frame.
                }
            }
        }

        return false;
    }

    private async Task LogInputSummaryAsync(IPage page, string suffix)
    {
        try
        {
            var summary = await page.EvaluateAsync<string>(
                "JSON.stringify([...document.querySelectorAll('input')].map(i => ({type:i.type,name:i.name,id:i.id,placeholder:i.placeholder,ariaLabel:i.getAttribute('aria-label')})))");
            _logger.LogInformation("Input summary ({Suffix}): {Summary}", suffix, summary);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read input summary");
        }
    }

    private async Task<bool> IsLoggedInAsync(IPage page)
    {
        try
        {
            if (page.Url.Contains("auth.festo.com", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var loginLink = page.Locator("[data-testid='navigation-login-link'], button:has-text('Inicio de sesión'), a:has-text('Inicio de sesión')");
            if (await loginLink.CountAsync() > 0)
            {
                return false;
            }

            var userMenu = page.Locator(".js-account-widget, .account-menu, [data-testid='user-menu']");
            if (await userMenu.CountAsync() > 0)
            {
                return true;
            }
        }
        catch
        {
            // Ignore and fallback below.
        }

        return page.Url.Contains("festo.com/mx/es", StringComparison.OrdinalIgnoreCase) &&
            !page.Url.Contains("loginError", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<List<ScrapedProduct>> TryScrapeProductDetailWithVariationsAsync(
        IPage page,
        string startUrl,
        SiteSelectors selectors,
        CancellationToken cancellationToken)
    {
        var products = new List<ScrapedProduct>();
        var seenProducts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await page.GotoAsync(startUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 90000
        });
        await AcceptCookiesAsync(page, cancellationToken);

        var rootProduct = await ExtractProductFromDetailPageAsync(page, selectors, new List<string>());
        if (rootProduct != null && seenProducts.Add(GetProductKey(rootProduct)))
        {
            products.Add(rootProduct);
        }

        await ClickVariationsTabAsync(page, cancellationToken);
        var variationLinks = await FindVariationLinksAsync(page);
        foreach (var link in variationLinks)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            await page.GotoAsync(link, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 90000
            });
            await AcceptCookiesAsync(page, cancellationToken);
            var variationProduct = await ExtractProductFromDetailPageAsync(page, selectors, new List<string>());
            if (variationProduct != null && seenProducts.Add(GetProductKey(variationProduct)))
            {
                products.Add(variationProduct);
            }
        }

        return products;
    }

    private async Task ClickVariationsTabAsync(IPage page, CancellationToken cancellationToken)
    {
        var candidates = page.Locator("button:has-text('Variantes'), a:has-text('Variantes'), button:has-text('Variaciones'), a:has-text('Variaciones')");
        if (await candidates.CountAsync() > 0)
        {
            await candidates.First.ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            await Task.Delay(500, cancellationToken);
        }
    }

    private async Task<List<string>> FindVariationLinksAsync(IPage page)
    {
        var links = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var scoped = await page.EvaluateAsync<string[]>(
                """
                () => {
                    const keywords = ['Variantes', 'Variaciones', 'Variants', 'Varianten'];
                    const anchors = Array.from(document.querySelectorAll('a[href*="/p/"], a[href*="/a/"]'));
                    const isInVariantSection = (el) => {
                        let node = el;
                        for (let i = 0; i < 6 && node; i++, node = node.parentElement) {
                            const text = (node.textContent || '');
                            if (keywords.some(k => text.includes(k))) {
                                return true;
                            }
                        }
                        return false;
                    };
                    return anchors.filter(a => isInVariantSection(a)).map(a => a.href);
                }
                """);
            foreach (var link in scoped)
            {
                links.Add(link);
            }
        }
        catch
        {
            // Ignore and fallback to other selectors.
        }

        try
        {
            var fallback = page.Locator("a[href*='/p/'], a[href*='/a/']");
            var count = await fallback.CountAsync();
            for (var i = 0; i < count; i++)
            {
                var href = await fallback.Nth(i).GetAttributeAsync("href");
                if (!string.IsNullOrWhiteSpace(href))
                {
                    links.Add(href);
                }
            }
        }
        catch
        {
            // Ignore.
        }

        return links.ToList();
    }

    private static string GetProductKey(ScrapedProduct product)
    {
        if (!string.IsNullOrWhiteSpace(product.SkuSource))
        {
            return product.SkuSource.Trim();
        }

        if (!string.IsNullOrWhiteSpace(product.Title))
        {
            return product.Title.Trim();
        }

        return Guid.NewGuid().ToString();
    }

    private async Task WaitForProductDetailsAsync(IPage page)
    {
        var selectors = new[]
        {
            "h1",
            ".price-display-text--u5EEm",
            "text=Precio unitario",
            "text=Código de barras"
        };

        foreach (var selector in selectors)
        {
            try
            {
                await page.WaitForSelectorAsync(selector, new() { Timeout = 10000 });
                return;
            }
            catch
            {
                // Try next selector.
            }
        }
    }

    private async Task<string?> TryExtractBarcodeAsync(IPage page)
    {
        try
        {
            var label = page.GetByText("Código de barras", new() { Exact = false });
            if (await label.CountAsync() > 0)
            {
                var container = label.First.Locator("xpath=ancestor::div[1]");
                if (await container.CountAsync() > 0)
                {
                    var text = await container.First.InnerTextAsync();
                    var match = System.Text.RegularExpressions.Regex.Match(text, "\\b\\d{8,14}\\b");
                    if (match.Success)
                    {
                        return match.Value;
                    }
                }
            }
        }
        catch
        {
            // Ignore and fallback to content scan.
        }

        try
        {
            var content = await page.ContentAsync();
            var match = System.Text.RegularExpressions.Regex.Match(content, "\\b\\d{8,14}\\b");
            return match.Success ? match.Value : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<List<string>> TryExtractBreadcrumbAsync(IPage page)
    {
        var crumbs = new List<string>();
        try
        {
            var items = page.Locator("nav ol li, .breadcrumb li, .breadcrumb-item");
            var count = await items.CountAsync();
            for (var i = 0; i < count; i++)
            {
                var text = await items.Nth(i).InnerTextAsync();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    crumbs.Add(text.Trim());
                }
            }
        }
        catch
        {
            // Ignore and fallback.
        }

        return crumbs;
    }

    private static async Task<string?> GetDetailHrefFromCardAsync(ILocator card)
    {
        try
        {
            var link = card.Locator("a[href*='/p/'], a[href*='/a/'], a[href*='/product'], a[href*='/producto']");
            if (await link.CountAsync() > 0)
            {
                return await link.First.GetAttributeAsync("href");
            }

            var detailLink = card.Locator("a:has-text('Detalles'), button:has-text('Detalles')");
            if (await detailLink.CountAsync() > 0)
            {
                var href = await detailLink.First.GetAttributeAsync("href");
                if (!string.IsNullOrWhiteSpace(href))
                {
                    return href;
                }
            }

            var anyLink = card.Locator("a[href]");
            if (await anyLink.CountAsync() > 0)
            {
                var href = await anyLink.First.GetAttributeAsync("href");
                if (!string.IsNullOrWhiteSpace(href))
                {
                    return href;
                }
            }
        }
        catch
        {
            // Ignore and return null.
        }

        return null;
    }

    private static string NormalizeHref(string baseUrl, string href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return href;
        }

        if (Uri.TryCreate(href, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            try
            {
                return new Uri(baseUri, href).ToString();
            }
            catch
            {
                return href;
            }
        }

        return href;
    }

    private async Task EnsureProductsLandingAsync(IPage page, SiteProfile site, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(site.BaseUrl) &&
            !page.Url.StartsWith(site.BaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            await page.GotoAsync(site.BaseUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 90000
            });
            await AcceptCookiesAsync(page, cancellationToken);
        }

        var productsLink = page.Locator("a:has-text('Productos')");
        if (await productsLink.CountAsync() > 0)
        {
            await productsLink.First.ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            await AcceptCookiesAsync(page, cancellationToken);
        }
    }

    private async Task WaitForCategoryOrProductListAsync(IPage page, CancellationToken cancellationToken)
    {
        var selectors = new[]
        {
            "a[data-testid='category-tile']",
            ".single-product-container--oWOit",
            ".single-product-container",
            "h1"
        };

        for (var attempt = 0; attempt < 5 && !cancellationToken.IsCancellationRequested; attempt++)
        {
            foreach (var selector in selectors)
            {
                if (await page.Locator(selector).CountAsync() > 0)
                {
                    return;
                }
            }

            try
            {
                await page.Mouse.WheelAsync(0, 800);
                await Task.Delay(1000, cancellationToken);
                await page.Mouse.WheelAsync(0, -800);
            }
            catch
            {
                await Task.Delay(1000, cancellationToken);
            }
        }
    }

    private async Task WaitForManualLoginAsync(IPage page, SiteProfile site, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddMinutes(5);
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            var url = page.Url ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(site.BaseUrl) &&
                url.StartsWith(site.BaseUrl, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (url.Contains("festo.com/mx/es", StringComparison.OrdinalIgnoreCase) &&
                !url.Contains("auth.festo.com", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await Task.Delay(1000, cancellationToken);
        }
    }

    private async Task<List<ScrapedProduct>> TryScrapeCategoriesAsync(
        IPage page,
        SiteProfile site,
        SiteSelectors selectors,
        CancellationToken cancellationToken)
    {
        var products = new List<ScrapedProduct>();
        await WaitForCategoryOrProductListAsync(page, cancellationToken);
        var categoryTiles = await page.QuerySelectorAllAsync("a[data-testid='category-tile']");
        if (categoryTiles.Count == 0)
        {
            var productListFound = await page.QuerySelectorAsync(".single-product-container--oWOit, .single-product-container");
            if (productListFound != null)
            {
                var localSeenProducts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var localMaxProducts = site.MaxProductsPerScrape > 0 ? site.MaxProductsPerScrape : int.MaxValue;
                await CollectProductsFromListAsync(
                    page,
                    site.Id,
                    selectors,
                    products,
                    localSeenProducts,
                    localMaxProducts,
                    new List<string>(),
                    cancellationToken);
                return products;
            }

            await EnsureProductsLandingAsync(page, site, cancellationToken);
            await WaitForCategoryOrProductListAsync(page, cancellationToken);
            categoryTiles = await page.QuerySelectorAllAsync("a[data-testid='category-tile']");
            if (categoryTiles.Count == 0)
            {
                return products;
            }
        }

        var maxProducts = site.MaxProductsPerScrape > 0 ? site.MaxProductsPerScrape : int.MaxValue;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenProducts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await TraverseCategoryPageAsync(
            page,
            selectors,
            products,
            visited,
            seenProducts,
            maxProducts,
            0,
            new List<string>(),
            cancellationToken);
        return products;
    }

    private async Task TraverseCategoryPageAsync(
        IPage page,
        SiteSelectors selectors,
        List<ScrapedProduct> products,
        HashSet<string> visited,
        HashSet<string> seenProducts,
        int maxProducts,
        int depth,
        List<string> categoryPath,
        CancellationToken cancellationToken)
    {
        if (depth > 4 || products.Count >= maxProducts || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        await AcceptCookiesAsync(page, cancellationToken);
        await WaitForCategoryOrProductListAsync(page, cancellationToken);

        var productListFound = await page.QuerySelectorAsync(".single-product-container--oWOit, .single-product-container");
        if (productListFound != null)
        {
            await CollectProductsFromListAsync(
                page,
                site.Id,
                selectors,
                products,
                seenProducts,
                maxProducts,
                categoryPath,
                cancellationToken);
            return;
        }

        var categoryLinks = await page.QuerySelectorAllAsync("a[data-testid='category-tile']");
        var links = new List<(string Href, string? Name)>();
        foreach (var link in categoryLinks)
        {
            var href = await link.GetAttributeAsync("href");
            if (string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            var name = await GetCategoryNameAsync(link);
            links.Add((href, name));
        }

        foreach (var (href, name) in links)
        {
            if (products.Count >= maxProducts || cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (!visited.Add(href))
            {
                continue;
            }

            await page.GotoAsync(href, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 90000
            });
            await AcceptCookiesAsync(page, cancellationToken);

            var nextPath = new List<string>(categoryPath);
            if (!string.IsNullOrWhiteSpace(name))
            {
                nextPath.Add(name);
            }
            await TraverseCategoryPageAsync(
                page,
                selectors,
                products,
                visited,
                seenProducts,
                maxProducts,
                depth + 1,
                nextPath,
                cancellationToken);
        }
    }

    private async Task CollectProductsFromListAsync(
        IPage page,
        Guid siteId,
        SiteSelectors selectors,
        List<ScrapedProduct> products,
        HashSet<string> seenProducts,
        int maxProducts,
        List<string> categoryPath,
        CancellationToken cancellationToken)
    {
        var cards = page.Locator(".single-product-container--oWOit, .single-product-container");
        var count = await cards.CountAsync();
        for (var i = 0; i < count && products.Count < maxProducts; i++)
        {
            await _scrapeControl.WaitIfPausedAsync(siteId, cancellationToken);
            var card = cards.Nth(i);
            var detailButtonSelectors = new List<string>
            {
                "button:has-text('Detalles')",
                "[data-testid='button']:has-text('Detalles')",
                "[role='button']:has-text('Detalles')"
            };

            if (!string.IsNullOrWhiteSpace(selectors.DetailButtonText))
            {
                detailButtonSelectors.Add($"button:has-text('{selectors.DetailButtonText}')");
                detailButtonSelectors.Add($"[role='button']:has-text('{selectors.DetailButtonText}')");
            }

            if (!string.IsNullOrWhiteSpace(selectors.DetailButtonClassPrefix))
            {
                var prefixSelector = BuildClassPrefixSelector(selectors.DetailButtonClassPrefix);
                detailButtonSelectors.Add($"button{prefixSelector}");
                detailButtonSelectors.Add($"{prefixSelector}");
            }

            var detailButton = card.Locator(string.Join(", ", detailButtonSelectors.Distinct()));
            if (await detailButton.CountAsync() > 0)
            {
                var previousUrl = page.Url;
                await card.ScrollIntoViewIfNeededAsync();
                await card.HoverAsync();
                var button = detailButton.First;
                await button.ScrollIntoViewIfNeededAsync();
                try
                {
                    await VerifyAndClickAsync(button, page, "festo_detail_button", cancellationToken);
                }
                catch
                {
                    try
                    {
                        await button.ClickAsync(new LocatorClickOptions { Force = true, Timeout = 15000 });
                    }
                    catch
                    {
                        var handle = await button.ElementHandleAsync();
                        if (handle != null)
                        {
                            await page.EvaluateAsync("el => el.click()", handle);
                        }
                    }
                }
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
                await AcceptCookiesAsync(page, cancellationToken);

                if (page.Url == previousUrl)
                {
                    var detailHref = await GetDetailHrefFromCardAsync(card);
                    if (!string.IsNullOrWhiteSpace(detailHref))
                    {
                        detailHref = NormalizeHref(page.Url, detailHref);
                        await page.GotoAsync(detailHref, new PageGotoOptions
                        {
                            WaitUntil = WaitUntilState.DOMContentLoaded,
                            Timeout = 90000
                        });
                        await AcceptCookiesAsync(page, cancellationToken);
                    }
                    else
                    {
                        await SaveDebugScreenshotAsync(page, "festo_detail_click_no_nav");
                        await SaveDebugHtmlAsync(await page.ContentAsync(), "festo_detail_click_no_nav");
                        await SaveDebugCardHtmlAsync(card, "festo_detail_card_no_link");
                        var buttonTexts = await card.Locator("button").AllTextContentsAsync();
                        if (buttonTexts.Count > 0)
                        {
                            _logger.LogInformation("Card button texts: {Texts}", string.Join(" | ", buttonTexts));
                        }
                    }
                }

                var product = await ExtractProductFromDetailPageAsync(page, selectors, categoryPath);
                if (product != null)
                {
                    var key = GetProductKey(product);
                    if (seenProducts.Add(key))
                    {
                        products.Add(product);
                    }
                }

                await page.GoBackAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
                await AcceptCookiesAsync(page, cancellationToken);
                continue;
            }

            var link = card.Locator("a[href]");
            if (await link.CountAsync() > 0)
            {
                var href = await link.First.GetAttributeAsync("href");
                if (!string.IsNullOrWhiteSpace(href))
                {
                    href = NormalizeHref(page.Url, href);
                    var detailPage = await page.Context.NewPageAsync();
                    await detailPage.GotoAsync(href, new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = 90000
                    });
                    await AcceptCookiesAsync(detailPage, cancellationToken);
                    var product = await ExtractProductFromDetailPageAsync(detailPage, selectors, categoryPath);
                    if (product != null)
                    {
                        var key = GetProductKey(product);
                        if (seenProducts.Add(key))
                        {
                            products.Add(product);
                        }
                    }
                    await detailPage.CloseAsync();
                }
            }
            else
            {
                await SaveDebugCardHtmlAsync(card, "festo_card_no_link");
            }
        }
    }

    private async Task<ScrapedProduct?> ExtractProductFromDetailPageAsync(
        IPage page,
        SiteSelectors selectors,
        List<string> categoryPath)
    {
        try
        {
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            await WaitForProductDetailsAsync(page);

            var product = new ScrapedProduct
            {
                RawHtml = await page.ContentAsync(),
                ScrapedAt = DateTime.UtcNow
            };

            var titleLocator = page.Locator("h1");
            if (await titleLocator.CountAsync() > 0)
            {
                product.Title = (await titleLocator.First.InnerTextAsync())?.Trim();
            }
            product.Attributes["product_url"] = page.Url;

            var skuSelector = selectors.SkuSelector ?? ".part-number, .sku, [data-testid*='sku']";
            var skuEl = await page.QuerySelectorAsync(skuSelector);
            if (skuEl != null)
            {
                product.SkuSource = (await skuEl.InnerTextAsync())?.Trim();
            }

            var gtinEl = await page.QuerySelectorAsync("[class*='gtin--']");
            if (gtinEl != null)
            {
                var gtinText = (await gtinEl.InnerTextAsync())?.Trim();
                if (!string.IsNullOrWhiteSpace(gtinText))
                {
                    product.SkuSource = gtinText;
                }
            }

            var priceSelector = selectors.PriceSelector ?? ".price-display-text--u5EEm, .price-display-text, .price, .price-value, .product-price";
            var priceEl = await page.QuerySelectorAsync(priceSelector);
            if (priceEl != null)
            {
                var priceText = await priceEl.InnerTextAsync();
                product.Price = ParsePrice(priceText);
            }

            var orderCodeEl = await page.QuerySelectorAsync("[class*='order-code--']");
            var codeEl = await page.QuerySelectorAsync("[class*='code--']");
            var descriptionParts = new List<string>();
            if (orderCodeEl != null)
            {
                var orderCodeText = (await orderCodeEl.InnerTextAsync())?.Trim();
                if (!string.IsNullOrWhiteSpace(orderCodeText))
                {
                    descriptionParts.Add($"order-code: {orderCodeText}");
                }
            }
            if (codeEl != null)
            {
                var codeText = (await codeEl.InnerTextAsync())?.Trim();
                if (!string.IsNullOrWhiteSpace(codeText))
                {
                    descriptionParts.Add($"code: {codeText}");
                }
            }
            if (descriptionParts.Count > 0)
            {
                product.Description = string.Join(" | ", descriptionParts);
            }

            var barcode = await TryExtractBarcodeAsync(page);
            if (!string.IsNullOrWhiteSpace(barcode))
            {
                product.Attributes["barcode"] = barcode;
                if (string.IsNullOrWhiteSpace(product.SkuSource))
                {
                    product.SkuSource = barcode;
                }
            }
            if (string.IsNullOrWhiteSpace(product.SkuSource))
            {
                product.SkuSource = page.Url;
            }

            var imageEl = await page.QuerySelectorAsync("img");
            if (imageEl != null)
            {
                product.ImageUrl = await imageEl.GetAttributeAsync("src");
            }

            var breadcrumb = await TryExtractBreadcrumbAsync(page);
            if (breadcrumb.Count > 0)
            {
                categoryPath = breadcrumb;
            }

            if (categoryPath.Count > 0)
            {
                var pathText = string.Join(" > ", categoryPath);
                product.Category = pathText;
                product.Attributes["category_path"] = pathText;
            }

            var hasBarcode = product.Attributes.TryGetValue("barcode", out var barcodeValue) &&
                !string.IsNullOrWhiteSpace(barcodeValue);
            if (!product.Price.HasValue && !hasBarcode)
            {
                return null;
            }

            return product;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> GetCategoryNameAsync(IElementHandle link)
    {
        try
        {
            var name = await link.QuerySelectorAsync(".category-tile-details-name--UDx87");
            if (name != null)
            {
                return (await name.InnerTextAsync())?.Trim();
            }

            var fallback = await link.InnerTextAsync();
            return string.IsNullOrWhiteSpace(fallback) ? null : fallback.Trim();
        }
        catch
        {
            return null;
        }
    }

    private async Task<(IReadOnlyList<IElementHandle> Elements, string Selector)> TryFindProductElementsAsync(IPage page, SiteSelectors selectors)
    {
        var selectorList = new List<string>();

        if (!string.IsNullOrWhiteSpace(selectors.ProductListClassPrefix))
        {
            selectorList.Add(BuildClassPrefixSelector(selectors.ProductListClassPrefix));
        }

        if (!string.IsNullOrWhiteSpace(selectors.ProductCardClassPrefix))
        {
            selectorList.Add(BuildClassPrefixSelector(selectors.ProductCardClassPrefix));
        }

        selectorList.AddRange(new[]
        {
            ".result-list-item",
            ".product-list-item",
            ".product-item",
            ".tile",
            ".teaser",
            "article",
            "a[href*='/p/']"
        });

        foreach (var selector in selectorList)
        {
            try
            {
                var elements = await page.QuerySelectorAllAsync(selector);
                if (elements.Count > 0)
                {
                    return (elements, selector);
                }
            }
            catch
            {
                // Ignore and try next selector.
            }
        }

        return (Array.Empty<IElementHandle>(), string.Empty);
    }

    private static void FillSelectorsFromJson(SiteSelectors selectors, string selectorsJson)
    {
        if (string.IsNullOrWhiteSpace(selectorsJson))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(selectorsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var root = doc.RootElement;
            selectors.ProductListSelector ??= GetSelector(root, "ProductListSelector", "productListSelector", "product_list_selector");
            selectors.ProductListClassPrefix ??= GetSelector(root, "ProductListClassPrefix", "productListClassPrefix", "product_list_class_prefix");
            selectors.ProductCardClassPrefix ??= GetSelector(root, "ProductCardClassPrefix", "productCardClassPrefix", "product_card_class_prefix");
            selectors.TitleSelector ??= GetSelector(root, "TitleSelector", "titleSelector", "title_selector");
            selectors.PriceSelector ??= GetSelector(root, "PriceSelector", "priceSelector", "price_selector");
            selectors.SkuSelector ??= GetSelector(root, "SkuSelector", "skuSelector", "sku_selector");
            selectors.ImageSelector ??= GetSelector(root, "ImageSelector", "imageSelector", "image_selector");
            selectors.DescriptionSelector ??= GetSelector(root, "DescriptionSelector", "descriptionSelector", "description_selector");
            selectors.NextPageSelector ??= GetSelector(root, "NextPageSelector", "nextPageSelector", "next_page_selector");
            selectors.CategorySelector ??= GetSelector(root, "CategorySelector", "categorySelector", "category_selector");
            selectors.BrandSelector ??= GetSelector(root, "BrandSelector", "brandSelector", "brand_selector");
            selectors.ProductLinkSelector ??= GetSelector(root, "ProductLinkSelector", "productLinkSelector", "product_link_selector");
            selectors.DetailButtonText ??= GetSelector(root, "DetailButtonText", "detailButtonText", "detail_button_text");
            selectors.DetailButtonClassPrefix ??= GetSelector(root, "DetailButtonClassPrefix", "detailButtonClassPrefix", "detail_button_class_prefix");

            if (selectors.MaxPages <= 0)
            {
                if (TryGetInt(root, out var maxPages, "MaxPages", "maxPages", "max_pages"))
                {
                    selectors.MaxPages = maxPages;
                }
            }

            if (!selectors.UsesInfiniteScroll)
            {
                if (TryGetBool(root, out var usesInfinite, "UsesInfiniteScroll", "usesInfiniteScroll", "uses_infinite_scroll"))
                {
                    selectors.UsesInfiniteScroll = usesInfinite;
                }
            }
        }
        catch
        {
            // Ignore invalid selector JSON
        }
    }

    private static string? GetSelector(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return null;
    }

    private static bool TryGetInt(JsonElement root, out int result, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var value))
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out result))
                {
                    return true;
                }
                if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out result))
                {
                    return true;
                }
            }
        }

        result = 0;
        return false;
    }

    private static bool TryGetBool(JsonElement root, out bool result, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var value))
            {
                if (value.ValueKind == JsonValueKind.True)
                {
                    result = true;
                    return true;
                }
                if (value.ValueKind == JsonValueKind.False)
                {
                    result = false;
                    return true;
                }
                if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out result))
                {
                    return true;
                }
            }
        }

        result = false;
        return false;
    }

    private static string BuildClassPrefixSelector(string prefix)
    {
        var safe = prefix.Replace("'", "\\'");
        return $"[class^='{safe}'], [class*=' {safe}'], [class*='{safe}-']";
    }
    private string GetStorageStatePath(string siteName)
    {
        var safeName = string.Join("_", siteName.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(Path.GetTempPath(), $"scrapsae_{safeName}_state.json");
    }

    private async Task ManualLoginFallbackAsync(SiteProfile site, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Manual Login Fallback for {SiteName}...", site.Name);
        
        // Close existing headless browser
        await DisposeAsync();

        // Set env var to force headful for the next GetContextAsync call
        Environment.SetEnvironmentVariable("SCRAPSAE_MANUAL_LOGIN_ACTIVE", "true");
        
        try
        {
            var context = await GetContextAsync();
            var page = await context.NewPageAsync();
            
            var loginUrl = !string.IsNullOrEmpty(site.LoginUrl) ? site.LoginUrl : site.BaseUrl;
            await page.GotoAsync(loginUrl);
            
            _logger.LogInformation("=============================================================================");
            _logger.LogInformation("PLEASE LOG IN MANUALLY IN THE BROWSER WINDOW.");
            _logger.LogInformation("The script acts as a 'Logged In' detector. It waits for you.");
            _logger.LogInformation("=============================================================================");

            // Wait for user to login. logic: check for logged-in indicators every few seconds
            var maxWait = TimeSpan.FromMinutes(5);
            var startTime = DateTime.UtcNow;
            var loggedIn = false;

            while ((DateTime.UtcNow - startTime) < maxWait && !cancellationToken.IsCancellationRequested)
            {
                if (await IsLoggedInAsync(page))
                {
                    loggedIn = true;
                    break;
                }
                await Task.Delay(2000, cancellationToken);
            }

            if (loggedIn)
            {
                _logger.LogInformation("Manual login detected! Saving session state...");
                var statePath = GetStorageStatePath(site.Name);
                await context.StorageStateAsync(new() { Path = statePath });
                _logger.LogInformation("Session state saved to {Path}", statePath);
            }
            else
            {
                throw new Exception("Manual login timed out or failed.");
            }
        }
        finally
        {
            // Reset env var
            Environment.SetEnvironmentVariable("SCRAPSAE_MANUAL_LOGIN_ACTIVE", null);
            // We will dispose this headful browser in the caller or let it be re-used? 
            // Caller disposes it to reload with state in headless mode (optional, but cleaner).
        }
    }
}
