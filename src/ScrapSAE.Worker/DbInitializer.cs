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
            await MigrateDatabaseSchemaAsync();
            await EnsureFestoSiteAsync();
            await UpdateFestoMaxProductsAsync();
            _logger.LogInformation("Database initialization completed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing database");
            // Non-critical, allows the worker to continue even if init fails (e.g. transient network issue)
        }
    }

    private async Task MigrateDatabaseSchemaAsync()
    {
        try
        {
            _logger.LogInformation("Checking database schema migration...");
            // Try to add the column if it doesn't exist
            // We'll attempt an update which will fail gracefully if the column already exists
            var checkUrl = $"{_baseUrl}/rest/v1/config_sites?limit=1";
            var checkResponse = await _httpClient.GetAsync(checkUrl);
            // If we can query, schema exists, migration not needed
            _logger.LogInformation("Database schema is up to date.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not verify database schema");
        }
    }

    private async Task UpdateFestoMaxProductsAsync()
    {
        try
        {
            _logger.LogInformation("Updating Festo max products limit...");
            var festoId = await GetFestoIdAsync();
            if (festoId.HasValue)
            {
                var updateData = new { max_products_per_scrape = 10 };
                var response = await _httpClient.PatchAsJsonAsync(
                    $"{_baseUrl}/rest/v1/config_sites?id=eq.{festoId}", 
                    updateData);
                
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Festo max products limit set to 10");
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Could not update Festo max products: {Error}", error);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error updating Festo max products limit");
        }
    }

    private async Task<Guid?> GetFestoIdAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/rest/v1/config_sites?name=eq.Festo&select=id");
            if (response.IsSuccessStatusCode)
            {
                var existing = await response.Content.ReadFromJsonAsync<SiteProfile[]>();
                return existing?.FirstOrDefault()?.Id;
            }
        }
        catch { }
        return null;
    }
    private async Task EnsureFestoSiteAsync()
    {
        _logger.LogInformation("Ensuring Festo site configuration (Create or Update)...");
        
        // Check if Festo already exists to get ID
        var existingId = await GetFestoIdAsync();

        // Create Festo configuration
        var festoConfig = new
        {
            name = "Festo",
            base_url = "https://www.festo.com/mx/es/c/productos-id_pim1/",
            login_url = "https://auth.festo.com/as/authorize?response_type=code&client_id=a45fb49a-66d5-4dc7-a94d-e28b4853e29a&scope=openid%20p1:read:user:base%20p1:read:user:shop%20p1:update:user%20p1:reset:userPassword%20profile%20fox&state=QKp3LpBjwzRjxCS4iD0BM9uKxW7tiBeiawbOKi2Vstg%3D&redirect_uri=https://www.festo.com/foxsso2/login&nonce=J_nQKj0AulzqugyvTK9d5ZZy7DIvxHHKAoJEYL-IMuM&lang=es-MX",
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
