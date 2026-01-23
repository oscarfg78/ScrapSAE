using Microsoft.Playwright;
using Microsoft.Extensions.Logging;
using System.Text.Json;
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
    private readonly Random _random = new();

    public PlaywrightScrapingService(ILogger<PlaywrightScrapingService> logger)
    {
        _logger = logger;
    }

    private async Task<IBrowser> GetBrowserAsync()
    {
        if (_browser == null)
        {
            _playwright = await Playwright.CreateAsync();
            var headless = true;
            var headlessEnv = Environment.GetEnvironmentVariable("SCRAPSAE_HEADLESS");
            if (!string.IsNullOrWhiteSpace(headlessEnv) &&
                bool.TryParse(headlessEnv, out var parsedHeadless))
            {
                headless = parsedHeadless;
            }

            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = headless
            });
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
            if (string.IsNullOrWhiteSpace(selectors.ProductListSelector))
            {
                _logger.LogError("Missing ProductListSelector for site {SiteName}. SelectorsJson: {SelectorsJson}", site.Name, selectorsJson);
                return products;
            }

            var browser = await GetBrowserAsync();
            var context = await browser.NewContextAsync();
            var page = await context.NewPageAsync();

            var initialUrl = site.BaseUrl;
            if (site.RequiresLogin && !string.IsNullOrEmpty(site.LoginUrl))
            {
                initialUrl = site.LoginUrl;
            }

            // Navigate to site (or login URL if provided)
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
                _logger.LogInformation("Site {SiteName} requires login. Attempting to authenticate...", site.Name);
                await HandleLoginAsync(page, site, cancellationToken);
            }
            
            if (site.RequiresLogin && !string.IsNullOrEmpty(site.LoginUrl))
            {
                if (!page.Url.StartsWith(site.BaseUrl, StringComparison.OrdinalIgnoreCase))
                {
                    await page.GotoAsync(site.BaseUrl, new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = 90000
                    });
                    await AcceptCookiesAsync(page, cancellationToken);
                    await SaveDebugScreenshotAsync(page, $"{site.Name}_post_login");
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
                _logger.LogInformation("Scraping page {Page} of {Site}", currentPage, site.Name);
                
                // Human simulation: Random pause before starting page processing
                await Task.Delay(_random.Next(2000, 5000), cancellationToken);

                // Handle infinite scroll
                if (selectors.UsesInfiniteScroll)
                {
                    await ScrollToBottomAsync(page);
                }

                // Get product elements
                var productElements = await page.QuerySelectorAllAsync(selectors.ProductListSelector ?? "");
                var usedFallbackSelector = string.Empty;
                if (productElements.Count == 0)
                {
                    (productElements, usedFallbackSelector) = await TryFindProductElementsAsync(page);
                    if (productElements.Count > 0)
                    {
                        _logger.LogInformation("Fallback product selector matched: {Selector}", usedFallbackSelector);
                    }
                }
                
                foreach (var element in productElements)
                {
                    try
                    {
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
            var loginButton = await page.QuerySelectorAsync(
                "a[href*='login'], a[href*='signin'], a[href*='login.aspx'], " +
                "button:has-text('Login'), button:has-text('Sign in'), " +
                "a:has-text('Login'), a:has-text('Sign in'), a:has-text('Iniciar sesión'), " +
                "[data-testid='login-button']");
            
            if (loginButton != null)
            {
                _logger.LogInformation("Found login button, clicking...");
                await loginButton.ClickAsync();
                
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
            var submitButton = await page.QuerySelectorAsync(
                "button[type='submit'], button:has-text('Sign in'), button:has-text('Login'), " +
                "button:has-text('Iniciar sesión'), button:has-text('Registrarse'), input[type='submit'], " +
                "[data-testid='login-submit'], button[name='submit']");
            
            if (submitButton != null)
            {
                _logger.LogInformation("Found submit button, clicking...");
                await submitButton.ClickAsync();
                
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
            var submitButton = await page.QuerySelectorAsync("button[type='submit'], input[type='submit']");
            if (submitButton != null)
            {
                await submitButton.ClickAsync();
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
        if (_browser != null)
        {
            await _browser.CloseAsync();
        }
        _playwright?.Dispose();
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
                    await locator.First.ClickAsync();
                    await Task.Delay(_random.Next(800, 1400), cancellationToken);
                    _logger.LogInformation("Clicked login link with selector {Selector}", selector);
                    return true;
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
                        await locator.First.ClickAsync();
                        _logger.LogInformation("Clicked {Label} button in frame {FrameUrl} with selector {Selector}",
                            label, frame.Url, selector);
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

    private async Task<List<ScrapedProduct>> TryScrapeCategoriesAsync(
        IPage page,
        SiteProfile site,
        SiteSelectors selectors,
        CancellationToken cancellationToken)
    {
        var products = new List<ScrapedProduct>();
        var categoryTiles = await page.QuerySelectorAllAsync("a[data-testid='category-tile']");
        if (categoryTiles.Count == 0)
        {
            return products;
        }

        var maxProducts = site.MaxProductsPerScrape > 0 ? site.MaxProductsPerScrape : int.MaxValue;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await TraverseCategoryPageAsync(page, selectors, products, visited, maxProducts, 0, cancellationToken);
        return products;
    }

    private async Task TraverseCategoryPageAsync(
        IPage page,
        SiteSelectors selectors,
        List<ScrapedProduct> products,
        HashSet<string> visited,
        int maxProducts,
        int depth,
        CancellationToken cancellationToken)
    {
        if (depth > 4 || products.Count >= maxProducts || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        await AcceptCookiesAsync(page, cancellationToken);

        var productListFound = await page.QuerySelectorAsync(".single-product-container--oWOit, .single-product-container");
        if (productListFound != null)
        {
            await CollectProductsFromListAsync(page, selectors, products, maxProducts, cancellationToken);
            return;
        }

        var categoryLinks = await page.QuerySelectorAllAsync("a[data-testid='category-tile']");
        foreach (var link in categoryLinks)
        {
            if (products.Count >= maxProducts || cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var href = await link.GetAttributeAsync("href");
            if (string.IsNullOrWhiteSpace(href) || !visited.Add(href))
            {
                continue;
            }

            await page.GotoAsync(href, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 90000
            });

            await TraverseCategoryPageAsync(page, selectors, products, visited, maxProducts, depth + 1, cancellationToken);
        }
    }

    private async Task CollectProductsFromListAsync(
        IPage page,
        SiteSelectors selectors,
        List<ScrapedProduct> products,
        int maxProducts,
        CancellationToken cancellationToken)
    {
        var cards = page.Locator(".single-product-container--oWOit, .single-product-container");
        var count = await cards.CountAsync();
        for (var i = 0; i < count && products.Count < maxProducts; i++)
        {
            var card = cards.Nth(i);
            var detailButton = card.Locator("button.product-details-link--vVn1R, button:has-text('Detalles')");
            if (await detailButton.CountAsync() > 0)
            {
                await detailButton.First.ClickAsync();
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
                await AcceptCookiesAsync(page, cancellationToken);

                var product = await ExtractProductFromDetailPageAsync(page, selectors);
                if (product != null)
                {
                    products.Add(product);
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
                    var detailPage = await page.Context.NewPageAsync();
                    await detailPage.GotoAsync(href, new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = 90000
                    });
                    await AcceptCookiesAsync(detailPage, cancellationToken);
                    var product = await ExtractProductFromDetailPageAsync(detailPage, selectors);
                    if (product != null)
                    {
                        products.Add(product);
                    }
                    await detailPage.CloseAsync();
                }
            }
        }
    }

    private async Task<ScrapedProduct?> ExtractProductFromDetailPageAsync(IPage page, SiteSelectors selectors)
    {
        try
        {
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

            var skuSelector = selectors.SkuSelector ?? ".part-number, .sku, [data-testid*='sku']";
            var skuEl = await page.QuerySelectorAsync(skuSelector);
            if (skuEl != null)
            {
                product.SkuSource = (await skuEl.InnerTextAsync())?.Trim();
            }

            var priceSelector = selectors.PriceSelector ?? ".price, .price-value, .product-price";
            var priceEl = await page.QuerySelectorAsync(priceSelector);
            if (priceEl != null)
            {
                var priceText = await priceEl.InnerTextAsync();
                product.Price = ParsePrice(priceText);
            }

            var imageEl = await page.QuerySelectorAsync("img");
            if (imageEl != null)
            {
                product.ImageUrl = await imageEl.GetAttributeAsync("src");
            }

            return product;
        }
        catch
        {
            return null;
        }
    }

    private async Task<(IReadOnlyList<IElementHandle> Elements, string Selector)> TryFindProductElementsAsync(IPage page)
    {
        var selectors = new[]
        {
            ".result-list-item",
            ".product-list-item",
            ".product-item",
            ".tile",
            ".teaser",
            "article",
            "a[href*='/p/']"
        };

        foreach (var selector in selectors)
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
            selectors.TitleSelector ??= GetSelector(root, "TitleSelector", "titleSelector", "title_selector");
            selectors.PriceSelector ??= GetSelector(root, "PriceSelector", "priceSelector", "price_selector");
            selectors.SkuSelector ??= GetSelector(root, "SkuSelector", "skuSelector", "sku_selector");
            selectors.ImageSelector ??= GetSelector(root, "ImageSelector", "imageSelector", "image_selector");
            selectors.DescriptionSelector ??= GetSelector(root, "DescriptionSelector", "descriptionSelector", "description_selector");
            selectors.NextPageSelector ??= GetSelector(root, "NextPageSelector", "nextPageSelector", "next_page_selector");
            selectors.CategorySelector ??= GetSelector(root, "CategorySelector", "categorySelector", "category_selector");
            selectors.BrandSelector ??= GetSelector(root, "BrandSelector", "brandSelector", "brand_selector");
            selectors.ProductLinkSelector ??= GetSelector(root, "ProductLinkSelector", "productLinkSelector", "product_link_selector");

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
}
