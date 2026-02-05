using Microsoft.Playwright;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Interfaces;
using ScrapSAE.Infrastructure.AI;
using ScrapSAE.Infrastructure.Services;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScrapSAE.Infrastructure.Scraping;

/// <summary>
/// Servicio de web scraping usando Playwright
/// </summary>
public partial class PlaywrightScrapingService : IScrapingService, IAsyncDisposable
{
    private const string ScreenshotDirectoryName = "scrapsae-screens";
    // Regex para detectar URLs de datasheets de Festo
    // Ejemplo: https://www.festo.com/mx/es/a/download-document/datasheet/195555
    private static readonly Regex _datasheetUrlRegex = new(@"download-document/datasheet/(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    private readonly ILogger<PlaywrightScrapingService> _logger;
    private readonly IBrowserSharingService _browserSharing;
    private readonly IScrapingSignalService _signalService;
    private readonly IScrapeControlService _scrapeControl;
    private readonly IPostExecutionAnalyzer _analyzer;
    private readonly IAIProcessorService _aiProcessor;
    private readonly ISyncLogService _syncLogService;
    private readonly ITelemetryService _telemetryService;
    private readonly ScrapingProcessManager _processManager;
    private readonly List<SiteProfile> _sites; // Cache simple for testing
    private IPlaywright? _playwright; // Only for persistent context overrides
    private IBrowserContext? _context;
    private bool _isPersistentContext;
    private readonly Random _random = new();

    // Cache local de productos (URL -> ScrapedProduct) para evitar re-procesar en la misma ejecución
    private readonly ConcurrentDictionary<string, ScrapedProduct> _localProductCache = new();

    public PlaywrightScrapingService(
        ILogger<PlaywrightScrapingService> logger, 
        IBrowserSharingService browserSharing,
        IScrapingSignalService signalService,
        IScrapeControlService scrapeControl,
        IPostExecutionAnalyzer analyzer,
        IAIProcessorService aiProcessor,
        ISyncLogService syncLogService,
        ITelemetryService telemetryService,
        ScrapingProcessManager processManager)
    {
        _logger = logger;
        _browserSharing = browserSharing;
        _signalService = signalService;
        _scrapeControl = scrapeControl;
        _analyzer = analyzer;
        _aiProcessor = aiProcessor;
        _syncLogService = syncLogService;
        _telemetryService = telemetryService;
        _processManager = processManager;
        _sites = new List<SiteProfile>(); // Initialize empty
    }


    private async Task<IBrowserContext> GetContextAsync(BrowserNewContextOptions? options = null)
    {
        var manualLoginEnv = Environment.GetEnvironmentVariable("SCRAPSAE_MANUAL_LOGIN");
        var forceManualLoginEnv = Environment.GetEnvironmentVariable("SCRAPSAE_FORCE_MANUAL_LOGIN");
        var festoManualLoginEnv = Environment.GetEnvironmentVariable("SCRAPSAE_MANUAL_LOGIN_FESTO");
        var manualEnv = Environment.GetEnvironmentVariable("SCRAPSAE_MANUAL_LOGIN_ACTIVE");
        var headlessEnv = Environment.GetEnvironmentVariable("SCRAPSAE_HEADLESS");
        
        var shouldBeHeadless = true;
        if (!string.IsNullOrWhiteSpace(headlessEnv) && bool.TryParse(headlessEnv, out var parsedHeadless))
        {
            shouldBeHeadless = parsedHeadless;
        }

        if ((!string.IsNullOrWhiteSpace(manualEnv) && manualEnv == "true") ||
            (!string.IsNullOrWhiteSpace(manualLoginEnv) && bool.TryParse(manualLoginEnv, out var manualLogin) && manualLogin) ||
            (!string.IsNullOrWhiteSpace(forceManualLoginEnv) && bool.TryParse(forceManualLoginEnv, out var forceManual) && forceManual) ||
            (!string.IsNullOrWhiteSpace(festoManualLoginEnv) && bool.TryParse(festoManualLoginEnv, out var forceFesto) && forceFesto))
        {
            shouldBeHeadless = false;
        }

        _logger.LogInformation("[DEBUG] GetContextAsync: manualLoginEnv={Manual}, forceManual={Force}, headlessEnv={Headless}", manualLoginEnv, forceManualLoginEnv, headlessEnv);
        _logger.LogInformation("[DEBUG] GetContextAsync: shouldBeHeadless calculated as {ShouldBeHeadless}", shouldBeHeadless);

        if (_context != null)
        {
             return _context;
        }

        var shouldUseSharedBrowser = true;
        var userDataDir = Environment.GetEnvironmentVariable("SCRAPSAE_PROFILE_DIR");
        
        if (!string.IsNullOrWhiteSpace(userDataDir))
        {
            shouldUseSharedBrowser = false;
        }

        // Configuración stealth básica
        var contextOptions = options ?? new BrowserNewContextOptions();
        PrepareContextOptions(contextOptions);

        if (shouldUseSharedBrowser)
        {
            var browser = await _browserSharing.GetBrowserAsync();
            _context = await browser.NewContextAsync(contextOptions);
        }
        else
        {
             // Fallback to local persistent context (rare case)
             _playwright ??= await Playwright.CreateAsync();
             
             var persistentOptions = new BrowserTypeLaunchPersistentContextOptions
             {
                 Headless = shouldBeHeadless,
                 UserAgent = contextOptions.UserAgent,
                 ViewportSize = contextOptions.ViewportSize,
                 Locale = contextOptions.Locale,
                 TimezoneId = contextOptions.TimezoneId,
                 ExtraHTTPHeaders = contextOptions.ExtraHTTPHeaders
             };
             persistentOptions.Args = new[] { "--disable-blink-features=AutomationControlled" };

             _context = await _playwright.Chromium.LaunchPersistentContextAsync(userDataDir ?? string.Empty, persistentOptions);
             _isPersistentContext = true;
        }
        
        await ApplyStealthScriptsAsync(_context);
        return _context;
    }

    private void PrepareContextOptions(BrowserNewContextOptions contextOptions)
    {
        // User-Agent realista y rotado
        if (string.IsNullOrEmpty(contextOptions.UserAgent))
        {
            var userAgents = new[]
            {
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/119.0.0.0 Safari/537.36",
                "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
            };
            contextOptions.UserAgent = userAgents[_random.Next(userAgents.Length)];
        }
        
        // Viewport realista y ligeramente variable
        if (contextOptions.ViewportSize == null)
        {
            var viewports = new[]
            {
                new ViewportSize { Width = 1920, Height = 1080 },
                new ViewportSize { Width = 1366, Height = 768 },
                new ViewportSize { Width = 1536, Height = 864 },
                new ViewportSize { Width = 1440, Height = 900 }
            };
            contextOptions.ViewportSize = viewports[_random.Next(viewports.Length)];
        }

        // Locale y timezone
        if (string.IsNullOrEmpty(contextOptions.Locale))
        {
            contextOptions.Locale = "es-MX";
        }
        if (string.IsNullOrEmpty(contextOptions.TimezoneId))
        {
            contextOptions.TimezoneId = "America/Mexico_City";
        }
        
        // Headers HTTP realistas
        if (contextOptions.ExtraHTTPHeaders == null)
        {
            contextOptions.ExtraHTTPHeaders = new Dictionary<string, string>
            {
                ["Accept-Language"] = "es-MX,es;q=0.9,en;q=0.8",
                ["DNT"] = "1"
            // Removed manual Sec-Fetch-* and Accept headers as they break API subrequests by overriding browser defaults incorrectly.
        };
        }

        // Security & Permissions - Fix for blank pages/CORS errors
        contextOptions.BypassCSP = true;
        contextOptions.IgnoreHTTPSErrors = true;
        contextOptions.Permissions = new[] { "geolocation" };
        contextOptions.Geolocation = new Geolocation { Latitude = 19.4326f, Longitude = -99.1332f }; // Mexico City
    }

    private async Task ApplyStealthScriptsAsync(IBrowserContext context)
    {
        // Inyectar script stealth
        var stealthScriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "stealth_script.js");
        if (!File.Exists(stealthScriptPath))
        {
            // Probar en el directorio raíz del proyecto (útil en desarrollo)
            var rootPath = Path.Combine(Directory.GetCurrentDirectory(), "stealth_script.js");
            if (File.Exists(rootPath))
            {
                stealthScriptPath = rootPath;
            }
            else 
            {
                // Fallback a ruta por defecto del sistema si existe
                var linuxPath = "/home/ubuntu/ScrapSAE/stealth_script.js";
                if (File.Exists(linuxPath))
                {
                    stealthScriptPath = linuxPath;
                }
            }
        }

        if (File.Exists(stealthScriptPath))
        {
            var stealthScript = await File.ReadAllTextAsync(stealthScriptPath);
            await context.AddInitScriptAsync(stealthScript);
            _logger.LogInformation("🥷 Stealth mode activated using script at {Path}", stealthScriptPath);
        }
        else
        {
            _logger.LogWarning("Stealth script not found. Searched in: {BaseDir} and {CurrDir}", 
                AppDomain.CurrentDomain.BaseDirectory, Directory.GetCurrentDirectory());
        }
    }

    public async Task<IEnumerable<ScrapedProduct>> ScrapeAsync(SiteProfile site, CancellationToken cancellationToken = default)
    {
        var products = new List<ScrapedProduct>();
        
        // Verificar si hay URLs directas para inspeccionar (modo de inspección manual)
        var directUrlsJson = Environment.GetEnvironmentVariable("SCRAPSAE_DIRECT_URLS");
        if (!string.IsNullOrEmpty(directUrlsJson))
        {
            _logger.LogInformation("Modo de inspección directa de URLs activado");
            var urls = System.Text.Json.JsonSerializer.Deserialize<List<string>>(directUrlsJson) ?? new List<string>();
            return await ScrapeDirectUrlsAsync(urls, site.Id, false, cancellationToken);
        }
        
        // Verificar si hay URLs aprendidas (modo automático con aprendizaje)
        var learnedUrlsJson = Environment.GetEnvironmentVariable("SCRAPSAE_LEARNED_URLS");
        if (!string.IsNullOrEmpty(learnedUrlsJson))
        {
            _logger.LogInformation("Modo de scraping con URLs aprendidas activado");
            await LogStepAsync(site.Id, "info", "Usando URLs aprendidas para scraping", null);
            var urls = System.Text.Json.JsonSerializer.Deserialize<List<string>>(learnedUrlsJson) ?? new List<string>();
            return await ScrapeDirectUrlsAsync(urls, site.Id, false, cancellationToken);
        }
        
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
            
            // Validar selectores según el modo
            var scrapingMode = selectors.ScrapingMode?.ToLower() ?? "traditional";
            _logger.LogInformation("[DEBUG] Validating selectors. Mode: {Mode}", scrapingMode);
            
            if (scrapingMode != "families" && string.IsNullOrWhiteSpace(selectors.ProductListSelector))
            {
                // Si el nombre es Festo, forzar modo familias si no hay selector de lista
                if (site.Name.Contains("Festo", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("[DEBUG] Site is Festo but mode is traditional and ProductListSelector is missing. Defaulting to families mode.");
                    scrapingMode = "families";
                    selectors.ScrapingMode = "families";
                }
                else
                {
                    _logger.LogError("Missing ProductListSelector for site {SiteName} in traditional mode. SelectorsJson: {SelectorsJson}", site.Name, selectorsJson);
                    return products;
                }
            }

            var storageStatePath = GetStorageStatePath(site.Name);
            var contextOptions = new BrowserNewContextOptions();
            
            // Si sabemos de antemano que vamos a forzar manual login, 
            // establecemos la variable de entorno para que GetContextAsync abra headful inmediatamente
            var forceManualLoginEnv = Environment.GetEnvironmentVariable("SCRAPSAE_FORCE_MANUAL_LOGIN");
            var isForcedManual = !string.IsNullOrWhiteSpace(forceManualLoginEnv) &&
                                 bool.TryParse(forceManualLoginEnv, out var forceManual) &&
                                 forceManual;
                                 
            if (isForcedManual)
            {
                Environment.SetEnvironmentVariable("SCRAPSAE_MANUAL_LOGIN_ACTIVE", "true");
            }

            if (File.Exists(storageStatePath))
            {
                _logger.LogInformation("Found saved storage state for {SiteName}, loading session...", site.Name);
                contextOptions.StorageStatePath = storageStatePath;
            }

            var context = await GetContextAsync(contextOptions);
            IPage page;
            try
            {
                page = await context.NewPageAsync();
            }
            catch (Exception ex) when (ex.GetType().Name == "TargetClosedException" || ex.Message.Contains("closed"))
            {
                _logger.LogWarning("Browser context closed unexpectedly. Re-initializing context...");
                _context = null; 
                context = await GetContextAsync(contextOptions);
                page = await context.NewPageAsync();
            }

            var initialUrl = site.BaseUrl;
            if (site.RequiresLogin && !string.IsNullOrEmpty(site.LoginUrl))
            {
                initialUrl = site.LoginUrl;
            }

            // Navigate to site (or login URL if provided)
            await _scrapeControl.WaitIfPausedAsync(site.Id, cancellationToken);
            await LogStepAsync(site.Id, "info", "Navegando a URL inicial.", new { url = initialUrl });
            await page.GotoAsync(initialUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 90000
            });
            await AcceptCookiesAsync(page, cancellationToken);
            var initialShot = await SaveStepScreenshotAsync(page, $"{site.Name}_initial");
            await LogStepAsync(site.Id, "success", "Pagina inicial cargada.", new { url = page.Url, screenshotFile = initialShot });

            // Handle login if required
            if (site.RequiresLogin && !string.IsNullOrEmpty(site.CredentialsEncrypted))
            {
                if (isForcedManual)
                {
                    _logger.LogInformation("Force manual login enabled for site {SiteName}.", site.Name);
                    await LogStepAsync(site.Id, "info", "Forzando login manual.");
                    // Reutilizar el page y context actuales
                    await ManualLoginFallbackInExistingPageAsync(page, site, cancellationToken);
                }
                else
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
                            await LogStepAsync(site.Id, "warn", "Login automatico fallo. Iniciando login manual.");
                            // Reutilizar el page y context actuales
                            await ManualLoginFallbackInExistingPageAsync(page, site, cancellationToken);
                        }
                    }
                    else
                    {
                        _logger.LogInformation("Detected existing login session for site {SiteName}", site.Name);
                        await LogStepAsync(site.Id, "success", "Sesion activa detectada.");
                    }
                }
            }
            
            if (site.RequiresLogin && !string.IsNullOrEmpty(site.LoginUrl))
            {
                // Solo re-navegar si no estamos ya en una página que parece ser del sitio base
                // y no acabamos de hacer un login manual exitoso (que ya nos dejó en la landing)
                if (!page.Url.Contains(site.BaseUrl, StringComparison.OrdinalIgnoreCase) && 
                    !page.Url.Contains("festo.com", StringComparison.OrdinalIgnoreCase))
                {
                    await _scrapeControl.WaitIfPausedAsync(site.Id, cancellationToken);
                    _logger.LogInformation("Redirecting to base URL: {Url}", site.BaseUrl);
                    await page.GotoAsync(site.BaseUrl, new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = 90000
                    });
                    await AcceptCookiesAsync(page, cancellationToken);
                    await SaveDebugScreenshotAsync(page, $"{site.Name}_post_login");
                }
            }
            
            try
            {
                await SaveDebugHtmlAsync(await page.ContentAsync(), $"{site.Name}_after_login_check");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not save debug HTML after login check (page navigating?): {Message}", ex.Message);
            }

            var startUrl = Environment.GetEnvironmentVariable("SCRAPSAE_START_URL");
            if (!string.IsNullOrWhiteSpace(startUrl))
            {
                _logger.LogInformation("Using start URL override: {StartUrl}", startUrl);
                await LogStepAsync(site.Id, "info", "Usando URL de inicio forzada.", new { url = startUrl });
                await _scrapeControl.WaitIfPausedAsync(site.Id, cancellationToken);
                var startProducts = await TryScrapeProductDetailWithVariationsAsync(
                    page,
                    startUrl,
                    selectors,
                    cancellationToken);
                if (startProducts.Count > 0)
                {
                    await LogStepAsync(site.Id, "success", "Scraping por URL directa completado.", new { count = startProducts.Count });
                    return startProducts;
                }
            }

            // --- GANCHO ESPECÍFICO PARA FESTO (MODO HYBRID PROCESS MANAGER) ---
            if (site.Name.Contains("Festo", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Activando ScrapingProcessManager (Estrategia Híbrida) para Festo...");
                
                // Definir Callbacks para el Manager
                
                // 1. Callback de Categoría (List Navigation)
                Func<Task<List<ScrapedProduct>>> categoryCallback = async () => 
                {
                    var listProducts = new List<ScrapedProduct>();
                    var seen = new HashSet<string>();
                    // Usamos la lógica existente de navegación recursiva pero envuelta
                    await NavigateAndCollectFromSubcategoriesAsync(
                        page, site.Id, selectors, listProducts, seen, 
                        site.MaxProductsPerScrape > 0 ? site.MaxProductsPerScrape : 100, 
                        new List<string>(), cancellationToken);
                    return listProducts;
                };

                // 2. Callback de Producto (Extraction)
                Func<Task<List<ScrapedProduct>>> productCallback = async () =>
                {
                    return await ExtractFestoProductsFromDetailPageAsync(page, selectors, new List<string>(), cancellationToken);
                };

                // 3. Callback de Descubrimiento (Deep Discovery)
                Func<Task<List<string>>> discoveryCallback = async () =>
                {
                    return await DiscoverRelatedProductUrlsAsync(page, cancellationToken);
                };

                // 4. Logger Step Wrapper
                Func<string, Task> stepLogger = async (msg) => 
                {
                    await LogStepAsync(site.Id, "info", $"[ProcessManager] {msg}", null);
                };

                // Ejecutar Estrategia Híbrida
                var hybridResults = await _processManager.ExecuteHybridStrategyAsync(
                    page, 
                    site, 
                    selectors, 
                    categoryCallback, 
                    productCallback, 
                    discoveryCallback, 
                    stepLogger, 
                    cancellationToken
                );

                if (hybridResults.Count > 0)
                {
                    await LogStepAsync(site.Id, "success", $"Scraping Híbrido completado. Total: {hybridResults.Count}", new { count = hybridResults.Count });
                    return hybridResults;
                }
                else
                {
                    await LogStepAsync(site.Id, "warning", "Scraping Híbrido finalizó sin productos.", null);
                    // Retornar lista vacía pero procesada
                    return new List<ScrapedProduct>();
                }
            }
            // --- FIN GANCHO FESTO ---

            // Detectar el modo de scraping y usar el método apropiado
            await LogStepAsync(site.Id, "info", $"Modo de scraping detectado: {scrapingMode}", null);
            
            List<ScrapedProduct> searchProducts;
            if (scrapingMode == "families")
            {
                // Modo families (Festo-style): Navegar a categorías y extraer familias
                var seenProducts = new HashSet<string>();
                var maxProducts = site.MaxProductsPerScrape > 0 ? site.MaxProductsPerScrape : 100;
                searchProducts = new List<ScrapedProduct>();
                
                var success = await TryScrapeFamiliesModeAsync(
                    page,
                    site,
                    selectors,
                    searchProducts,
                    seenProducts,
                    maxProducts,
                    cancellationToken);
                    
                if (success && searchProducts.Count > 0)
                {
                    await LogStepAsync(site.Id, "success", "Scraping en modo families completado.", new { count = searchProducts.Count });
                    return searchProducts;
                }
            }
            else
            {
                // Modo tradicional: Búsqueda por categorías
                searchProducts = await TryScrapeCategorySearchAsync(page, site, selectors, cancellationToken);
                if (searchProducts.Count > 0)
                {
                    await LogStepAsync(site.Id, "success", "Scraping por busqueda de categorias completado.", new { count = searchProducts.Count });
                    return searchProducts;
                }
            }

            var categoryProducts = await TryScrapeCategoriesAsync(page, site, selectors, cancellationToken);
            if (categoryProducts.Count > 0)
            {
                await LogStepAsync(site.Id, "success", "Scraping por navegacion de categorias completado.", new { count = categoryProducts.Count });
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
                var productElements = new List<IElementHandle>();
                if (!string.IsNullOrEmpty(selectors.ProductListSelector))
                {
                    productElements = (await page.QuerySelectorAllAsync(selectors.ProductListSelector)).ToList();
                }

                var usedFallbackSelector = string.Empty;
                if (productElements.Count == 0)
                {
                    var (fallbackElements, fallbackSelector) = await TryFindProductElementsAsync(page, selectors);
                    productElements = fallbackElements.ToList();
                    usedFallbackSelector = fallbackSelector;
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
            string email = string.Empty;
            string password = string.Empty;

            if (site.CredentialsEncrypted != null && site.CredentialsEncrypted.Trim().StartsWith("{"))
            {
                try
                {
                    var credsObj = Newtonsoft.Json.Linq.JObject.Parse(site.CredentialsEncrypted);
                    email = credsObj["username"]?.ToString() ?? credsObj["email"]?.ToString() ?? string.Empty;
                    password = credsObj["password"]?.ToString() ?? string.Empty;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse JSON credentials for site {SiteName}", site.Name);
                }
            }

            if (string.IsNullOrEmpty(email) && site.CredentialsEncrypted != null)
            {
                var credentials = site.CredentialsEncrypted.Split('|');
                if (credentials.Length >= 2)
                {
                    email = credentials[0].Trim();
                    password = credentials[1].Trim();
                }
            }

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                _logger.LogWarning("Invalid credentials format or empty credentials for site {SiteName}. Credentials string length: {Length}", site.Name, site.CredentialsEncrypted?.Length ?? 0);
                return;
            }

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
                await HighlightLocatorAsync(emailField);
                // Usar simulación de escritura humana
                await SimulateTypingAsync(emailField, email);
                _logger.LogInformation("Email ingresado correctamente");
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
                // Wait for password field to appear
                try
                {
                    await page.WaitForSelectorAsync("input[type='password']", new() { Timeout = 10000 });
                    _logger.LogInformation("Password field appeared after clicking Continue");
                }
                catch
                {
                    _logger.LogWarning("Password field did not appear within timeout");
                }
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
                await HighlightLocatorAsync(passwordField);
                // Usar simulación de escritura humana para password
                await SimulateTypingAsync(passwordField, password);
                _logger.LogInformation("Password ingresado correctamente");
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
                
                // Wait for a logged-in indicator to confirm successful login
                try
                {
                    await page.WaitForSelectorAsync("button:has-text('MiFesto'), [data-testid='user-menu'], .user-menu", new() { Timeout = 20000 });
                    _logger.LogInformation("Login verified - user menu detected");
                }
                catch
                {
                    _logger.LogWarning("Could not verify login - user menu not detected within timeout");
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

        // Validar si la extracción fue exitosa
        var isValid = !string.IsNullOrEmpty(product.Title) && (product.Price.HasValue || !string.IsNullOrEmpty(product.SkuSource));
        
        // Fallback con IA si está habilitado y la extracción falló
        var useFallback = Environment.GetEnvironmentVariable("SCRAPSAE_SCREENSHOT_FALLBACK") == "true";
        if (!isValid && useFallback)
        {
            _logger.LogInformation("Extracción tradicional insuficiente. Intentando fallback con análisis de captura (AI)...");
            await TryFallbackWithAiAsync(page, element, product);
        }

        return product;
    }

    private async Task TryFallbackWithAiAsync(IPage page, IElementHandle element, ScrapedProduct product)
    {
        try
        {
            // Capturar screenshot del elemento
            var screenshotBytes = await element.ScreenshotAsync(new ElementHandleScreenshotOptions { Type = ScreenshotType.Png });
            var base64 = Convert.ToBase64String(screenshotBytes);
            
            // Crear payload para el procesador
            var rawDataPayload = new { screenshotBase64 = base64 };
            var rawDataJson = System.Text.Json.JsonSerializer.Serialize(rawDataPayload);
            
            // Procesar con AI
            try
            {
                var processed = await _aiProcessor.ProcessProductAsync(rawDataJson);
                
                // Mapear resultados si mejoran lo existente
                if (string.IsNullOrEmpty(product.Title) && !string.IsNullOrEmpty(processed.Name))
                    product.Title = processed.Name;
                    
                if (!product.Price.HasValue && processed.Price.HasValue)
                    product.Price = processed.Price;
                    
                if (string.IsNullOrEmpty(product.SkuSource) && !string.IsNullOrEmpty(processed.Sku))
                    product.SkuSource = processed.Sku;
                    
                if (string.IsNullOrEmpty(product.Description) && !string.IsNullOrEmpty(processed.Description))
                    product.Description = processed.Description;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("AI Fallback analysis skipped: {Message}", ex.Message);
            }
                
            product.AiEnriched = true;
            _logger.LogInformation("Fallback AI exitoso: Titulo={Title}, Precio={Price}", product.Title, product.Price);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error durante fallback AI");
        }
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
        
        // Limpiar texto de precio (ej: "1,234.56 MXN" -> "1234.56")
        // Festo usa MXN y a veces formatos con comas/puntos invertidos según región
        var cleaned = priceText.Replace("MXN", "").Replace("$", "").Trim();
        
        // Detectar si el punto es separador de miles (ej: 1.234,56) o decimal (1,234.56)
        // Regla simple: si hay coma y punto, y el punto está después, el punto es decimal.
        // En MX/ES festo suele ser 1.234,56 o similar.
        
        bool hasComma = cleaned.Contains(",");
        bool hasDot = cleaned.Contains(".");
        
        if (hasComma && hasDot)
        {
            if (cleaned.IndexOf(",") < cleaned.IndexOf("."))
            {
                // Formato 1,234.56 -> Quitar coma
                cleaned = cleaned.Replace(",", "");
            }
            else
            {
                // Formato 1.234,56 -> Quitar punto y cambiar coma a punto
                cleaned = cleaned.Replace(".", "").Replace(",", ".");
            }
        }
        else if (hasComma)
        {
            // Solo coma -> asumimos decimal (ej: 1234,56)
            cleaned = cleaned.Replace(",", ".");
        }
        
        // Quitar cualquier otro carácter que no sea dígito o punto
        cleaned = new string(cleaned.Where(c => char.IsDigit(c) || c == '.').ToArray());
        
        if (decimal.TryParse(cleaned, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var price))
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

    private async Task<string?> CaptureScreenshotBase64Async(IPage page, string suffix)
    {
        try
        {
            var bytes = await page.ScreenshotAsync(new() { FullPage = true });
            var base64 = Convert.ToBase64String(bytes);
            _logger.LogInformation("Captured screenshot for {Suffix}, size={Size} bytes", suffix, bytes.Length);
            return base64;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not capture screenshot for {Suffix}", suffix);
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Verificar si se debe mantener el navegador abierto
        var keepBrowser = Environment.GetEnvironmentVariable("SCRAPSAE_KEEP_BROWSER") == "true";
        if (keepBrowser)
        {
            _logger.LogInformation("Manteniendo navegador abierto para futura reutilización de sesión");
            return;
        }
        
        if (_context != null)
        {
            try 
            {
                await _context.CloseAsync();
                await _context.DisposeAsync();
            } 
            catch { }
            _context = null;
        }

        // Only dispose _playwright if we created it locally (persistent context case)
        if (_playwright != null)
        {
            _playwright.Dispose();
            _playwright = null;
        }
        
        GC.SuppressFinalize(this);
    }


    private async Task SaveDebugScreenshotAsync(IPage page, string suffix)
    {
        try
        {
            var filename = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{suffix}.png";
            var screenshotPath = Path.Combine(GetScreenshotDirectory(), filename);
            await page.ScreenshotAsync(new() { Path = screenshotPath, FullPage = true });
            _logger.LogInformation("Saved debug screenshot to: {Path}", screenshotPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not save debug screenshot");
        }
    }

    private async Task LogStepAsync(Guid siteId, string status, string message, object? details = null)
    {
        if (_syncLogService == null)
        {
            return;
        }

        try
        {
            var log = new SyncLog
            {
                OperationType = "scrape-step",
                SiteId = siteId,
                Status = status,
                Message = message,
                Details = details == null ? null : System.Text.Json.JsonSerializer.Serialize(details),
                CreatedAt = DateTime.UtcNow
            };
            await _syncLogService.LogOperationAsync(log);
        }
        catch
        {
            // Avoid breaking scraping flow if logging fails.
        }
    }

    private async Task SaveElementScreenshotAsync(ILocator locator, string suffix)
    {
        try
        {
            var filename = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{suffix}.png";
            var screenshotPath = Path.Combine(GetScreenshotDirectory(), filename);
            await locator.ScreenshotAsync(new() { Path = screenshotPath });
            _logger.LogInformation("Saved element screenshot to: {Path}", screenshotPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not save element screenshot");
        }
    }

    private static string GetScreenshotDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), ScreenshotDirectoryName);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private async Task<string?> SaveStepScreenshotAsync(IPage page, string suffix)
    {
        try
        {
            var filename = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{suffix}.png";
            var screenshotPath = Path.Combine(GetScreenshotDirectory(), filename);
            await page.ScreenshotAsync(new() { Path = screenshotPath, FullPage = true });
            _logger.LogInformation("Saved step screenshot to: {Path}", screenshotPath);
            return filename;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not save step screenshot");
            return null;
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
            ".didomi-notice-agree-button",
            "button:has-text('Aceptar todas las cookies')",
            "button:has-text('Aceptar solo lo necesario')",
            "button:has-text('Accept all')",
            "button:has-text('Agree')",
            "a.didomi-components-button", // Selector adicional solicitado por el usuario
            "button.didomi-components-button"
        };

        foreach (var selector in selectors)
        {
            try
            {
                var locator = page.Locator(selector);
                if (await locator.CountAsync() > 0 && await locator.First.IsVisibleAsync())
                {
                    _logger.LogInformation("Clicking cookie consent button: {Selector}", selector);
                    await locator.First.ClickAsync(new LocatorClickOptions { Force = true, Timeout = 5000 });
                    await Task.Delay(1000, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("AcceptCookiesAsync: Selector {Selector} failed: {Msg}", selector, ex.Message);
            }
        }

        // Fallback agresivo: Remover el banner del DOM si persiste
        try
        {
            await page.EvaluateAsync(@"() => {
                const selectors = ['#didomi-host', '.didomi-popup-container', '.didomi-popup-backdrop', '#didomi-notice', '.didomi-notice-popup'];
                selectors.forEach(s => {
                    const el = document.querySelector(s);
                    if (el) el.remove();
                });
                document.body.classList.remove('didomi-popup-open');
                document.body.style.overflow = 'auto';
                document.documentElement.style.overflow = 'auto';
            }");
            _logger.LogInformation("Attempted to remove cookie banner via DOM manipulation.");
        }
        catch (Exception ex)
        {
            _logger.LogDebug("DOM cookie removal failed: {Msg}", ex.Message);
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
            "button:has-text('Inicio de sesión')",
            "a:has-text('Inicio de sesión')"
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
    
    /// <summary>
    /// Verifica y refresca la sesión si es necesario (session keepalive)
    /// </summary>
    private async Task<bool> EnsureSessionActiveAsync(IPage page, Guid siteId, CancellationToken cancellationToken)
    {
        try
        {
            // Verificar si estamos logueados
            var isLogged = await IsLoggedInAsync(page);
            
            if (isLogged)
            {
                // Sesión activa - hacer click en el icono de usuario para refrescarla
                var userIconSelectors = new[]
                {
                    "[data-testid='user-menu']",
                    ".js-account-widget",
                    "button[aria-label*='cuenta'], button[aria-label*='account']",
                    "a[href*='/account'], a[href*='/mi-cuenta']",
                    // Icono de silueta de persona (común en Festo)
                    "svg[class*='user'], svg[class*='person'], svg[class*='account']",
                    "[class*='user-icon'], [class*='account-icon']",
                    // Header icons
                    "header button:has(svg), nav button:has(svg)"
                };
                
                foreach (var selector in userIconSelectors)
                {
                    try
                    {
                        var icon = page.Locator(selector).First;
                        if (await icon.CountAsync() > 0 && await icon.IsVisibleAsync())
                        {
                            _logger.LogDebug("Refrescando sesión con click en: {Selector}", selector);
                            await icon.ClickAsync(new LocatorClickOptions { Timeout = 3000 });
                            await Task.Delay(1000, cancellationToken);
                            
                            // Cerrar cualquier menú que se haya abierto
                            await page.Keyboard.PressAsync("Escape");
                            await Task.Delay(500, cancellationToken);
                            
                            await LogStepAsync(siteId, "info", "Sesión refrescada exitosamente", null);
                            return true;
                        }
                    }
                    catch { }
                }
                
                _logger.LogDebug("Sesión activa pero no se encontró icono para refrescar");
                return true;
            }
            
            // Sesión no activa - intentar click en el icono de login
            await LogStepAsync(siteId, "warning", "Sesión parece inactiva, intentando recuperar", null);
            
            var loginIconSelectors = new[]
            {
                "[data-testid='navigation-login-link']",
                "a:has-text('Inicio de sesión')",
                "button:has-text('Login')",
                "[class*='login-link'], [class*='sign-in']"
            };
            
            foreach (var selector in loginIconSelectors)
            {
                try
                {
                    var icon = page.Locator(selector).First;
                    if (await icon.CountAsync() > 0 && await icon.IsVisibleAsync())
                    {
                        _logger.LogInformation("Encontrado enlace de login, haciendo click para recuperar sesión");
                        await icon.ClickAsync(new LocatorClickOptions { Timeout = 5000 });
                        await Task.Delay(2000, cancellationToken);
                        
                        // Verificar si ahora estamos en página de login
                        if (page.Url.Contains("auth.festo.com"))
                        {
                            await LogStepAsync(siteId, "warning", "Redirigido a página de login - se requiere re-autenticación", null);
                            return false;
                        }
                        
                        return await IsLoggedInAsync(page);
                    }
                }
                catch { }
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error verificando sesión activa");
            return false;
        }
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

    private static async Task<string?> GetDetailHrefFromCardAsync(ILocator card, SiteSelectors selectors)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(selectors.ProductLinkSelector))
            {
                var link = card.Locator(selectors.ProductLinkSelector);
                if (await link.CountAsync() > 0)
                {
                    var href = await link.First.GetAttributeAsync("href");
                    if (!string.IsNullOrWhiteSpace(href))
                    {
                        return href;
                    }
                }
            }

            var imageLink = card.Locator("a:has(img)");
            if (await imageLink.CountAsync() > 0)
            {
                var href = await imageLink.First.GetAttributeAsync("href");
                if (!string.IsNullOrWhiteSpace(href))
                {
                    return href;
                }
            }

            var titleLink = card.Locator("a:has(h1), a:has(h2), a:has(h3), a:has(span)");
            if (await titleLink.CountAsync() > 0)
            {
                var href = await titleLink.First.GetAttributeAsync("href");
                if (!string.IsNullOrWhiteSpace(href))
                {
                    return href;
                }
            }

            var patternLink = card.Locator("a[href*='/p/'], a[href*='/a/'], a[href*='/product'], a[href*='/producto']");
            if (await patternLink.CountAsync() > 0)
            {
                return await patternLink.First.GetAttributeAsync("href");
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

    private async Task<List<ScrapedProduct>> TryScrapeCategorySearchAsync(
        IPage page,
        SiteProfile site,
        SiteSelectors selectors,
        CancellationToken cancellationToken)
    {
        var products = new List<ScrapedProduct>();
        var landingUrl = selectors.CategoryLandingUrl ?? site.BaseUrl;
        if (string.IsNullOrWhiteSpace(landingUrl))
        {
            return products;
        }

        await _scrapeControl.WaitIfPausedAsync(site.Id, cancellationToken);
        if (!page.Url.StartsWith(landingUrl, StringComparison.OrdinalIgnoreCase))
        {
            await LogStepAsync(site.Id, "info", "Abriendo landing de categorias.", new { url = landingUrl });
            await page.GotoAsync(landingUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 90000
            });
            await AcceptCookiesAsync(page, cancellationToken);
        }

        var pageCategories = await ReadCategoryNamesAsync(page, site.Id, selectors, cancellationToken);
        if (pageCategories.Count > 0)
        {
            await LogStepAsync(site.Id, "info", "Categorias detectadas en pagina.", new { count = pageCategories.Count });
        }

        var searchTerms = selectors.CategorySearchTerms.Count > 0
            ? new List<string>(selectors.CategorySearchTerms)
            : pageCategories;

        if (searchTerms.Count == 0)
        {
            await LogStepAsync(site.Id, "warn", "No se encontraron categorias para buscar.");
            return products;
        }

        if (pageCategories.Count > 0 && selectors.CategorySearchTerms.Count > 0)
        {
            searchTerms = searchTerms
                .Where(term => pageCategories.Any(cat => cat.Contains(term, StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (searchTerms.Count == 0)
        {
            await LogStepAsync(site.Id, "warn", "Categorias del catalogo no coinciden con la pagina.");
            return products;
        }

        var maxProducts = site.MaxProductsPerScrape > 0 ? site.MaxProductsPerScrape : int.MaxValue;
        var seenProducts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var term in searchTerms)
        {
            if (products.Count >= maxProducts || cancellationToken.IsCancellationRequested)
            {
                break;
            }

            await _scrapeControl.WaitIfPausedAsync(site.Id, cancellationToken);
            await LogStepAsync(site.Id, "info", "Buscando categoria.", new { term });

            var input = await FindSearchInputAsync(page, selectors);
            if (input == null)
            {
                var fallbackUrl = BuildSearchUrl(site.BaseUrl, term);
                if (string.IsNullOrWhiteSpace(fallbackUrl))
                {
                    await LogStepAsync(site.Id, "warn", "No se encontro input de busqueda y no hay URL de busqueda.");
                    break;
                }

                await LogStepAsync(site.Id, "info", "Navegando a URL de busqueda.", new { url = fallbackUrl });
                await page.GotoAsync(fallbackUrl, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 90000
                });
                await AcceptCookiesAsync(page, cancellationToken);
            }
            else
            {
                await input.ClickAsync();
                try
                {
                    await input.PressAsync("Control+A");
                    await input.PressAsync("Delete");
                }
                catch
                {
                    // Ignore and fallback to FillAsync below.
                }
                await input.FillAsync(term);

                var searchButton = await FindSearchButtonAsync(page, selectors);
                if (searchButton != null)
                {
                    await VerifyAndClickAsync(searchButton, page, "magnifier_button", cancellationToken);
                }
                else
                {
                    await input.PressAsync("Enter");
                }
            }

            await WaitForProductListAsync(page, selectors, cancellationToken);

            var beforeCount = products.Count;
            var resultsShot = await SaveStepScreenshotAsync(page, $"search_results_{term}");
            
            // Usar el nuevo método que navega por subcategorías
            await NavigateAndCollectFromSubcategoriesAsync(
                page,
                site.Id,
                selectors,
                products,
                seenProducts,
                maxProducts,
                new List<string> { term },
                cancellationToken,
                depth: 0);
            
            var added = products.Count - beforeCount;
            await LogStepAsync(site.Id, "success", "Busqueda completada.", new { term, added, screenshotFile = resultsShot });

            await _scrapeControl.WaitIfPausedAsync(site.Id, cancellationToken);
            if (!page.Url.StartsWith(landingUrl, StringComparison.OrdinalIgnoreCase))
            {
                await page.GotoAsync(landingUrl, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 90000
                });
                await AcceptCookiesAsync(page, cancellationToken);
            }
        }

        return products;
    }

    private static string? BuildSearchUrl(string baseUrl, string term)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(term))
        {
            return null;
        }

        if (baseUrl.Contains("festo.com", StringComparison.OrdinalIgnoreCase))
        {
            return $"https://www.festo.com/mx/es/search/?text={Uri.EscapeDataString(term)}";
        }

        return null;
    }

    private async Task<List<string>> ReadCategoryNamesAsync(
        IPage page,
        Guid siteId,
        SiteSelectors selectors,
        CancellationToken cancellationToken)
    {
        var names = new List<string>();
        var selector = selectors.CategoryLinkSelector ??
                       "a[data-testid='category-tile'], a[href*='/c/'], a[href*='/category']";
        try
        {
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            var items = page.Locator(selector);
            var count = await items.CountAsync();
            for (var i = 0; i < count; i++)
            {
                await _scrapeControl.WaitIfPausedAsync(siteId, cancellationToken);
                var item = items.Nth(i);
                var name = await ExtractCategoryNameAsync(item, selectors);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name);
                }
            }
        }
        catch
        {
            // Ignore and fallback.
        }

        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static async Task<string?> ExtractCategoryNameAsync(ILocator item, SiteSelectors selectors)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(selectors.CategoryNameSelector))
            {
                var nameEl = item.Locator(selectors.CategoryNameSelector);
                if (await nameEl.CountAsync() > 0)
                {
                    var text = await nameEl.First.InnerTextAsync();
                    return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
                }
            }

            var fallback = await item.InnerTextAsync();
            return string.IsNullOrWhiteSpace(fallback) ? null : fallback.Trim();
        }
        catch
        {
            return null;
        }
    }

    private async Task<ILocator?> FindSearchInputAsync(IPage page, SiteSelectors selectors)
    {
        var selectorList = new List<string>();
        if (!string.IsNullOrWhiteSpace(selectors.SearchInputSelector))
        {
            selectorList.Add(selectors.SearchInputSelector);
        }

        selectorList.AddRange(new[]
        {
            "input[type='search']",
            "input[placeholder*='Buscar']",
            "input[aria-label*='Buscar']",
            "input[name='q']"
        });

        foreach (var selector in selectorList.Distinct())
        {
            var locator = page.Locator(selector);
            if (await locator.CountAsync() > 0)
            {
                return locator.First;
            }
        }

        return null;
    }

    private async Task<ILocator?> FindSearchButtonAsync(IPage page, SiteSelectors selectors)
    {
        var selectorList = new List<string>();
        if (!string.IsNullOrWhiteSpace(selectors.SearchButtonSelector))
        {
            selectorList.Add(selectors.SearchButtonSelector);
        }

        selectorList.AddRange(new[]
        {
            ".magnifier-button",
            "button.magnifier-button",
            "[data-testid*='magnifier']",
            "button[aria-label*='Buscar']"
        });

        foreach (var selector in selectorList.Distinct())
        {
            var locator = page.Locator(selector);
            if (await locator.CountAsync() > 0)
            {
                return locator.First;
            }
        }

        return null;
    }

    private async Task WaitForProductListAsync(IPage page, SiteSelectors selectors, CancellationToken cancellationToken)
    {
        var selectorList = new List<string>();
        if (!string.IsNullOrWhiteSpace(selectors.ProductListSelector))
        {
            selectorList.Add(selectors.ProductListSelector);
        }

        if (!string.IsNullOrWhiteSpace(selectors.ProductListClassPrefix))
        {
            selectorList.Add(BuildClassPrefixSelector(selectors.ProductListClassPrefix));
        }

        selectorList.AddRange(new[]
        {
            ".single-product-container--oWOit",
            ".single-product-container",
            ".product-list-page-container-right",
            ".product-list-page-container-right--",
            "[class*='article-card--']",
            "[class*='categories-list-grid--']"
        });

        foreach (var selector in selectorList.Distinct())
        {
            try
            {
                await page.WaitForSelectorAsync(selector, new() { Timeout = 15000 });
                return;
            }
            catch
            {
                // Try next selector.
            }
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
            site.Id,
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
        Guid siteId,
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

        var productListFound = await page.QuerySelectorAsync(".single-product-container--oWOit, .single-product-container, [class*='article-card--'], [class*='categories-list-grid--']");
        if (productListFound != null)
        {
        await CollectProductsFromListAsync(
            page,
            siteId,
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
                siteId,
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
        int pageNum = 1;
        bool hasNextPage = true;

        do
        {
            await FastScrollToBottomAsync(page);
            
            // Updated Selectors for New HTML (Comprar Placas...) + Legacy Fallback
            var cards = page.Locator("div[class*='article-card--'], div[class*='single-product-container--'], div.article-card");
            var count = await cards.CountAsync();

            await LogStepAsync(siteId, "info", $"Página {pageNum}: Productos detectados en listado: {count}.", new
            {
                count,
                url = page.Url
            });

            for (var i = 0; i < count && products.Count < maxProducts; i++)
            {
                await _scrapeControl.WaitIfPausedAsync(siteId, cancellationToken);
                var card = cards.Nth(i);
                
                // 1. Try to find link directly (Most reliable for new HTML)
                var detailHref = await GetDetailHrefFromCardAsync(card, selectors);
                
                if (string.IsNullOrWhiteSpace(detailHref))
                {
                    // Fallback: Try identifying button (Legacy)
                    var detailButton = card.Locator("button:has-text('Detalles'), [role='button']:has-text('Detalles')").First;
                    if (await detailButton.CountAsync() > 0)
                    {
                        // Logic simplified: If we have a button, we might need to click it to get the link 
                        // or it navigates. For now, let's treat it as a failure to get *Link* and log it, 
                        // OR falling back to legacy behavior is too complex here? 
                        // Actually, GetDetailHrefFromCardAsync should be improved to find the link if it exists.
                        _logger.LogWarning($"Card {i} sin HREF directo. Intentando lógica legacy...");
                        // Call legacy logic for this card? No, hard to Mix. 
                        // Let's assume GetDetailHrefFromCardAsync covers it or we skip.
                        await SaveDebugCardHtmlAsync(card, "card_no_href");
                        continue;
                    }
                }

                if (!string.IsNullOrWhiteSpace(detailHref))
                {
                    detailHref = NormalizeHref(page.Url, detailHref);
                    if (seenProducts.Contains(detailHref)) continue;

                    // Navigate Deep
                    var previousUrl = page.Url;
                    try
                    {
                        // Use existing method for details + variations
                        // Signature: TryScrapeProductDetailWithVariationsAsync(IPage page, string startUrl, SiteSelectors selectors, CancellationToken cancellationToken)
                        var extractedProducts = await TryScrapeProductDetailWithVariationsAsync(page, detailHref, selectors, cancellationToken);
                        
                        foreach(var p in extractedProducts)
                        {
                            if (p != null)
                            {
                                p.SourceUrl = detailHref; // Ensure SourceUrl is set
                                products.Add(p);
                                seenProducts.Add(detailHref);
                                // SaveProductToStagingAsync removed as it doesn't exist in this context. 
                                // Caller (ScrapeAsync) handles persistence of the 'products' list.
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Error procesando producto {detailHref}: {ex.Message}");
                    }
                    finally
                    {
                        if (page.Url != previousUrl)
                        {
                            await page.GotoAsync(previousUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
                            await FastScrollToBottomAsync(page); // Re-trigger lazy load
                            // Re-init locator after navigation
                            cards = page.Locator("div[class*='article-card--'], div[class*='single-product-container--'], div.article-card");
                        }
                    }
                }
            }

            // Pagination Check
            var nextButtons = page.Locator("[class*='Pagination_arrowButton']"); // Broad selector
            var buttonsCount = await nextButtons.CountAsync();
            
            bool clickedNext = false;
            if (buttonsCount > 0)
            {
               // Use Nth(count - 1) instead of Last() to avoid LINQ/Property ambiguity errors
               var nextButton = nextButtons.Nth(buttonsCount - 1);
               if (await nextButton.IsVisibleAsync() && await nextButton.IsEnabledAsync())
               {
                   try 
                   {
                       // Check if it's disabled via class
                       var classAttr = await nextButton.GetAttributeAsync("class");
                       if (classAttr != null && !classAttr.Contains("disabled"))
                       {
                           await LogStepAsync(siteId, "info", $"Clicks en Siguiente Página ({pageNum} -> {pageNum+1})...");
                           await nextButton.ClickAsync();
                           await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                           await HumanDelayAsync(2000, 3000);
                           pageNum++;
                           clickedNext = true;
                       }
                   }
                   catch (Exception ex) { _logger.LogWarning($"Error pagination: {ex.Message}"); }
               }
            }

            hasNextPage = clickedNext && products.Count < maxProducts;

        } while (hasNextPage && !cancellationToken.IsCancellationRequested);
    }

    private async Task CollectProductsFromList_LegacyAsync(
        IPage page,
        Guid siteId,
        SiteSelectors selectors,
        List<ScrapedProduct> products,
        HashSet<string> seenProducts,
        int maxProducts,
        List<string> categoryPath,
        CancellationToken cancellationToken)
    {
        // 1. Selector Robusto para Tarjetas de Producto
        var cards = page.Locator("[class*='single-product-container--'], [class*='article-card--'], .single-product-container, [data-testid='product-card']");
        var count = await cards.CountAsync();
        
        await LogStepAsync(siteId, "info", $"Productos detectados en listado: {count}", new
        {
            count,
            categoryPath = categoryPath.Count > 0 ? string.Join(" > ", categoryPath) : null
        });

        // Loop de extracción
        int analyzedCount = 0;
        int maxAnalysisLimit = 100; // User request: Limit analysis to avoiding cycling

        for (var i = 0; i < count && products.Count < maxProducts; i++)
        {
            if (analyzedCount >= maxAnalysisLimit) 
            {
                _logger.LogWarning("Se alcanzó el límite de análisis de {Limit} items por página. Pasando a siguiente estrategia.", maxAnalysisLimit);
                break;
            }
            analyzedCount++;

            await _scrapeControl.WaitIfPausedAsync(siteId, cancellationToken);
            var card = cards.Nth(i);
            string? productUrl = null;
            bool navigated = false;
            
            try
            {
                 // A. Intentar obtener HREF directo (Prioridad)
                 productUrl = await GetDetailHrefFromCardAsync(card, selectors);
                 
                 if (!string.IsNullOrWhiteSpace(productUrl))
                 {
                     productUrl = NormalizeHref(page.Url, productUrl);
                     _logger.LogInformation("Procesando en NUEVA PESTAÑA (Link Directo): {Url}", productUrl);
                     
                     var detailPage = await page.Context.NewPageAsync();
                     try 
                     {
                         await detailPage.GotoAsync(productUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
                         await _processManager.WaitForHydrationAsync(detailPage, cancellationToken);
                         
                         var extractedProducts = await ExtractFestoProductsFromDetailPageAsync(detailPage, selectors, categoryPath, cancellationToken);
                         foreach (var product in extractedProducts)
                         {
                             var key = GetProductKey(product);
                             if (seenProducts.Add(key))
                             {
                                 if (!string.IsNullOrEmpty(productUrl)) product.SourceUrl = productUrl; 
                                 products.Add(product);
                             }
                         }
                     }
                     finally { await detailPage.CloseAsync(); }
                     continue; 
                 }

                 // B. Fallback: Clic en Botón (Solo si no hay link)
                 // HTML Analysis confirmed Festo main list items use <button class="...product-details-link--...">Detalles</button>
                 _logger.LogInformation("No se encontró URL directa. Buscando botón 'Detalles'...");
                 
                 var detailButtonSelectors = new List<string>
                 {
                     "[class*='product-details-link--']", // Verified from HTML
                     "button:has-text('Detalles')",
                     "[data-testid='button']:has-text('Detalles')",
                     "[role='button']:has-text('Detalles')"
                 };

                 if (!string.IsNullOrWhiteSpace(selectors.DetailButtonText)) 
                    detailButtonSelectors.Add($"button:has-text('{selectors.DetailButtonText}')");
                 
                 var detailButton = card.Locator(string.Join(", ", detailButtonSelectors.Distinct())).First;
                 if (await detailButton.CountAsync() > 0)
                 {
                     await card.ScrollIntoViewIfNeededAsync();
                     _logger.LogInformation("Botón Detalles encontrado. Intentando clic y abrir nueva pestaña (workaround)...");

                     // Workaround: Opening in new tab via Ctrl+Click is cleaner than back-and-forth
                     // But verify if Festo supports Ctrl+Click on these buttons (often they are JS onclicks).
                     // If JS: we must click, wait for nav, process, go back.
                     // Verified: They are <button>, so likely JS router push.
                     
                     // Capture current URL to verify nav
                     var preClickUrl = page.Url;
                     
                     try {
                        await detailButton.ClickAsync(new LocatorClickOptions { Timeout = 10000 });
                     } catch {
                        await detailButton.DispatchEventAsync("click");
                     }
                     
                     await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
                     await _processManager.WaitForHydrationAsync(page, cancellationToken);

                     if (page.Url != preClickUrl)
                     {
                         // Navigated successfully
                         _logger.LogInformation("Navegación por clic exitosa. Extrayendo...");
                         var extractedProducts = await ExtractFestoProductsFromDetailPageAsync(page, selectors, categoryPath, cancellationToken);
                         foreach (var product in extractedProducts)
                         {
                             var key = GetProductKey(product);
                             if (seenProducts.Add(key))
                             {
                                 products.Add(product);
                             }
                         }
                         
                         // GO BACK
                         _logger.LogInformation("Regresando al listado...");
                         await page.GoBackAsync(new PageGoBackOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
                         await _processManager.WaitForHydrationAsync(page, cancellationToken);
                     }
                     else
                     {
                         _logger.LogWarning("Clic realizado pero URL no cambió. Posible modal o fallo.");
                         await SaveDebugCardHtmlAsync(card, "festo_click_fail");
                     }
                 }
                 else
                 {
                    _logger.LogWarning("Ni link ni botón encontrados para tarjeta {Index}", i);
                    await SaveDebugCardHtmlAsync(card, "festo_card_dead");
                 }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error procesando tarjeta de producto {Index}", i);
                // Try recovery logic
                try { if(page.Url.Contains("/p/")) await page.GoBackAsync(); } catch {}
            }
        }
    }


// -------------------------------------------------------------------------
// UNIFIED HYBRID CRAWLER IMPLEMENTATION
// -------------------------------------------------------------------------

    /// <summary>
    /// Extracts links from the breadcrumb navigation to enable "Upward/Sideways" crawling.
    /// </summary>
    private async Task<List<string>> ExtractBreadcrumbLinksAsync(IPage page)
    {
        var links = new List<string>();
        try
        {
            // Common breadcrumb selectors
            var selectors = new[] { 
                "nav[aria-label='breadcrumb'] a", 
                ".breadcrumb a", 
                "ul.breadcrumbs a", 
                "[data-testid='breadcrumb'] a",
                ".main-navigation a" // Fallback
            };

            foreach (var selector in selectors)
            {
                var elements = await page.QuerySelectorAllAsync(selector);
                foreach (var el in elements)
                {
                    var href = await el.GetAttributeAsync("href");
                    if (!string.IsNullOrWhiteSpace(href))
                    {
                         var fullUrl = NormalizeHref(page.Url, href);
                         // Filter out "Home" usually, or keep it if we want to restart from root (maybe too aggressive?)
                         // Let's filter out simple "/" or "home" to avoid restarting the whole site scan
                         if (fullUrl.TrimEnd('/').EndsWith("festo.com") || fullUrl.EndsWith("/mx/es")) continue;
                         
                         links.Add(fullUrl);
                    }
                }
                if (links.Count > 0) break; 
            }
        }
        catch { }
        return links.Distinct().ToList();
    }

    /// <summary>
    /// Crawls deeper from a set of seed URLs/Products to find related items (Graph Traversal).
    /// </summary>
    private async Task CrawlProductsFromSeedsAsync(
        IPage page, 
        Guid siteId, 
        SiteSelectors selectors,
        List<ScrapedProduct> products, 
        HashSet<string> seenProducts, 
        Queue<string> urlQueue,
        CancellationToken cancellationToken)
    {
        // User requested removing limits, but we need a sanity check to avoid infinite memory growth.
        const int MaxCrawlDepth = 10; 
        
        _logger.LogInformation("Starting Deep Crawl from {Count} seeds...", urlQueue.Count);

        // Track depth per URL using a parallel dictionary
        var urlDepths = new Dictionary<string, int>();
        foreach(var url in urlQueue) urlDepths[url] = 0;
        
        // Avoid visiting the same URL twice in the crawl session
        var visitedInCrawl = new HashSet<string>();

        while (urlQueue.Count > 0 && !cancellationToken.IsCancellationRequested)
        {
            var currentUrl = urlQueue.Dequeue();
            if (visitedInCrawl.Contains(currentUrl)) continue;
            visitedInCrawl.Add(currentUrl);

            int currentDepth = urlDepths.GetValueOrDefault(currentUrl, 0);
            if (currentDepth >= MaxCrawlDepth) continue;

            try
            {
                // --- DATASHEET HANDLING ---
                // Si es una URL de datasheet, extraemos la info directamente sin navegar (para evitar descargas automáticas/errores)
                if (IsDatasheetUrl(currentUrl))
                {
                    _logger.LogInformation("[Crawler] Datasheet URL detected: {Url}", currentUrl);
                    var datasheetProduct = ExtractDatasheetProduct(currentUrl);
                    
                    var key = GetProductKey(datasheetProduct);
                    if (seenProducts.Add(key))
                    {
                        products.Add(datasheetProduct);
                        await LogStepAsync(siteId, "success", $"[Crawler] +Datasheet {datasheetProduct.SkuSource}", new { sku = datasheetProduct.SkuSource, is_attachment = true });
                    }
                    continue; // Skip navigation
                }

                await _scrapeControl.WaitIfPausedAsync(siteId, cancellationToken);
                
                if (products.Count % 10 == 0) 
                    _logger.LogInformation("[Crawler] Processed {Count} products so far...", products.Count);

                _logger.LogInformation("[Crawler Depth {Depth}] Visiting: {Url}", currentDepth, currentUrl);
                
                await page.GotoAsync(currentUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
                await AcceptCookiesAsync(page, cancellationToken);

                var newSeeds = new List<string>();

                // -------------------------------------------------------------------------
                // POLYMORPHIC HANDLING: Check if Product or Category (Refined with User Selectors)
                // -------------------------------------------------------------------------
                
                bool isProductPage = false;
                bool isCategoryPage = false;

                // 1. Detection via specific Festo classes
                if (await page.Locator("[class*='product-page-headline--']").CountAsync() > 0) 
                {
                    isProductPage = true;
                    _logger.LogInformation("[PageDetection] Identified as PRODUCT via 'product-page-headline'");
                }
                else if (await page.Locator("div[class*='product-list--']").CountAsync() > 0 || 
                         await page.Locator("div[class*='categories-list-grid--']").CountAsync() > 0 ||
                         await page.Locator("div[class*='article-card--']").CountAsync() > 0 ||
                         await page.Locator("[class*='Pagination_arrowButton']").CountAsync() > 0)
                {
                    isCategoryPage = true;
                    _logger.LogInformation("[PageDetection] Identified as CATEGORY/LIST via list/grid/card/pagination selectors.");
                }
                else 
                {
                    // Fallback heuristics if classes change or specific selectors fail
                    if (!string.IsNullOrEmpty(selectors.SkuSelector) && await page.Locator(selectors.SkuSelector).CountAsync() > 0) 
                    {
                        isProductPage = true;
                        _logger.LogInformation("[PageDetection] Identified as PRODUCT via SKU selector.");
                    }
                    else if (await page.Locator(".price-display-text, .price, .product-price").CountAsync() > 0) 
                    {
                        isProductPage = true;
                        _logger.LogInformation("[PageDetection] Identified as PRODUCT via Price selector.");
                    }
                    else if (currentUrl.Contains("/p/") || currentUrl.Contains("/a/")) 
                    {
                        isProductPage = true;
                         _logger.LogInformation("[PageDetection] Identified as PRODUCT via URL pattern (/p/ or /a/).");
                    }
                }
                
                if (isProductPage)
                {
                    // --- CASE A: PRODUCT PAGE ---
                    var extractedList = await ExtractFestoProductsFromDetailPageAsync(page, selectors, new List<string> { "Crawled" }, cancellationToken);
                    if (extractedList != null && extractedList.Any())
                    {
                        foreach(var p in extractedList) 
                        {
                            var key = GetProductKey(p);
                            if(seenProducts.Add(key)) 
                            {
                                products.Add(p);
                                await LogStepAsync(siteId, "success", $"[Crawler] +{p.Title}", new { sku = p.SkuSource, depth = currentDepth });
                            }
                        }
                    }
                    
                    // Harvest Neighbors (Sideways)
                    var relatedUrls = await DiscoverRelatedProductUrlsAsync(page, cancellationToken);
                    newSeeds.AddRange(relatedUrls);

                    // Harvest Breadcrumbs (Upwards/Sideways)
                    var breadcrumbs = await ExtractBreadcrumbLinksAsync(page);
                    if (breadcrumbs.Count > 0)
                    {
                        _logger.LogInformation("[Crawler] Found {Count} breadcrumb links to traverse.", breadcrumbs.Count);
                        newSeeds.AddRange(breadcrumbs);
                    }
                }
                else if (isCategoryPage)
                {
                    // --- CASE B: CATEGORY/LIST PAGE ---
                     _logger.LogInformation("[Crawler] Page identified as Category (via product-list class). Harvesting items...");
                     
                     // 1. Collect Products using specific 'triad-order-code' awareness if possible
                     // We update the selector context for this specific scrape to be very precise if needed, 
                     // but sticking to compatible CollectProducts is safer. 
                     // We will Verify the items found contain 'triad-order-code' or just trust the general list extractor which is robust.
                     
                     int countBefore = products.Count;
                     await CollectProductsFromListAsync(page, siteId, selectors, products, seenProducts, 10000, new List<string>{"CrawledCat"}, cancellationToken);
                     int collected = products.Count - countBefore;
                     
                     if (collected == 0)
                     {
                         // Fallback: If general collector failed but we KNOW it's a product list via class
                         // Try to find links near 'triad-order-code'
                         var codes = await page.Locator("span[class*='triad-order-code--']").AllAsync();
                         foreach (var codeSpan in codes)
                         {
                            // Try to find links relative to 'triad-order-code'
                            // We look for an anchor tag in the parent containers.
                            // Strategy: Go up to 3 levels (span -> div/li -> container) and find closest 'a' with href='/p/' or '/a/'
                            try 
                            {
                                // JS Eval is faster and more robust for "closest link"
                                var href = await codeSpan.EvaluateAsync<string>(@"el => {
                                    const card = el.closest('li') || el.closest('div.product-item') || el.closest('tr');
                                    if (!card) return null;
                                    const link = card.querySelector(""a[href*='/p/']"") || card.querySelector(""a[href*='/a/']"");
                                    return link ? link.href : null;
                                }");

                                if (!string.IsNullOrEmpty(href))
                                {
                                     var fullUrl = NormalizeHref(page.Url, href);
                                     if (!seenProducts.Contains(fullUrl) && !urlDepths.ContainsKey(fullUrl))
                                     {
                                         newSeeds.Add(fullUrl);
                                         _logger.LogDebug("[Crawler] Found item via triad-code: {Url}", fullUrl);
                                     }
                                }
                            }
                            catch { /* Ignore element-level failures */ }
                         }
                     }
                     else 
                     {
                         _logger.LogInformation("[Crawler] Harvested {Count} products from list.", collected);
                     }

                     // 2. Collect Subcategories/Links to inspect
                     var subLinks = await DiscoverRelatedProductUrlsAsync(page, cancellationToken);
                     newSeeds.AddRange(subLinks);
                }
                else
                {
                    // Unknown page type. Try generic discovery just in case it's a hub page or uncategorized
                    var links = await DiscoverRelatedProductUrlsAsync(page, cancellationToken);
                    newSeeds.AddRange(links);
                }

                // Enqueue new seeds
                foreach(var seed in newSeeds)
                {
                    if (!urlDepths.ContainsKey(seed) && !visitedInCrawl.Contains(seed)) {
                        
                        // Penalty: Breadcrumbs (Categories) cost less depth? Or same?
                        // Let's treat them same for now.
                        urlDepths[seed] = currentDepth + 1;
                        urlQueue.Enqueue(seed);
                    }
                }
            }
            catch (Exception ex)
            {
                 _logger.LogWarning("[Crawler] Failed to process {Url}: {msg}", currentUrl, ex.Message);
            }
        }
    }


    private async Task NavigateAndCollectFromSubcategoriesAsync(
        IPage page,
        Guid siteId,
        SiteSelectors selectors,
        List<ScrapedProduct> products,
        HashSet<string> seenProducts,
        int maxProducts,
        List<string> categoryPath,
        CancellationToken cancellationToken,
        int depth = 0)
    {
        // Read max depth from env or default to 5
        var maxDepthEnv = Environment.GetEnvironmentVariable("SCRAPSAE_MAX_DEPTH");
        int maxDepthLimit = 5;
        if (!string.IsNullOrWhiteSpace(maxDepthEnv) && int.TryParse(maxDepthEnv, out var parsedDepth))
        {
            maxDepthLimit = parsedDepth;
        }

        if (depth >= maxDepthLimit || products.Count >= maxProducts)
        {
            if (depth >= maxDepthLimit)
                _logger.LogInformation("[Depth {Depth}] Max recursion depth reached ({Max}). Stopping branch.", depth, maxDepthLimit);
            return;
        }

        var currentPath = string.Join(" > ", categoryPath);
        _logger.LogInformation("[Depth {Depth}] Entering category processing: {Path}", depth, currentPath);

        await _scrapeControl.WaitIfPausedAsync(siteId, cancellationToken);
        
        // Wait for content with a bit more patience
        try 
        {
             await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
             await Task.Delay(2000, cancellationToken); 
        }
        catch { /* Ignore timeout */ }

        // 1. Check for Products directly on this page
        var productContainers = page.Locator("[class*='single-product-container--'], [class*='article-card--']");
        var productCount = await productContainers.CountAsync();
        
        // Check for results counter as secondary indicator
        var resultsCount = await page.QuerySelectorAsync("h2[class*='results-count--']");
        if (resultsCount != null)
        {
            productCount = Math.Max(productCount, 1); // Ensure we try to extract if counter exists
        }

        if (productCount > 0)
        {
            _logger.LogInformation("[Depth {Depth}] Found product/list container. Starting Hybrid Extraction...", depth);
            
            // Phase 1: Collect from the list (Vertical/Initial Harvest)
            int countBefore = products.Count;
            await CollectProductsFromListAsync(page, siteId, selectors, products, seenProducts, maxProducts, categoryPath, cancellationToken);
            int newFound = products.Count - countBefore;
            
            // Phase 2: Crawler Mode (Horizontal/Deep Expansion)
            // Use the products we just found as seeds for the crawler
            // Also invoke DiscoverRelatedProductUrlsAsync on the list page itself
            
            var crawlQueue = new Queue<string>();
            
            // Ensure lazy loaded elements (recommendations) are visible
            await FastScrollToBottomAsync(page);

            // Add explicitly discovered URLs from the list page
            var discoveredOnListPage = await DiscoverRelatedProductUrlsAsync(page, cancellationToken);
            foreach(var url in discoveredOnListPage) {
                if(!seenProducts.Contains(url)) crawlQueue.Enqueue(url);
            }

            // CRITICAL FIX: Seed the crawler with the products we JUST found in Phase 1.
            // Even if DiscoverRelated... returned 0, we know we found products.
            // Iterate the new products added to the list and add their SourceUrls to the queue.
            for(int i = countBefore; i < products.Count; i++)
            {
                var p = products[i];
                if (!string.IsNullOrEmpty(p.SourceUrl) && !seenProducts.Contains(p.SourceUrl))
                {
                    crawlQueue.Enqueue(p.SourceUrl);
                }
                // Also add SourceUrl derived from SKU if pattern matches
                // (e.g. if we have an SKU but SourceUrl was empty/internal)
            }
            
            // Note: CollectProductsFromListAsync updates 'products' but doesn't explicitly return their URLs in a list.
            // In a real optimized scenario we'd capture them. For now, relying on 'discoveredOnListPage' is a good approximation,
            // OR we can iterate the last 'newFound' products to get their URLs if they have them.
            
            if (crawlQueue.Count > 0)
            {
                 _logger.LogInformation("[Depth {Depth}] Transitioning to Crawler Mode with {Count} seeds...", depth, crawlQueue.Count);
                 await CrawlProductsFromSeedsAsync(page, siteId, selectors, products, seenProducts, crawlQueue, cancellationToken);
            }

            return; // Found products (leaf node behaviour), so we stop looking for subcategories here
        }

        // 2. No products found, look for Subcategories
        _logger.LogInformation("[Depth {Depth}] No products found. looking for subcategories...", depth);

        var subcategorySelectors = new[]
        {
            "div[class*='categories-list-grid--'] a",
            "a[class*='category-tile--']",
            "a[data-testid='category-tile']",
            "a[class*='product-family--']", // Festo families
            "div[class*='category-container--'] a",
            "[class*='category-list'] a",
            "a[href*='/c/']", // Generic fallback for Festo 'category' paths
            "a[href*='/cam/']" // Another Festo pattern
        };

        ILocator? subcategoryLinksLocations = null;
        int linkCount = 0;
        string usedSelector = "";

        foreach (var selector in subcategorySelectors)
        {
            var links = page.Locator(selector);
            var count = await links.CountAsync();
            if (count > 0)
            {
                // Validate that these are not header/footer links but main content
                // A primitive check: ensure they are somewhat large or central? 
                // For now, accept the first match that yields results.
                subcategoryLinksLocations = links;
                linkCount = count;
                usedSelector = selector;
                break;
            }
        }

        if (subcategoryLinksLocations == null || linkCount == 0)
        {
            _logger.LogWarning("[Depth {Depth}] Dead end: No products and no subcategories found at {Url}", depth, page.Url);
            await LogStepAsync(siteId, "info", "Dead end (no products/subcategories)", new { url = page.Url, depth });
            return;
        }

        _logger.LogInformation("[Depth {Depth}] Found {Count} subcategories using selector '{Selector}'", depth, linkCount, usedSelector);

        // Snapshot URLs to avoid stale element exceptions during navigation
        var subCatsToVisit = new List<(string url, string name)>();
        for (int i = 0; i < Math.Min(linkCount, 30); i++) // Limit breadth per level to 30 to avoid explosion
        {
            try 
            {
                var link = subcategoryLinksLocations.Nth(i);
                var href = await link.GetAttributeAsync("href");
                var name = await link.InnerTextAsync();
                
                if (!string.IsNullOrWhiteSpace(href))
                {
                    // Basic filter to avoid non-category links (e.g. login, contact)
                    if (href.Contains("login") || href.Contains("contact") || href.Contains("javascript")) continue;

                    var fullUrl = NormalizeHref(page.Url, href);
                    subCatsToVisit.Add((fullUrl, name?.Trim() ?? "Unknown"));
                }
            }
            catch {}
        }

        // 3. Process Subcategories
        foreach (var (url, name) in subCatsToVisit)
        {
            if (products.Count >= maxProducts || cancellationToken.IsCancellationRequested) break;

            var newPath = new List<string>(categoryPath) { name };
            
            // Retry logic for navigation
            bool navSuccess = false;
            for(int attempt=0; attempt<3; attempt++) 
            {
                try 
                {
                    await _scrapeControl.WaitIfPausedAsync(siteId, cancellationToken);
                    
                    // Only log on first attempt to avoid noise
                    if(attempt == 0) 
                        _logger.LogInformation("[Depth {Depth}] Navigating to subcategory '{Name}': {Url}", depth, name, url);
                    
                     await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 90000 });
                     navSuccess = true;
                     break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("[Depth {Depth}] Navigation to subcategory failed (Attempt {Attempt}/3): {Msg}", depth, attempt+1, ex.Message);
                    await Task.Delay(3000 * (attempt + 1)); // Backoff
                }
            }

            if (navSuccess)
            {
                await AcceptCookiesAsync(page, cancellationToken);
                
                // RECURSE
                await NavigateAndCollectFromSubcategoriesAsync(
                    page, 
                    siteId, 
                    selectors, 
                    products, 
                    seenProducts, 
                    maxProducts, 
                    newPath, 
                    cancellationToken, 
                    depth + 1);
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

            product.ScreenshotBase64 = await CaptureScreenshotBase64Async(page, "product_detail");

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
            if (string.IsNullOrWhiteSpace(product.Title) && !product.Price.HasValue && !hasBarcode)
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

    private async Task<List<ScrapedProduct>> ExtractFestoProductsFromDetailPageAsync(
        IPage page,
        SiteSelectors selectors,
        List<string> categoryPath,
        CancellationToken cancellationToken)
    {
        var products = new List<ScrapedProduct>();
        
        try
        {
            _logger.LogInformation(">>> Iniciando extracción detallada en: {Url}", page.Url);
            // User requested 90s wait for heavy SPAs - explicitly wait for content appearance
            _logger.LogInformation("Esperando carga de contenido principal (hasta 90s)...");
            try {
                // Wait for ANY valid product content indicator:
                // 1. Variant Table Container
                // 2. Variant List Tab
                // 3. Variant List Items (direct)
                // 4. Single Product Headline
                // 5. Product Details Content
                await page.WaitForSelectorAsync(
                    "[class*='variants-table-container--'], " +
                    "[class*='tab-navigation-item--'], " + 
                    "[class*='article-list--'], " +
                    "[class*='product-page-headline--'], " +
                    "div[class*='product-details-content--']", 
                    new PageWaitForSelectorOptions { Timeout = 90000 });
            } 
            catch { 
                _logger.LogWarning("Timeout esperando selectores principales. Intentando extraer de todos modos.");
            }
            
            await Task.Delay(2000, cancellationToken); // Grace period after appearance

            // Descubrir URLs de navegación una vez por página
            var navigationUrls = await DiscoverRelatedProductUrlsAsync(page, cancellationToken);
            
            // Extract Family Description (Generic for all variants)
            string familyDescription = "";
            var descEl = await page.QuerySelectorAsync("div[class*='product-details-content--'], div[class*='text-image-section--']");
            if (descEl != null)
            {
                 familyDescription = (await descEl.InnerTextAsync())?.Trim() ?? "";
                 if (familyDescription.Length > 500) familyDescription = familyDescription.Substring(0, 500) + "..."; // Truncate if too long
            }
            
            var variantTableSelector = selectors.VariantTableSelector ?? "div[class*='variants-table-container--']";
            var variantTable = await page.QuerySelectorAsync(variantTableSelector);
            
            if (variantTable != null)
            {
                _logger.LogInformation("Página de familia de productos detectada - extrayendo variantes");
                
                var familyTitle = "";
                var titleLocator = page.Locator("h1");
                if (await titleLocator.CountAsync() > 0)
                {
                    familyTitle = (await titleLocator.First.InnerTextAsync())?.Trim() ?? "";
                }
                
                var variantRowSelector = selectors.VariantRowSelector ?? "tr[class*='product-row--']";
                var variantRows = await variantTable.QuerySelectorAllAsync(variantRowSelector);
                
                _logger.LogInformation("Encontradas {Count} variantes en la tabla", variantRows.Count);
                
                foreach (var row in variantRows)
                {
                    try
                    {
                        var product = new ScrapedProduct
                        {
                            ScrapedAt = DateTime.UtcNow,
                            Attributes = new Dictionary<string, string>(),
                            NavigationUrls = new List<string>(navigationUrls),
                            Description = familyDescription
                        };
                        
                        var skuLinkSelector = selectors.VariantSkuLinkSelector ?? "a[href*='/p/'], a[href*='/a/']";
                        var skuLink = await row.QuerySelectorAsync(skuLinkSelector);
                        if (skuLink != null)
                        {
                            var skuText = (await skuLink.InnerTextAsync())?.Trim();
                            if (!string.IsNullOrWhiteSpace(skuText))
                            {
                                product.SkuSource = skuText;
                                product.Title = $"{familyTitle} {skuText}";
                            }
                            
                            var href = await skuLink.GetAttributeAsync("href");
                            if (!string.IsNullOrWhiteSpace(href))
                            {
                                product.Attributes["variant_url"] = NormalizeHref(page.Url, href);
                            }
                        }
                        
                        var priceElements = await row.QuerySelectorAllAsync("div[class*='price-display-text--'], span[class*='price--']");
                        foreach (var priceEl in priceElements)
                        {
                            var priceText = (await priceEl.InnerTextAsync())?.Trim();
                            if (!string.IsNullOrWhiteSpace(priceText) && priceText.Contains("MXN"))
                            {
                                product.Price = ParsePrice(priceText);
                                product.Attributes["price_text"] = priceText;
                                break;
                            }
                        }
                        
                        var imageEl = await row.QuerySelectorAsync("img");
                        if (imageEl == null)
                        {
                            imageEl = await page.QuerySelectorAsync("img[class*='image--']");
                        }
                        if (imageEl != null)
                        {
                            product.ImageUrl = await imageEl.GetAttributeAsync("src");
                        }
                        
                        product.Attributes["product_url"] = page.Url;
                        product.Attributes["family_title"] = familyTitle;
                        
                        // Ensure SourceUrl is set for crawler seeding
                        if (product.Attributes.ContainsKey("variant_url"))
                        {
                            product.SourceUrl = product.Attributes["variant_url"];
                        }
                        else
                        {
                            product.SourceUrl = page.Url;
                        }

                        if (!string.IsNullOrWhiteSpace(product.SkuSource))
                        {
                            products.Add(product);
                            _logger.LogInformation("Variante extraída: {SKU} - Precio: {Price}", 
                                product.SkuSource, product.Price?.ToString() ?? "N/A");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error extrayendo variante de fila");
                    }
                }
            }
            else
            {
                // New Logic: Check for Tabbed Variant List (User Request)
                var tabSelector = "[class*='tab-navigation-item--']";
                var listSelector = "[class*='article-list--']";
                
                var tabNavigation = await page.QuerySelectorAsync(tabSelector);
                var articleList = await page.QuerySelectorAsync(listSelector);
                
                _logger.LogInformation("Debug: Verificando estructura de variantes. Tab: {HasTab}, Lista: {HasList}", 
                    tabNavigation != null, articleList != null);
                
                if (tabNavigation != null || articleList != null)
                {
                     _logger.LogInformation("Página de familia de productos detectada (TABS) - extrayendo variantes");
                     
                     // Ensure list is visible
                     if (articleList == null && tabNavigation != null)
                     {
                         _logger.LogInformation("Haciendo clic en pestaña de variantes para cargar lista...");
                         await tabNavigation.ClickAsync();
                         await Task.Delay(2000, cancellationToken); // Wait for click reaction
                         
                         // Wait for list to appear
                         try {
                             await page.WaitForSelectorAsync(listSelector, new PageWaitForSelectorOptions { Timeout = 10000 });
                         } catch { _logger.LogWarning("Timeout esperando a que cargue la lista tras clic."); }
                         
                         articleList = await page.QuerySelectorAsync(listSelector);
                     }
                     
                     if (articleList != null)
                     {
                        var articles = await articleList.QuerySelectorAllAsync("div[class*='single-article--']");
                        _logger.LogInformation("Encontrados {Count} artículos en lista de variantes", articles.Count);

                        foreach (var article in articles)
                        {
                            try 
                            {
                                var p = new ScrapedProduct
                                {
                                    ScrapedAt = DateTime.UtcNow,
                                    Attributes = new Dictionary<string, string>(),
                                    NavigationUrls = new List<string>(navigationUrls),
                                    Description = familyDescription
                                };
                                
                                var linkEl = await article.QuerySelectorAsync("div[class*='triad-article-link-wrapper--'] a");
                                if (linkEl != null)
                                {
                                    var href = await linkEl.GetAttributeAsync("href");
                                    if (!string.IsNullOrEmpty(href))
                                    {
                                        p.Attributes["variant_url"] = NormalizeHref(page.Url, href);
                                        p.SourceUrl = p.Attributes["variant_url"];
                                    }
                                }
                                
                                var titleEl = await article.QuerySelectorAsync("a[class*='triad-title--']");
                                if (titleEl != null) p.Title = (await titleEl.InnerTextAsync())?.Trim();
                                
                                var triadSkuEl = await article.QuerySelectorAsync("span[class*='triad-order-code--']");
                                if (triadSkuEl != null) p.SkuSource = (await triadSkuEl.InnerTextAsync())?.Trim();
                                
                                p.Attributes["product_url"] = page.Url;

                                if (!string.IsNullOrWhiteSpace(p.SkuSource)) {
                                    products.Add(p);
                                    _logger.LogInformation("Variante(Lista) OK: {SKU}", p.SkuSource);
                                }
                            }
                            catch {}
                        }
                        if (products.Count > 0) return products;
                     }
                }

                _logger.LogInformation("Página de producto simple detectada");
                
                var product = new ScrapedProduct
                {
                    RawHtml = await page.ContentAsync(),
                    ScrapedAt = DateTime.UtcNow,
                    Attributes = new Dictionary<string, string>(),
                    NavigationUrls = new List<string>(navigationUrls)
                };
                
                // Extraer Título (Headline + Order Code)
                var headlineEl = await page.QuerySelectorAsync("[class*='product-page-headline--']");
                var orderCodeEl = await page.QuerySelectorAsync("[class*='order-code--']");
                
                if (headlineEl != null)
                {
                    var headline = (await headlineEl.InnerTextAsync())?.Trim() ?? "";
                    var orderCode = orderCodeEl != null ? (await orderCodeEl.InnerTextAsync())?.Trim() ?? "" : "";
                    
                    if (!string.IsNullOrEmpty(orderCode))
                    {
                        product.Title = $"{headline} {orderCode}".Trim();
                        product.SkuSource = orderCode;
                        product.Attributes["order_code"] = orderCode;
                    }
                    else
                    {
                        product.Title = headline;
                    }
                }

                if (string.IsNullOrEmpty(product.Title))
                {
                    var titleLocator = page.Locator("h1");
                    if (await titleLocator.CountAsync() > 0)
                    {
                        product.Title = (await titleLocator.First.InnerTextAsync())?.Trim();
                    }
                }
                
                var skuSelector = selectors.DetailSkuSelector ?? "span[class*='part-number-value--'], .part-number";
                var skuEl = await page.QuerySelectorAsync(skuSelector);
                if (skuEl != null)
                {
                    var skuVal = (await skuEl.InnerTextAsync())?.Trim();
                    if (string.IsNullOrEmpty(product.SkuSource)) {
                        product.SkuSource = skuVal;
                    }
                    product.Attributes["part_number"] = skuVal ?? "";
                }
                
                if (string.IsNullOrWhiteSpace(product.SkuSource) && !string.IsNullOrWhiteSpace(product.Title))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(product.Title, @"[A-Z]{2,}[-\w]+");
                    if (match.Success)
                    {
                        product.SkuSource = match.Value;
                    }
                }
                
                var priceSelector = selectors.DetailPriceSelector ?? "div[class*='price-display-text--'], .price";
                var priceEl = await page.QuerySelectorAsync(priceSelector);
                if (priceEl != null)
                {
                    var priceText = (await priceEl.InnerTextAsync())?.Trim();
                    product.Price = ParsePrice(priceText ?? "");
                    product.Attributes["price_text"] = priceText ?? "";
                }
                
                var imageEl = await page.QuerySelectorAsync("img[class*='image--']");
                if (imageEl != null)
                {
                    product.ImageUrl = await imageEl.GetAttributeAsync("src");
                }
                
                product.Attributes["product_url"] = page.Url;
                product.SourceUrl = page.Url; // FIXED: Explicitly set SourceUrl

                // Extraer datos técnicos
                await ExtractFestoTechnicalDataAsync(page, product, cancellationToken);
                
                if (!string.IsNullOrWhiteSpace(product.SkuSource))
                {
                    products.Add(product);
                    _logger.LogInformation("OK - Producto Individual: SKU={SKU}, Title='{Title}', Price={Price}", product.SkuSource, product.Title, product.Price);
                }
                else
                {
                    _logger.LogWarning("FALLO - Producto Individual: No se pudo extraer SKU. Title='{Title}', URL={Url}", product.Title, page.Url);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extrayendo productos de página de detalle de Festo");
        }
        
        return products;
    }

    private async Task ExtractFestoTechnicalDataAsync(IPage page, ScrapedProduct product, CancellationToken cancellationToken)
    {
        try
        {
            // 1. Click en la pestaña de Datos técnicos o similar
            var tabs = await page.QuerySelectorAllAsync("[class*='product-details-tabs__list-item']");
            foreach (var tab in tabs)
            {
                var text = await tab.InnerTextAsync();
                if (text != null && (text.Contains("Datos técnicos") || text.Contains("Technical data")))
                {
                    _logger.LogInformation("Haciendo click en pestaña de datos técnicos");
                    await tab.ClickAsync();
                    await Task.Delay(1000, cancellationToken);
                    break;
                }
            }

            // 2. Extraer datos de la tabla
            var rows = await page.QuerySelectorAllAsync("[class*='technical-data-table-row--']");
            if (rows.Count == 0)
            {
                // Re-intentar buscar si la tabla no aparece inmediatamente
                await Task.Delay(1000, cancellationToken);
                rows = await page.QuerySelectorAllAsync("[class*='technical-data-table-row--']");
            }

            foreach (var row in rows)
            {
                var propEl = await row.QuerySelectorAsync("[class*='technical-data-property--']");
                var valEl = await row.QuerySelectorAsync("[class*='technical-data-value--']");
                
                if (propEl != null && valEl != null)
                {
                    var propName = (await propEl.InnerTextAsync())?.Trim();
                    var propValue = (await valEl.InnerTextAsync())?.Trim();
                    
                    if (!string.IsNullOrEmpty(propName) && propValue != null)
                    {
                        var key = $"tech_{propName.ToLower().Replace(" ", "_").Replace(":", "")}";
                        product.Attributes[key] = propValue;
                    }
                }
            }
            
            _logger.LogInformation("Extraídos {Count} atributos técnicos", product.Attributes.Count(a => a.Key.StartsWith("tech_")));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error extrayendo datos técnicos de Festo");
        }
    }

    private async Task<bool> NavigateBackViaFestoBreadcrumbAsync(IPage page, CancellationToken cancellationToken)
    {
        try
        {
            // Buscar todos los items del breadcrumb usando coincidencia parcial de clase
            var breadcrumbItems = await page.QuerySelectorAllAsync("li[class*='breadcrumb-item']");
            if (breadcrumbItems.Count >= 2)
            {
                // El penúltimo elemento suele ser la categoría padre (VAMC en el ejemplo del usuario)
                var parentCategoryItem = breadcrumbItems[breadcrumbItems.Count - 2];
                var link = await parentCategoryItem.QuerySelectorAsync("a[class*='breadcrumb-item__link']");
                
                if (link != null)
                {
                    var linkText = await link.InnerTextAsync();
                    var href = await link.GetAttributeAsync("href");
                    _logger.LogInformation("Estrategia Breadcrumb: Detectada categoría padre '{Category}'. Navegando a URL: {Url}", linkText?.Trim(), href);
                    
                    await link.ClickAsync();
                    await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
                    await Task.Delay(1000, cancellationToken);
                    return true;
                }
                else
                {
                    _logger.LogDebug("Estrategia Breadcrumb: El penúltimo ítem no contiene un enlace clickeable.");
                }
            }
            else
            {
                _logger.LogDebug("Estrategia Breadcrumb: No hay suficientes elementos en el breadcrumb (encontrados: {Count}).", breadcrumbItems.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Estrategia de breadcrumb falló o no disponible: {Message}", ex.Message);
        }
        return false;
    }

    private async Task<List<string>> LoadFestoExampleUrlsAsync()
    {
        try
        {
            var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "festo_example_urls.json");
            if (!File.Exists(filePath))
            {
                // Reintentar en el directorio de ejecución si estamos en desarrollo
                filePath = Path.Combine(Directory.GetCurrentDirectory(), "festo_example_urls.json");
            }

            if (File.Exists(filePath))
            {
                var json = await File.ReadAllTextAsync(filePath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("inspectRequest", out var inspect) && 
                    inspect.TryGetProperty("urls", out var urls))
                {
                    return urls.EnumerateArray().Select(u => u.GetString() ?? "").Where(u => !string.IsNullOrEmpty(u)).ToList();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("No se pudo cargar festo_example_urls.json: {Message}", ex.Message);
        }
        return new List<string>();
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
            selectors.CategoryLandingUrl ??= GetSelector(root, "CategoryLandingUrl", "categoryLandingUrl", "category_landing_url");
            selectors.CategoryLinkSelector ??= GetSelector(root, "CategoryLinkSelector", "categoryLinkSelector", "category_link_selector");
            selectors.CategoryNameSelector ??= GetSelector(root, "CategoryNameSelector", "categoryNameSelector", "category_name_selector");
            selectors.SearchInputSelector ??= GetSelector(root, "SearchInputSelector", "searchInputSelector", "search_input_selector");
            selectors.SearchButtonSelector ??= GetSelector(root, "SearchButtonSelector", "searchButtonSelector", "search_button_selector");
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
            
            // Nuevas propiedades para modo families
            selectors.ScrapingMode ??= GetSelector(root, "ScrapingMode", "scrapingMode", "scraping_mode");
            selectors.ProductFamilyLinkSelector ??= GetSelector(root, "ProductFamilyLinkSelector", "productFamilyLinkSelector", "product_family_link_selector");
            selectors.ProductFamilyLinkText ??= GetSelector(root, "ProductFamilyLinkText", "productFamilyLinkText", "product_family_link_text");
            selectors.VariantTableSelector ??= GetSelector(root, "VariantTableSelector", "variantTableSelector", "variant_table_selector");
            selectors.VariantRowSelector ??= GetSelector(root, "VariantRowSelector", "variantRowSelector", "variant_row_selector");
            selectors.VariantSkuLinkSelector ??= GetSelector(root, "VariantSkuLinkSelector", "variantSkuLinkSelector", "variant_sku_link_selector");
            selectors.DetailSkuSelector ??= GetSelector(root, "DetailSkuSelector", "detailSkuSelector", "detail_sku_selector");
            selectors.DetailPriceSelector ??= GetSelector(root, "DetailPriceSelector", "detailPriceSelector", "detail_price_selector");
            
            // Nuevas propiedades para extracción profunda de detalles
            selectors.VariantDetailLinkSelector ??= GetSelector(root, "VariantDetailLinkSelector", "variantDetailLinkSelector", "variant_detail_link_selector");
            selectors.DetailTitleSelector ??= GetSelector(root, "DetailTitleSelector", "detailTitleSelector", "detail_title_selector");
            selectors.DetailDescriptionSelector ??= GetSelector(root, "DetailDescriptionSelector", "detailDescriptionSelector", "detail_description_selector");
            selectors.DetailImageSelector ??= GetSelector(root, "DetailImageSelector", "detailImageSelector", "detail_image_selector");


            if (selectors.CategoryUrls == null || selectors.CategoryUrls.Count == 0)
            {
                if (TryGetStringList(root, out var urls, "CategoryUrls", "categoryUrls", "category_urls"))
                {
                    selectors.CategoryUrls = urls;
                }
            }

            if (selectors.MaxPages <= 0)
            {
                if (TryGetInt(root, out var maxPages, "MaxPages", "maxPages", "max_pages"))
                {
                    selectors.MaxPages = maxPages;
                }
            }

            if (selectors.CategorySearchTerms.Count == 0 &&
                TryGetStringList(root, out var terms, "CategorySearchTerms", "categorySearchTerms", "category_search_terms"))
            {
                selectors.CategorySearchTerms = terms;
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

    private static bool TryGetStringList(JsonElement root, out List<string> result, params string[] names)
    {
        result = new List<string>();
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var text = item.GetString();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            result.Add(text.Trim());
                        }
                    }
                }
            }

            if (result.Count > 0)
            {
                return true;
            }
        }

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
        
        // Intentar usar el directorio de sesiones del API si existe
        var apiSessionsPath = Path.Combine(Directory.GetCurrentDirectory(), "sessions");
        if (Directory.Exists(apiSessionsPath))
        {
            return Path.Combine(apiSessionsPath, $"{safeName}_state.json");
        }
        
        // Fallback al temp path si no estamos en el directorio esperado
        return Path.Combine(Path.GetTempPath(), $"scrapsae_{safeName}_state.json");
    }

    private async Task<IBrowserContext> CreateBrowserContextAsync(IBrowser browser, string siteName)
    {
        var options = new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36",
            JavaScriptEnabled = true,
            AcceptDownloads = true
        };

        var statePath = GetStorageStatePath(siteName);
        if (File.Exists(statePath))
        {
            try 
            {
                options.StorageStatePath = statePath;
                _logger.LogInformation("Loaded session state from {Path}", statePath);
            }
            catch (Exception ex)
            {
                 _logger.LogWarning("Failed to load session state: {Message}. Creating fresh context.", ex.Message);
            }
        }

        return await browser.NewContextAsync(options);
    }

    private async Task ManualLoginFallbackInExistingPageAsync(IPage page, SiteProfile site, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Manual Login Fallback for {SiteName} in existing browser...", site.Name);
        
        try
        {
            var context = page.Context;
            var loginUrl = !string.IsNullOrEmpty(site.LoginUrl) ? site.LoginUrl : site.BaseUrl;
            
            // Solo navegar si no estamos ya en la página de login o si acabamos de abrir el browser
            if (page.Url == "about:blank" || !page.Url.Contains("festo.com", StringComparison.OrdinalIgnoreCase))
            {
                await page.GotoAsync(loginUrl);
            }
            
            _logger.LogInformation("=============================================================================");
            _logger.LogInformation("PLEASE LOG IN MANUALLY IN THE BROWSER WINDOW.");
            _logger.LogInformation("Waiting for confirmation signal via API...");
            _logger.LogInformation("=============================================================================");

            // Wait for signal from API
            await _signalService.WaitForLoginConfirmationAsync(site.Id.ToString(), cancellationToken);

            _logger.LogInformation("Manual login confirmation received! Saving session state...");
            var statePath = GetStorageStatePath(site.Name);
            await context.StorageStateAsync(new() { Path = statePath });
            _logger.LogInformation("Session state saved to {Path}", statePath);

            try
            {
                var landingUrl = site.BaseUrl;
                if (!string.IsNullOrWhiteSpace(landingUrl))
                {
                    _logger.LogInformation("Navigating to landing URL: {Url}", landingUrl);
                    await page.GotoAsync(landingUrl, new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = 90000
                    });
                    await AcceptCookiesAsync(page, cancellationToken);
                    
                    var readyShot = await SaveStepScreenshotAsync(page, $"{site.Name}_manual_login_ready");
                    await LogStepAsync(site.Id, "success", "Login manual completado y confirmado.", new { url = page.Url, screenshotFile = readyShot });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Manual login completed, but landing page preparation failed.");
                await LogStepAsync(site.Id, "warn", "Login manual completado, pero no se pudo preparar la landing.");
            }
        }
        finally
        {
            // Reset env var
            Environment.SetEnvironmentVariable("SCRAPSAE_MANUAL_LOGIN_ACTIVE", null);
        }
    }

    public async Task<List<ScrapedProduct>> ScrapeDirectUrlsAsync(List<string> urls, Guid siteId, bool inspectOnly, CancellationToken cancellationToken)
    {
        var products = new List<ScrapedProduct>();
        var seenProducts = new HashSet<string>();
        
        // Buscar sitio y selectores
        var site = _sites.FirstOrDefault(s => s.Id == siteId);
        if (site == null)
        {
            _logger.LogWarning("Sitio no encontrado para inspección directa: {SiteId}", siteId);
            return products;
        }

        var selectorsJson = "";
        if (site.Selectors is JsonElement jsonElement)
            selectorsJson = jsonElement.GetRawText();
        else if (site.Selectors is string s)
            selectorsJson = s;
        else
            selectorsJson = JsonConvert.SerializeObject(site.Selectors);

        var selectors = JsonConvert.DeserializeObject<SiteSelectors>(selectorsJson) ?? new SiteSelectors();
        FillSelectorsFromJson(selectors, selectorsJson);

        // Iniciar navegador
        var browser = await _browserSharing.GetBrowserAsync();
        var context = await CreateBrowserContextAsync(browser, site.Name ?? "default");
        var page = await context.NewPageAsync();

        try 
        {
            await LogStepAsync(siteId, "info", $"🚀 Iniciando inspección directa de {urls.Count} URLs", new { count = urls.Count });

            foreach (var url in urls)
            {
                if (cancellationToken.IsCancellationRequested) break;

                try 
                {
                    // Emular comportamiento humano antes de navegar
                    await SimulateHumanBehaviorAsync(page, cancellationToken);

                    _logger.LogInformation("Inspeccionando URL: {Url}", url);
                    await LogStepAsync(siteId, "info", $"🌐 Navegando a: {url}", new { url });
                    
                    await HumanNavigateAsync(page, url, WaitUntilState.NetworkIdle);
                    await AcceptCookiesAsync(page, cancellationToken);
                    
                    // Verificar sesión
                    await LogStepAsync(siteId, "info", "🔒 Verificando estado de la sesión...", null);
                    await EnsureSessionActiveAsync(page, siteId, cancellationToken);
                    await LogStepAsync(siteId, "info", "✅ Sesión activa y lista.", null);

                    // Screenshot
                    var screenshot = await SaveStepScreenshotAsync(page, $"inspect_{Path.GetFileName(new Uri(url).AbsolutePath)}");
                    
                    // Extracción Deep
                    await LogStepAsync(siteId, "info", "🧪 Extrayendo información técnica (Deep Extraction)...", null);
                    // Update: The error was in ExtractProductFromDetailPageDeepAsync call, but here it says ExtractFestoProductsFromDetailPageAsync.
                    // Let me check ScrapeDirectUrlsAsync implementation again.
                    // Ah, line 4376 in the original file (which is ExtractProductFromDetailPageDeepAsync) called DiscoverRelatedProductUrlsAsync(page, cancellationToken).
                    // This usage was inside ExtractProductFromDetailPageDeepAsync.
                    // Now I need to update the CALLERS of ExtractProductFromDetailPageDeepAsync.
                    // Wait, I am looking at ScrapeDirectUrlsAsync (around 3800).
                    // In the previous view it used ExtractFestoProductsFromDetailPageAsync.
                    // I need to find where ExtractProductFromDetailPageDeepAsync is CALLED.
                    
                    var extractedProducts = await ExtractFestoProductsFromDetailPageAsync(page, selectors, new List<string>(), cancellationToken);
                    
                    if (extractedProducts != null && extractedProducts.Any())
                    {
                        foreach (var product in extractedProducts)
                        {
                            product.SourceUrl = url;
                            product.ScreenshotBase64 = screenshot;
                            if (seenProducts.Add(GetProductKey(product)))
                            {
                                products.Add(product);
                                await LogStepAsync(siteId, "success", $"Producto extraído: {product.Title}", new { sku = product.SkuSource, url });
                            }
                        }

                        // UNIFIED CRAWLER: Use this page as a seed for further discovery
                        if (url.Contains("festo.com") || (site.Name != null && site.Name.Contains("Festo", StringComparison.OrdinalIgnoreCase)))
                        {
                            _logger.LogInformation("URL Directa -> Iniciando Crawler Profundo...");
                            var queue = new Queue<string>();
                            
                            // Discover neighbors on this page
                            var neighbors = await DiscoverRelatedProductUrlsAsync(page, cancellationToken);
                            foreach(var n in neighbors) 
                                if(!seenProducts.Contains(n)) queue.Enqueue(n);
                                
                            // Try breadcrumb fallback only if queue is empty or low? 
                            // No, let's do both. Breadcrumb goes UP, Crawler goes SIDEWAYS.
                            
                            // 1. Sideways Crawl
                            if (queue.Count > 0)
                            {
                                await CrawlProductsFromSeedsAsync(page, siteId, selectors, products, seenProducts, queue, cancellationToken);
                            }

                            // 2. Breadcrumb Up (Original Logic) - kept for robustness
                            await LogStepAsync(siteId, "info", "🔍 Intentando expandir vía breadcrumbs (Hacia arriba)...", null);
                            if (await NavigateBackViaFestoBreadcrumbAsync(page, cancellationToken))
                            {
                                await NavigateAndCollectFromSubcategoriesAsync(
                                    page,
                                    siteId,
                                    selectors,
                                    products,
                                    seenProducts,
                                    10000, // Reduced limits as requested (effectively high)
                                    new List<string> { "Recursivo" },
                                    cancellationToken,
                                    depth: 0);
                            }
                        }
                    }
                    else
                    {
                        await LogStepAsync(siteId, "warning", $"No se pudo extraer producto", new { url });
                    }

                    // Pausa humanizada
                    await HumanDelayAsync(1000, 3000);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error inspeccionando URL: {Url}", url);
                    await LogStepAsync(siteId, "error", $"Error: {ex.Message}", new { url });
                }
            }
        }
        finally
        {
            await context.CloseAsync();
        }

        return products;
    }


// Nuevos métodos para el modo "families" de scraping
// Estos métodos deben agregarse a la clase PlaywrightScrapingService

/// <summary>
/// Método principal para scraping en modo "families" (estilo Festo)
/// </summary>
private async Task<bool> TryScrapeFamiliesModeAsync(
    IPage page,
    SiteProfile site,
    SiteSelectors selectors,
    List<ScrapedProduct> products,
    HashSet<string> seenProducts,
    int maxProducts,
    CancellationToken cancellationToken)
{
    try
    {
        await LogStepAsync(site.Id, "info", "Iniciando scraping en modo families", new { mode = "families" });
        
        // Obtener las URLs de categorías desde la configuración
        var categoryUrls = selectors.CategoryUrls ?? new List<string>();
        
        if (categoryUrls.Count == 0)
        {
            // Usar la página actual como punto de partida si no hay URLs configuradas
            await LogStepAsync(site.Id, "info", "No se encontraron URLs de categorías, usando página actual", new { url = page.Url });
            categoryUrls = new List<string> { page.Url };
        }

        
        foreach (var categoryUrl in categoryUrls)
        {
            if (products.Count >= maxProducts)
            {
                await LogStepAsync(site.Id, "info", "Límite de productos alcanzado", new { count = products.Count });
                break;
            }
            
            await LogStepAsync(site.Id, "info", $"Procesando categoría: {categoryUrl}", null);
            
            // Navegar a la URL de la categoría con comportamiento humano
            await HumanNavigateAsync(page, categoryUrl, WaitUntilState.NetworkIdle);
            
            var screenshot = await SaveStepScreenshotAsync(page, $"category_{Path.GetFileName(categoryUrl)}");
            
            // Buscar enlaces de familias de productos
            var familyLinks = await CollectFamilyLinksAsync(page, selectors);
            await LogStepAsync(site.Id, "info", $"Encontrados {familyLinks.Count} enlaces de familias", new { screenshotFile = screenshot });
            
            // Procesar cada familia
            foreach (var familyLink in familyLinks)
            {
                if (products.Count >= maxProducts)
                    break;
                    
                await _scrapeControl.WaitIfPausedAsync(site.Id, cancellationToken);
                
                // Pausa humanizada antes de procesar la siguiente familia
                await HumanDelayAsync(2000, 4000);
                
                try
                {
                    var familyProducts = await ExtractProductsFromFamilyPageAsync(
                        page,
                        site.Id,
                        familyLink,
                        selectors,
                        seenProducts,
                        maxProducts - products.Count,
                        cancellationToken);
                    
                    products.AddRange(familyProducts);
                    await LogStepAsync(site.Id, "info", $"Extraídos {familyProducts.Count} productos de la familia", new { url = familyLink });
                }
                catch (Exception ex)
                {
                    await LogStepAsync(site.Id, "error", $"Error procesando familia: {ex.Message}", new { url = familyLink });
                }
            }
        }
        
        return products.Count > 0;
    }
    catch (Exception ex)
    {
        await LogStepAsync(site.Id, "error", $"Error en modo families: {ex.Message}", null);
        return false;
    }
}

/// <summary>
/// Recolecta los enlaces de familias de productos de una página de categoría
/// </summary>
private async Task<List<string>> CollectFamilyLinksAsync(IPage page, SiteSelectors selectors)
{
    var links = new List<string>();
    
    try
    {
        // Limpiar banners de cookies que puedan obstruir clics
        await AcceptCookiesAsync(page, CancellationToken.None);

        // Hacer scroll gradual humanizado para cargar los productos sugeridos
        _logger.LogInformation("Haciendo scroll gradual para cargar productos sugeridos...");
        await HumanScrollAsync(page, scrolls: 4);
        
        await SaveStepScreenshotAsync(page, "festo_category_scroll_done");
        
        // Buscar enlaces usando el selector o el texto configurado
        ILocator linkLocator;
        
        if (!string.IsNullOrEmpty(selectors.ProductFamilyLinkSelector))
        {
            _logger.LogInformation("Buscando familias con selector: {Selector}", selectors.ProductFamilyLinkSelector);
            linkLocator = page.Locator(selectors.ProductFamilyLinkSelector);
        }
        else if (!string.IsNullOrEmpty(selectors.ProductFamilyLinkText))
        {
            _logger.LogInformation("Buscando familias con texto: {Text}", selectors.ProductFamilyLinkText);
            linkLocator = page.Locator($"a:has-text('{selectors.ProductFamilyLinkText}')");
        }
        else
        {
            // Fallback: Detectar automáticamente enlaces de categorías/familias en Festo
            _logger.LogInformation("Intentando detectar enlaces de familias automáticamente...");
            
            // Selectores comunes para tarjetas de categoría en Festo
            var fallbackSelectors = new[]
            {
                // Tarjetas de categoría con imagen y texto
                "a[class*='category-card--'], a[class*='category-tile--']",
                "a[class*='product-family--'], a[class*='product-category--']",
                // Enlaces dentro de contenedores de categorías
                "div[class*='category-list--'] a, div[class*='categories--'] a",
                "div[class*='family-list--'] a, div[class*='families--'] a",
                // Enlaces con imágenes de productos (tarjetas típicas)
                "a:has(img):has(span), a:has(img):has(div)",
                // Selectores de Festo específicos
                "[class*='pim-category-'] a, [class*='product-group-'] a",
                // Cualquier enlace que contenga /c/ en la URL (categorías de Festo)
                "a[href*='/c/productos/'], a[href*='/catalog/']"
            };
            
            foreach (var selector in fallbackSelectors)
            {
                try
                {
                    var testLocator = page.Locator(selector);
                    var testCount = await testLocator.CountAsync();
                    if (testCount > 0)
                    {
                        _logger.LogInformation("Fallback selector encontró {Count} elementos: {Selector}", testCount, selector);
                        linkLocator = testLocator;
                        goto FoundLinks;
                    }
                }
                catch { }
            }
            
            // Último fallback: Buscar cualquier enlace que parezca una categoría
            _logger.LogWarning("No se encontraron enlaces con selectores de fallback, buscando enlaces genéricos...");
            linkLocator = page.Locator("main a[href*='/c/'], main a[href*='/productos/'], main a[href*='/category/']");
            
            FoundLinks:;
        }

        
        var count = await linkLocator.CountAsync();
        _logger.LogInformation($"Encontrados {count} enlaces de familias en la página");
        
        // Extraer las URLs de los enlaces
        for (int i = 0; i < count; i++)
        {
            try
            {
                var href = await linkLocator.Nth(i).GetAttributeAsync("href");
                if (!string.IsNullOrEmpty(href))
                {
                    // Convertir a URL absoluta si es necesario
                    if (!href.StartsWith("http"))
                    {
                        var baseUrl = new Uri(page.Url);
                        href = new Uri(baseUrl, href).ToString();
                    }
                    
                    if (!links.Contains(href))
                    {
                        links.Add(href);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error extrayendo enlace {i}: {ex.Message}");
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogError($"Error recolectando enlaces de familias: {ex.Message}");
    }
    
    return links;
}

/// <summary>
/// Extrae productos de una página de familia (con tabla de variantes)
/// </summary>
private async Task<List<ScrapedProduct>> ExtractProductsFromFamilyPageAsync(
    IPage page,
    Guid siteId,
    string familyUrl,
    SiteSelectors selectors,
    HashSet<string> seenProducts,
    int maxProducts,
    CancellationToken cancellationToken)
{
    var products = new List<ScrapedProduct>();
    
    try
    {
        // Limpiar banners de cookies que puedan obstruir clics en la tabla de variantes
        await AcceptCookiesAsync(page, CancellationToken.None);

        // Navegar a la página de la familia con comportamiento humano
        await HumanNavigateAsync(page, familyUrl, WaitUntilState.NetworkIdle);
        
        // Extraer el título de la familia
        string? familyTitle = null;
        if (!string.IsNullOrEmpty(selectors.TitleSelector))
        {
            var titleElem = page.Locator(selectors.TitleSelector).First;
            if (await titleElem.CountAsync() > 0)
            {
                familyTitle = await titleElem.TextContentAsync();
            }
        }
        
        // Buscar la tabla de variantes
        if (string.IsNullOrEmpty(selectors.VariantTableSelector))
        {
            await LogStepAsync(siteId, "warning", "No se configuró selector de tabla de variantes", null);
            return products;
        }
        
        var variantTable = page.Locator(selectors.VariantTableSelector).First;
        if (await variantTable.CountAsync() == 0)
        {
            await LogStepAsync(siteId, "info", "No se encontró tabla de variantes en esta familia", new { url = familyUrl });
            return products;
        }
        
        // Extraer las filas de variantes
        if (string.IsNullOrEmpty(selectors.VariantRowSelector))
        {
            await LogStepAsync(siteId, "warning", "No se configuró selector de filas de variantes", null);
            return products;
        }
        
        var variantRows = page.Locator(selectors.VariantRowSelector);
        var rowCount = await variantRows.CountAsync();
        await LogStepAsync(siteId, "info", $"Encontradas {rowCount} variantes en la familia", new { familyTitle });
        
        // FASE 1: Recolectar todas las URLs de detalle de las variantes
        var variantUrls = new List<(int index, string url, string? sku)>();
        var detailLinkSelector = selectors.VariantDetailLinkSelector ?? selectors.VariantSkuLinkSelector ?? "a[href*='/p/']";
        
        for (int i = 0; i < rowCount && variantUrls.Count < maxProducts; i++)
        {
            try
            {
                var row = variantRows.Nth(i);
                var link = row.Locator(detailLinkSelector).First;
                
                if (await link.CountAsync() > 0)
                {
                    var href = await link.GetAttributeAsync("href");
                    var skuPreview = await link.TextContentAsync();
                    skuPreview = skuPreview?.Trim();
                    
                    if (!string.IsNullOrEmpty(href))
                    {
                        var fullUrl = NormalizeHref(familyUrl, href);
                        
                        // Skip if already seen
                        if (!string.IsNullOrEmpty(skuPreview) && seenProducts.Contains(skuPreview))
                            continue;
                            
                        variantUrls.Add((i, fullUrl, skuPreview));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Error recolectando URL de variante {Index}: {Message}", i, ex.Message);
            }
        }
        
        _logger.LogInformation("Recolectadas {Count} URLs de variantes para extracción profunda", variantUrls.Count);
        await LogStepAsync(siteId, "info", $"URLs de variantes recolectadas: {variantUrls.Count}", null);
        
        // FASE 2: Navegar a cada página de detalle y extraer todos los datos
        foreach (var (index, detailUrl, skuPreview) in variantUrls)
        {
            if (products.Count >= maxProducts || cancellationToken.IsCancellationRequested)
                break;
                
            try
            {
                _logger.LogInformation("Extrayendo variante {Index}/{Total} desde página de detalle: {Url}", 
                    index + 1, variantUrls.Count, detailUrl);
                
                // Navegar a la página de detalle
                await HumanNavigateAsync(page, detailUrl, WaitUntilState.NetworkIdle);
                await AcceptCookiesAsync(page, cancellationToken);
                
                // Extraer todos los datos desde la página de detalle
                var product = await ExtractProductFromDetailPageDeepAsync(page, selectors, siteId, familyTitle, cancellationToken);
                
                if (product != null && !string.IsNullOrEmpty(product.SkuSource))
                {
                    if (seenProducts.Add(product.SkuSource))
                    {
                        product.Attributes["family_url"] = familyUrl;
                        product.Attributes["family_title"] = familyTitle ?? "";
                        product.Attributes["variant_index"] = index.ToString();
                        product.Attributes["detail_url"] = detailUrl;
                        
                        products.Add(product);
                        _logger.LogInformation("Variante extraída (profunda): SKU={Sku}, Título={Title}, Precio={Price}", 
                            product.SkuSource, product.Title, product.Price?.ToString() ?? "N/A");
                    }
                }
                else
                {
                    _logger.LogWarning("No se pudo extraer producto válido de: {Url}", detailUrl);
                }
                
                // Pausa humana entre páginas
                await HumanDelayAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error extrayendo variante {Index} desde {Url}: {Message}", 
                    index, detailUrl, ex.Message);
                await LogStepAsync(siteId, "warning", $"Error en variante {index}", new { url = detailUrl, error = ex.Message });
            }
        }
    }
    catch (Exception ex)
    {
        await LogStepAsync(siteId, "error", $"Error extrayendo productos de familia: {ex.Message}", new { url = familyUrl });
    }
    
    return products;
}

/// <summary>
/// Extrae un producto con máxima precisión desde una página de detalle
/// </summary>
private async Task<ScrapedProduct?> ExtractProductFromDetailPageDeepAsync(
    IPage page,
    SiteSelectors selectors,
    Guid siteId,
    string? familyTitle,
    CancellationToken cancellationToken)
{
    try
    {
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await Task.Delay(1000); // Pequeña espera para que se carguen elementos dinámicos
        
        var product = new ScrapedProduct
        {
            ScrapedAt = DateTime.UtcNow,
            Attributes = new Dictionary<string, string>
            {
                ["product_url"] = page.Url
            }
        };
        
        // 1. Extraer SKU (prioridad máxima) - Múltiples estrategias para Festo
        var skuSelector = selectors.DetailSkuSelector ?? "span[class*='part-number'], .sku, [data-testid*='sku']";
        var skuElem = page.Locator(skuSelector).First;
        if (await skuElem.CountAsync() > 0)
        {
            product.SkuSource = (await skuElem.TextContentAsync())?.Trim();
        }
        
        // Fallback 1: Buscar el número de artículo de Festo (patrón típico como "5249943")
        if (string.IsNullOrEmpty(product.SkuSource))
        {
            // El artículo suele mostrarse debajo del modelo (ej: bajo "DSNU-12-70-P-A")
            var articleElem = page.Locator("h1 + div, h1 ~ div:first-of-type, [class*='article-number--'], [class*='product-number--']").First;
            if (await articleElem.CountAsync() > 0)
            {
                var text = (await articleElem.TextContentAsync())?.Trim();
                // Buscar patrón de número de artículo (7 dígitos típico de Festo)
                var match = System.Text.RegularExpressions.Regex.Match(text ?? "", @"\b\d{6,8}\b");
                if (match.Success)
                {
                    product.SkuSource = match.Value;
                    _logger.LogInformation("SKU extraído de número de artículo: {Sku}", product.SkuSource);
                }
            }
        }
        
        // Fallback 2: Extraer de la URL (formato: /a/5249943/)
        if (string.IsNullOrEmpty(product.SkuSource))
        {
            var urlMatch = System.Text.RegularExpressions.Regex.Match(page.Url, @"/a/(\d+)/?");
            if (urlMatch.Success)
            {
                product.SkuSource = urlMatch.Groups[1].Value;
                _logger.LogInformation("SKU extraído de URL: {Sku}", product.SkuSource);
            }
        }
        
        // Fallback 3: Buscar GTIN/Código de barras
        if (string.IsNullOrEmpty(product.SkuSource))
        {
            var gtinElem = page.Locator("[class*='gtin--'], [class*='order-code--'], text=Código de barras").First;
            if (await gtinElem.CountAsync() > 0)
            {
                var gtinText = (await gtinElem.TextContentAsync())?.Trim();
                // Extraer número del texto (puede contener "Código de barras / GTIN: 4052568472080")
                var gtinMatch = System.Text.RegularExpressions.Regex.Match(gtinText ?? "", @"\b\d{8,14}\b");
                if (gtinMatch.Success)
                {
                    product.SkuSource = gtinMatch.Value;
                    product.Attributes["gtin"] = gtinMatch.Value;
                    _logger.LogInformation("SKU extraído de GTIN: {Sku}", product.SkuSource);
                }
            }
        }
        
        // Fallback 4: Buscar en todo el contenido visible patrones de número de artículo
        if (string.IsNullOrEmpty(product.SkuSource))
        {
            try
            {
                var pageContent = await page.ContentAsync();
                // Buscar patrones típicos de Festo: número de 7 dígitos
                var articleMatch = System.Text.RegularExpressions.Regex.Match(pageContent, @"(?:art[íi]culo|article|product).*?(\d{6,8})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (articleMatch.Success)
                {
                    product.SkuSource = articleMatch.Groups[1].Value;
                    _logger.LogInformation("SKU extraído de contenido de página: {Sku}", product.SkuSource);
                }
            }
            catch { }
        }

        
        // 2. Extraer Título - Intentar capturar nombre y modelo por separado
        var titleSelector = selectors.DetailTitleSelector ?? selectors.TitleSelector ?? "h1";
        var titleElem = page.Locator(titleSelector).First;
        if (await titleElem.CountAsync() > 0)
        {
            product.Title = (await titleElem.TextContentAsync())?.Trim();
        }
        
        // Intentar extraer modelo del producto (ej: "DSNU-12-70-P-A")
        // En Festo, el modelo suele estar cerca del título o en elementos específicos
        try
        {
            // Buscar modelo en patrón típico (letras-números-guiones)
            var modelLocator = page.Locator("h1 ~ div, [class*='model--'], [class*='product-name--']").First;
            if (await modelLocator.CountAsync() > 0)
            {
                var modelText = (await modelLocator.TextContentAsync())?.Trim();
                var modelMatch = System.Text.RegularExpressions.Regex.Match(modelText ?? "", @"\b[A-Z]{2,}[-\dA-Z]+\b");
                if (modelMatch.Success)
                {
                    product.Attributes["model"] = modelMatch.Value;
                    // Enriquecer el título con el modelo si no lo tiene
                    if (!string.IsNullOrEmpty(product.Title) && !product.Title.Contains(modelMatch.Value))
                    {
                        product.Title = $"{product.Title} {modelMatch.Value}".Trim();
                    }
                    else if (string.IsNullOrEmpty(product.Title))
                    {
                        product.Title = modelMatch.Value;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Error extrayendo modelo: {Message}", ex.Message);
        }
        
        // Si no hay título, usar el de la familia + SKU
        if (string.IsNullOrEmpty(product.Title) && !string.IsNullOrEmpty(familyTitle))
        {
            product.Title = $"{familyTitle} {product.SkuSource ?? ""}".Trim();
        }

        
        // 3. Extraer Precio
        var priceSelector = selectors.DetailPriceSelector ?? "div[class*='price-display-text--'], .price, .product-price";
        var priceElem = page.Locator(priceSelector).First;
        if (await priceElem.CountAsync() > 0)
        {
            var priceText = await priceElem.TextContentAsync();
            product.Price = ParsePrice(priceText ?? "");
            product.Attributes["price_text"] = priceText ?? "";
        }
        
        // 4. Extraer Descripción
        var descSelector = selectors.DetailDescriptionSelector ?? selectors.DescriptionSelector ?? 
            ".product-description, [class*='description--'], .description";
        var descElem = page.Locator(descSelector).First;
        if (await descElem.CountAsync() > 0)
        {
            product.Description = (await descElem.TextContentAsync())?.Trim();
        }
        
        // 5. Extraer Imagen Principal
        var imageSelector = selectors.DetailImageSelector ?? selectors.ImageSelector ?? 
            "img[class*='image--'], .product-image img, img[src*='product']";
        var imageElem = page.Locator(imageSelector).First;
        if (await imageElem.CountAsync() > 0)
        {
            product.ImageUrl = await imageElem.GetAttributeAsync("src");
        }
        
        // 6. Extraer atributos adicionales (specs, categoría, etc.)
        try
        {
            // Breadcrumb para categoría
            var breadcrumb = await TryExtractBreadcrumbAsync(page);
            if (breadcrumb.Count > 0)
            {
                product.Category = string.Join(" > ", breadcrumb);
                product.Attributes["category_path"] = product.Category;
            }
            
            // Marca
            var brandElem = page.Locator("[class*='brand--'], .brand, [data-testid*='brand']").First;
            if (await brandElem.CountAsync() > 0)
            {
                product.Brand = (await brandElem.TextContentAsync())?.Trim();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Error extrayendo atributos adicionales: {Message}", ex.Message);
        }
        
            // 7. Descubrimiento de URLs de navegación/relacionados (Sugerencia del Usuario)
            product.NavigationUrls = await DiscoverRelatedProductUrlsAsync(page, cancellationToken);
            if (product.NavigationUrls.Any())
            {
                _logger.LogInformation("Asociadas {Count} URLs de navegación al producto {Sku}", product.NavigationUrls.Count, product.SkuSource);
            }

            return product;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error en ExtractProductFromDetailPageDeepAsync");
            return null;
        }
    }


/// <summary>
/// Simula actividad aleatoria de usuario (hover, scroll, etc.) para parecer humano
/// </summary>
private async Task SimulateUserActivityAsync(IPage page)
{
    try
    {
        _logger.LogInformation("Simulando actividad de usuario ocioso...");
        
        // 1. Simular algunos movimientos de mouse
        for (int i = 0; i < _random.Next(2, 5); i++)
        {
            await SimulateMouseMovementAsync(page);
            await Task.Delay(_random.Next(500, 1500));
        }
        
        // 2. Simular hovers sobre elementos aleatorios (como si revisara el menú)
        var links = await page.QuerySelectorAllAsync("a, button");
        if (links.Count > 0)
        {
            for (int i = 0; i < _random.Next(1, 4); i++)
            {
                var randomLink = links[_random.Next(links.Count)];
                if (await randomLink.IsVisibleAsync())
                {
                    await randomLink.HoverAsync(new ElementHandleHoverOptions { Force = true });
                    await Task.Delay(_random.Next(1000, 2000));
                }
            }
        }
        
        // 3. Scroll pequeño
        await page.EvaluateAsync("window.scrollBy({ top: Math.floor(Math.random() * 200) - 100, behavior: 'smooth' })");
        await Task.Delay(_random.Next(1000, 2000));
    }
    catch (Exception ex)
    {
        _logger.LogWarning($"Error simulando actividad: {ex.Message}");
    }
}
// Métodos auxiliares para simular comportamiento humano
// Métodos auxiliares para simular comportamiento humano

/// <summary>
/// Pausa aleatoria para simular comportamiento humano
/// </summary>
private async Task HumanDelayAsync(int minMs = 2000, int maxMs = 5000)
{
    var delay = _random.Next(minMs, maxMs);
    _logger.LogDebug($"Pausa humana: {delay}ms");
    await Task.Delay(delay);
}


    /// <summary>
    /// Simula movimiento de mouse utilizando curvas de Bezier para mayor naturalidad
    /// </summary>
    private async Task SimulateMouseMovementAsync(IPage page)
    {
        try
        {
            var viewport = page.ViewportSize;
            var width = viewport?.Width ?? 1920;
            var height = viewport?.Height ?? 1080;
            
            // Puntos de inicio y fin aleatorios
            var x1 = _random.Next(100, width - 100);
            var y1 = _random.Next(100, height - 100);
            var x2 = _random.Next(100, width - 100);
            var y2 = _random.Next(100, height - 100);
            
            // Mover al inicio (si no estamos ahí ya)
            await page.Mouse.MoveAsync(x1, y1);
            
            // Calcular puntos de control para curva de Bezier cúbica
            var control1X = x1 + (x2 - x1) * 0.3 + _random.Next(-50, 50);
            var control1Y = y1 + (y2 - y1) * 0.3 + _random.Next(-50, 50);
            var control2X = x1 + (x2 - x1) * 0.7 + _random.Next(-50, 50);
            var control2Y = y1 + (y2 - y1) * 0.7 + _random.Next(-50, 50);
            
            // Simular movimiento a lo largo de la curva
            var steps = _random.Next(20, 50); // Número de pasos
            for (int i = 1; i <= steps; i++)
            {
                var t = (double)i / steps;
                
                // Fórmula de Bezier cúbica
                var cx = Math.Pow(1 - t, 3) * x1 + 
                         3 * Math.Pow(1 - t, 2) * t * control1X + 
                         3 * (1 - t) * Math.Pow(t, 2) * control2X + 
                         Math.Pow(t, 3) * x2;
                         
                var cy = Math.Pow(1 - t, 3) * y1 + 
                         3 * Math.Pow(1 - t, 2) * t * control1Y + 
                         3 * (1 - t) * Math.Pow(t, 2) * control2Y + 
                         Math.Pow(t, 3) * y2;
                
                await page.Mouse.MoveAsync((float)cx, (float)cy);
                // Pequeña pausa variable entre pasos para realismo
                if (i % 5 == 0) await Task.Delay(_random.Next(5, 15)); 
            }

            
            _logger.LogDebug($"Movimiento Bezier completado: ({x1},{y1}) -> ({x2},{y2})");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error en movimiento Bezier: {ex.Message}");
        }
    }

    /// <summary>
    /// Simula escritura humana con velocidad variable y errores ocasionales
    /// </summary>
    private async Task SimulateTypingAsync(ILocator locator, string text)
    {
        try 
        {
            await locator.ClickAsync();
            await HumanDelayAsync(200, 600);
            
            foreach (char c in text)
            {
                // Velocidad de escritura variable (WPM simulado)
                await Task.Delay(_random.Next(30, 150));
                
                // Simular error de tipeo ocasional (2% de probabilidad)
                if (_random.Next(0, 50) == 0)
                {
                    char wrongChar = (char)(c + _random.Next(-1, 2)); // Carácter cercano
                    await locator.PressSequentiallyAsync(wrongChar.ToString());
                    await Task.Delay(_random.Next(100, 300)); // Darse cuenta del error
                    await locator.PressAsync("Backspace");
                    await Task.Delay(_random.Next(100, 200)); // Pausa antes de corregir
                }
                
                await locator.PressSequentiallyAsync(c.ToString());
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error simulando escritura: {ex.Message}");
            // Fallback a llenado rápido si falla la simulación
            await locator.FillAsync(text);
        }
    }

    /// <summary>
    /// Simula escritura humana con velocidad variable y errores ocasionales (Overload IElementHandle)
    /// </summary>
    private async Task SimulateTypingAsync(IElementHandle handle, string text)
    {
        try 
        {
            await handle.ClickAsync();
            await HumanDelayAsync(200, 600);
            
            foreach (char c in text)
            {
                // Velocidad de escritura variable (WPM simulado)
                await Task.Delay(_random.Next(30, 150));
                
                // Simular error de tipeo ocasional (2% de probabilidad)
                if (_random.Next(0, 50) == 0)
                {
                    char wrongChar = (char)(c + _random.Next(-1, 2)); // Carácter cercano
                    #pragma warning disable CS0612
                    await handle.TypeAsync(wrongChar.ToString());
                    #pragma warning restore CS0612
                    await Task.Delay(_random.Next(100, 300)); // Darse cuenta del error
                    await handle.PressAsync("Backspace");
                    await Task.Delay(_random.Next(100, 200)); // Pausa antes de corregir
                }
                
                #pragma warning disable CS0612
                await handle.TypeAsync(c.ToString());
                #pragma warning restore CS0612
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error simulando escritura: {ex.Message}");
            await handle.FillAsync(text);
        }
    }

    /// <summary>
    /// Pasa el mouse sobre el elemento antes de hacer clic para simular duda/enfoque
    /// </summary>
    private async Task SimulateHoverBeforeClickAsync(ILocator locator)
    {
        try
        {
            await locator.HoverAsync();
            await Task.Delay(_random.Next(300, 800)); // Pausa visual
        }
        catch 
        {
            // Ignorar errores de hover, no es crítico
        }
    }
    
    /// <summary>
    /// Pasa el mouse sobre el elemento antes de hacer clic (Overload IElementHandle)
    /// </summary>
    private async Task SimulateHoverBeforeClickAsync(IElementHandle handle)
    {
        try
        {
            await handle.HoverAsync();
            await Task.Delay(_random.Next(300, 800)); // Pausa visual
        }
        catch 
        {
            // Ignorar errores de hover
        }
    }

    private static async Task HighlightLocatorAsync(ILocator locator)
    {
        try
        {
            var handle = await locator.ElementHandleAsync();
            if (handle != null)
            {
                await HighlightLocatorAsync(handle);
            }
        }
        catch
        {
            // Ignore highlight failures
        }
    }
    
    private static async Task HighlightLocatorAsync(IElementHandle handle)
    {
        try
        {
            await handle.EvaluateAsync(
                "el => { el.style.outline = '2px solid #ff9800'; el.style.outlineOffset = '2px'; }");
        }
        catch
        {
            // Ignore highlight failures
        }
    }


/// <summary>
/// Realiza scroll gradual para simular lectura humana
/// </summary>
private async Task HumanScrollAsync(IPage page, int scrolls = 3)
{
    try
    {
        _logger.LogInformation($"Realizando scroll gradual ({scrolls} pasos)...");
        
            for (int i = 0; i < scrolls; i++)
            {
                var scrollAmount = _random.Next(250, 750);
                await page.EvaluateAsync($@"(amount) => {{
                    window.scrollBy({{
                        top: amount,
                        behavior: 'smooth'
                    }});
                }}", scrollAmount);
                
                await HumanDelayAsync(1200, 3000);
                
                if (_random.Next(0, 5) == 0)
                {
                    await page.EvaluateAsync("window.scrollBy({ top: -150, behavior: 'smooth' })");
                    await HumanDelayAsync(800, 1500);
                }
                
                if (_random.Next(0, 2) == 0)
                {
                    await SimulateMouseMovementAsync(page);
                }
            }
            
            await HumanDelayAsync(1500, 3000);
    }
    catch (Exception ex)
    {
        _logger.LogWarning($"Error en scroll gradual: {ex.Message}");
    }
}

/// <summary>
/// Simula lectura de página antes de hacer clic
/// </summary>
private async Task SimulateReadingAsync(IPage page)
{
    try
    {
        // Simular que el usuario está leyendo la página
        await HumanDelayAsync(2000, 4000);
        
        // Mover el mouse ocasionalmente
        if (_random.Next(0, 2) == 0)
        {
            await SimulateMouseMovementAsync(page);
        }
        
        // Pequeño scroll aleatorio
        if (_random.Next(0, 3) == 0)
        {
            var scrollAmount = _random.Next(-200, 200);
            await HumanDelayAsync(500, 1500);
        }
    }
    catch (Exception ex)
    {
        _logger.LogWarning($"Error simulando lectura: {ex.Message}");
    }
}


    /// <summary>
    /// Scrolls to the bottom of the page to trigger lazy loading of widgets (recommendations, footers).
    /// </summary>
    private async Task FastScrollToBottomAsync(IPage page)
    {
        try 
        {
            await page.EvaluateAsync(@"async () => {
                await new Promise((resolve) => {
                    var totalHeight = 0;
                    var distance = 500;
                    var timer = setInterval(() => {
                        var scrollHeight = document.body.scrollHeight;
                        window.scrollBy(0, distance);
                        totalHeight += distance;

                        if(totalHeight >= scrollHeight - window.innerHeight){
                            clearInterval(timer);
                            resolve();
                        }
                    }, 100);
                });
            }");
            await Task.Delay(2000); // Wait for animations/AJAX
        }
        catch { /* Ignore scroll errors */ }
    }

    /// <summary>
    /// Detecta si la página ha caído en un modo de mantenimiento o bloqueo y espera/reintenta
    /// </summary>
    private async Task<bool> DetectAndHandleMaintenanceAsync(IPage page, int retryCount = 0)
    {
        try
        {
            var url = page.Url;
            var content = await page.ContentAsync();
            
            bool isMaintenance = url.Contains("maintenance", StringComparison.OrdinalIgnoreCase) || 
                                url.Contains("wartung", StringComparison.OrdinalIgnoreCase) ||
                                content.Contains("Maintenance work", StringComparison.OrdinalIgnoreCase) ||
                                content.Contains("Wartungsarbeiten", StringComparison.OrdinalIgnoreCase);
                                
            if (isMaintenance)
            {
                _logger.LogWarning("⚠️ Detectada página de mantenimiento/espera en: {Url}", url);
                if (retryCount < 3)
                {
                    _logger.LogInformation("Esperando 10 segundos antes de reintentar... (Intento {Retry})", retryCount + 1);
                    await Task.Delay(10000);
                    await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });
                    return await DetectAndHandleMaintenanceAsync(page, retryCount + 1);
                }
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Error in DetectAndHandleMaintenanceAsync: {Msg}", ex.Message);
        }
        return false;
    }

    /// <summary>
    /// Navega a una URL con comportamiento humano
    /// </summary>
    private async Task HumanNavigateAsync(IPage page, string url, WaitUntilState waitUntil = WaitUntilState.NetworkIdle)
    {
        _logger.LogInformation($"Navegando (modo humano) a: {url}");
        
        // Pausa antes de navegar
        await HumanDelayAsync(1500, 3000);
        
        // Navegar
        await page.GotoAsync(url, new PageGotoOptions 
        { 
            WaitUntil = waitUntil,
            Timeout = 90000
        });
        
        // Pausa después de cargar
        await HumanDelayAsync(2500, 5000);
        
        // Detectar si caímos en mantenimiento
        await DetectAndHandleMaintenanceAsync(page);
        
        // Simular lectura inicial
        await SimulateReadingAsync(page);
    }

/// <summary>
/// Hace clic en un elemento con comportamiento humano
/// </summary>
private async Task HumanClickAsync(ILocator locator)
{
    try
    {
        // Pausa antes del clic
        await HumanDelayAsync(1000, 2000);
        
        // Scroll al elemento
        await locator.ScrollIntoViewIfNeededAsync();
        await HumanDelayAsync(500, 1000);
        
        // Mover mouse al elemento con trayectoria curva (opcionalmente)
        var box = await locator.BoundingBoxAsync();
        if (box != null)
        {
            var targetX = box.X + box.Width / 2;
            var targetY = box.Y + box.Height / 2;
            
            // Simular un movimiento un poco más complejo
            await locator.Page.Mouse.MoveAsync(targetX + _random.Next(-5, 5), targetY + _random.Next(-5, 5), new MouseMoveOptions { Steps = 10 });
            await HumanDelayAsync(200, 500);
        }
        
        // Hacer clic
        await locator.ClickAsync();
        
        // Pausa después del clic
        await HumanDelayAsync(1500, 3000);
    }
    catch (Exception ex)
    {
        _logger.LogWarning($"Error en clic humano: {ex.Message}");
        throw;
    }
}

/*
/// <summary>
    var products = new List<ScrapedProduct>();
*/
    
/*
    try
    {
        var urls = System.Text.Json.JsonSerializer.Deserialize<List<string>>(directUrlsJson);
        if (urls == null || urls.Count == 0)
        {
            _logger.LogWarning("No se encontraron URLs para inspeccionar");
            return products;
        }
        
        _logger.LogInformation("Inspeccionando {Count} URLs directamente", urls.Count);
        await LogStepAsync(site.Id, "info", $"🚀 Iniciando inspección directa de {urls.Count} URLs", new { count = urls.Count });
        await LogStepAsync(site.Id, "info", "🔍 El scraper intentará extraer datos de cada URL usando los selectores configurados.");
        
        // Parsear selectores del sitio
        string selectorsJson;
        if (site.Selectors is JsonElement jsonElement)
            selectorsJson = jsonElement.GetRawText();
        else if (site.Selectors is string s)
            selectorsJson = s;
        else
            selectorsJson = JsonConvert.SerializeObject(site.Selectors);
        
        var selectors = JsonConvert.DeserializeObject<SiteSelectors>(selectorsJson) ?? new SiteSelectors();
        FillSelectorsFromJson(selectors, selectorsJson);
        
        // Obtener contexto del navegador
        var storageStatePath = GetStorageStatePath(site.Name);
        var contextOptions = new BrowserNewContextOptions();
        if (File.Exists(storageStatePath))
        {
            contextOptions.StorageStatePath = storageStatePath;
        }
        
        var context = await GetContextAsync(contextOptions);
        var page = await context.NewPageAsync();
        
        // Procesar cada URL
        foreach (var url in urls)
        {
            if (cancellationToken.IsCancellationRequested) break;
            
            try
            {
                _logger.LogInformation("Inspeccionando URL: {Url}", url);
                await LogStepAsync(site.Id, "info", $"Inspeccionando: {url}", new { url });
                
                // Navegar a la URL
                await HumanNavigateAsync(page, url, WaitUntilState.NetworkIdle);
                
                // Verificar y refrescar sesión si es necesario
                await EnsureSessionActiveAsync(page, site.Id, cancellationToken);
                
                // Tomar screenshot
                var screenshot = await SaveStepScreenshotAsync(page, $"inspect_{Path.GetFileName(new Uri(url).AbsolutePath)}");
                
                // Extraer producto de la página de detalle
                var product = await ExtractProductFromDetailPageDeepAsync(
                    page, 
                    selectors, 
                    site.Id, 
                    null);

                
                if (product != null)
                {
                    product.SourceUrl = url;
                    product.ScreenshotBase64 = screenshot;
                    products.Add(product);
                    await LogStepAsync(site.Id, "success", $"Producto extraído: {product.Title}", 
                        new { sku = product.SkuSource, url });
                }
                else
                {
                    await LogStepAsync(site.Id, "warning", $"No se pudo extraer producto", new { url });
                }
                
                // Pausa humanizada entre URLs
                await HumanDelayAsync(2000, 4000);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error inspeccionando URL: {Url}", url);
                await LogStepAsync(site.Id, "error", $"Error: {ex.Message}", new { url });
            }
        }
        
        await LogStepAsync(site.Id, "success", $"Inspección completada: {products.Count} productos extraídos", 
            new { total = urls.Count, extracted = products.Count });
        
        // Guardar estado de sesión
        if (File.Exists(storageStatePath) || products.Count > 0)
        {
            try
            {
                await context.StorageStateAsync(new BrowserContextStorageStateOptions { Path = storageStatePath });
            }
            catch { }
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error en modo de inspección directa");
        await LogStepAsync(site.Id, "error", $"Error crítico: {ex.Message}", null);
    }
    
    return products;
}
*/

    private async Task<List<string>> DiscoverRelatedProductUrlsAsync(IPage page, CancellationToken cancellationToken)
    {
        var productsLinks = new List<string>();
        try
        {
            // 1. Specific Selectors (High Confidence)
            var relatedSelectors = new[]
            {
                "h4.product-name--syDfy a.product-link--rpGXq",
                "a[class*='product-link--']",
                "a[class*='related-product']",
                ".accessories-table a[href*='/a/']", 
                ".accessories-table a[href*='/p/']",
                // Festo spare parts / accessories often in tables or lists
                "[id*='spare-parts'] a[href*='/p/']",
                "[id*='accessories'] a[href*='/p/']",
                // Dynamic Recommendation Widgets
                "div[class*='reco-widget'] a",
                "div[class*='recommendation'] a",
                "div[class*='similar-products'] a",
                "div[class*='card--'] a" // Generic card catcher for carousels
            };

            foreach (var selector in relatedSelectors)
            {
                try {
                    var elements = await page.Locator(selector).AllAsync();
                    foreach (var el in elements)
                    {
                        var href = await el.GetAttributeAsync("href");
                        if (!string.IsNullOrEmpty(href))
                        {
                            var fullUrl = NormalizeHref(page.Url, href);
                            if (!productsLinks.Contains(fullUrl)) productsLinks.Add(fullUrl);
                        }
                    }
                } catch { /* Ignore individual selector failures */ }
            }

            // 2. Broad pattern matching (Medium Confidence) - Filter strictly for products
            // Avoid adding general category links here, usually products have /p/ or /a/ in strict positions or patterns
            var allLinks = await page.EvaluateAsync<string[]>("() => [...document.querySelectorAll('a')].map(a => a.href)");
            
            foreach (var link in allLinks)
            {
                if (string.IsNullOrEmpty(link) || link.Contains("#") || link.Contains("javascript:")) continue;
                
                // Exclude common non-product paths
                if (link.Contains("/login") || link.Contains("/cart") || link.Contains("/checkout") || link.Contains("/contact")) continue;

                bool matches = false;
                if (link.Contains("festo.com"))
                {
                    // Strict Festo product patterns
                    // /p/code -> product detail
                    // /a/code -> article detail (spare part / specific SKU)
                    // /c/code -> category/family (CRITICAL for crawling)
                    // /cat/ -> generic category
                    matches = link.Contains("/p/") || link.Contains("/a/") || link.Contains("/c/") || link.Contains("/cat/");
                }
                else
                {
                    // Generic fallback
                    matches = link.Contains("/product/") || link.Contains("/producto/") || link.Contains("/item/");
                }
                
                if (matches && !productsLinks.Contains(link))
                {
                    productsLinks.Add(link);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error discovering URLs in HTML");
        }
        
        return productsLinks.Distinct().ToList();
    }

    private async Task SimulateHumanBehaviorAsync(IPage page, CancellationToken token)
    {
        var delay = _random.Next(1000, 20000); // 1 sec to 20 sec delay
        _logger.LogInformation("⏳ Emulando comportamiento humano: Espera de {Delay}s...", delay / 1000.0);
        
        // Random mouse movement
        try 
        {
            if (page.ViewportSize != null)
            {
                var width = page.ViewportSize.Width;
                var height = page.ViewportSize.Height;
                await page.Mouse.MoveAsync(_random.Next(0, width), _random.Next(0, height), new MouseMoveOptions { Steps = 5 });
                await Task.Delay(_random.Next(200, 800), token);
                await page.Mouse.MoveAsync(_random.Next(0, width), _random.Next(0, height), new MouseMoveOptions { Steps = 10 });
            }
        } 
        catch { /* Ignore mouse errors */ }

        await Task.Delay(delay, token);
    }

    private bool IsDatasheetUrl(string url)
    {
        return !string.IsNullOrWhiteSpace(url) && _datasheetUrlRegex.IsMatch(url);
    }

    private ScrapedProduct ExtractDatasheetProduct(string url)
    {
        var match = _datasheetUrlRegex.Match(url);
        var sku = match.Success ? match.Groups[1].Value : "unknown";
        
        var product = new ScrapedProduct
        {
            SkuSource = sku,
            Title = $"Datasheet {sku}",
            Description = "Documento técnico / Hoja de datos",
            SourceUrl = url,
            ScrapedAt = DateTime.UtcNow,
            Attributes = new Dictionary<string, string>
            {
                ["type"] = "datasheet",
                ["is_attachment"] = "true"
            }
        };

        product.Attachments.Add(new ProductAttachment
        {
            FileName = $"datasheet_{sku}.pdf",
            FileUrl = url,
            FileType = "application/pdf"
        });

        return product;
    }
}

