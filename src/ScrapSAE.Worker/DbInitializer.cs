using System.Net.Http.Json;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using ScrapSAE.Core.Entities;

namespace ScrapSAE.Worker;

public class DbInitializer
{
    private readonly ILogger<DbInitializer> _logger;
    private readonly string _baseUrl;
    private readonly HttpClient _httpClient;

    public DbInitializer(IConfiguration configuration, ILogger<DbInitializer> logger)
    {
        _logger = logger;
        
        var url = configuration["Supabase:Url"] ?? throw new ArgumentNullException("Supabase:Url not configured");
        var key = configuration["Supabase:ServiceKey"] ?? throw new ArgumentNullException("Supabase:ServiceKey not configured");

        _baseUrl = url.TrimEnd('/');
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("apikey", key);
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {key}");
        _httpClient.DefaultRequestHeaders.Add("Prefer", "return=representation");
    }

    public async Task InitializeAsync()
    {
        try
        {
            _logger.LogInformation("Initializing database configuration...");
            await EnsureFestoSiteAsync();
            _logger.LogInformation("Database initialization completed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing database");
            // Non-critical, allows the worker to continue even if init fails (e.g. transient network issue)
        }
    }
    private async Task EnsureFestoSiteAsync()
    {
        _logger.LogInformation("Ensuring Festo site configuration (Create or Update)...");
        
        // Check if Festo already exists to get ID
        var checkResponse = await _httpClient.GetAsync($"{_baseUrl}/rest/v1/config_sites?name=eq.Festo&select=id");
        Guid? existingId = null;
        if (checkResponse.IsSuccessStatusCode)
        {
            var existing = await checkResponse.Content.ReadFromJsonAsync<SiteProfile[]>();
            existingId = existing?.FirstOrDefault()?.Id;
        }

        // Create Festo configuration
        var festoConfig = new
        {
            name = "Festo",
            base_url = "https://www.festo.com/mx/es",
            is_active = true,
            requires_login = true,
            credentials_encrypted = "fred.flores@osmafremx.com|Otrosmafremx2302",
            selectors = new
            {
                ProductListSelector = ".result-list-item",
                TitleSelector = ".product-name",
                PriceSelector = ".price-value",
                SkuSelector = ".part-number",
                ImageSelector = ".product-image img",
                NextPageSelector = ".pagination-next",
                MaxPages = 2
            },
            cron_expression = "ALWAYS"
        };

        HttpResponseMessage response;
        if (existingId.HasValue)
        {
            response = await _httpClient.PatchAsJsonAsync($"{_baseUrl}/rest/v1/config_sites?id=eq.{existingId}", festoConfig);
        }
        else
        {
            response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/rest/v1/config_sites", festoConfig);
        }
        
        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Festo site configuration optimized successfully.");
        }
        else
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Failed to ensure Festo site configuration: {Error}", error);
        }
    }
}
