using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace ScrapSAE.Infrastructure.Scraping
{
    public interface IBrowserSharingService : IAsyncDisposable
    {
        Task<IBrowser> GetBrowserAsync();
        Task<IPlaywright> GetPlaywrightAsync();
        Task CloseBrowserAsync();
    }

    public class BrowserSharingService : IBrowserSharingService
    {
        private readonly ILogger<BrowserSharingService> _logger;
        private IPlaywright? _playwright;
        private IBrowser? _browser;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public BrowserSharingService(ILogger<BrowserSharingService> logger)
        {
            _logger = logger;
        }

        public async Task<IPlaywright> GetPlaywrightAsync()
        {
             await _lock.WaitAsync();
             try
             {
                 if (_playwright == null)
                 {
                     _playwright = await Playwright.CreateAsync();
                 }
                 return _playwright;
             }
             finally
             {
                 _lock.Release();
             }
        }

        public async Task<IBrowser> GetBrowserAsync()
        {
            if (_browser != null && _browser.IsConnected)
            {
                return _browser;
            }

            await _lock.WaitAsync();
            try
            {
                if (_browser != null && _browser.IsConnected)
                {
                    return _browser;
                }

                _logger.LogInformation("Initializing shared Playwright browser instance.");
                
                if (_playwright == null)
                {
                    _playwright = await Playwright.CreateAsync();
                }

                var headlessEnv = Environment.GetEnvironmentVariable("SCRAPSAE_HEADLESS");
                var headless = string.IsNullOrEmpty(headlessEnv) || (bool.TryParse(headlessEnv, out var h) && h);
                var manualLogin = Environment.GetEnvironmentVariable("SCRAPSAE_MANUAL_LOGIN") == "true" ||
                                  Environment.GetEnvironmentVariable("SCRAPSAE_FORCE_MANUAL_LOGIN") == "true";

                if (manualLogin)
                {
                    _logger.LogInformation("Manual login mode enabled: forcing Headless=false");
                    headless = false;
                }

                _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = headless,
                    Args = new[] { "--start-maximized" } // Optional
                });

                _logger.LogInformation("Browser launched successfully. Headless: {Headless}", headless);
                return _browser;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task CloseBrowserAsync()
        {
            await _lock.WaitAsync();
            try
            {
                if (_browser != null)
                {
                    _logger.LogInformation("Closing shared browser instance.");
                    await _browser.CloseAsync();
                    await _browser.DisposeAsync();
                    _browser = null;
                }

                if (_playwright != null)
                {
                    _playwright.Dispose();
                    _playwright = null;
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await CloseBrowserAsync();
        }
    }
}
