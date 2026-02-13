using ScrapSAE.Core.Interfaces;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Infrastructure.AI;
using ScrapSAE.Infrastructure.Data;
using ScrapSAE.Infrastructure.Scraping;
using ScrapSAE.Infrastructure.Services;
using ScrapSAE.Worker;

using Serilog;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/scrapsae_worker-.log", rollingInterval: RollingInterval.Day, outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Logging.AddSerilog();

builder.Services.Configure<FlashlyApiConfig>(builder.Configuration.GetSection("FlashlyApi"));
builder.Services.Configure<SyncOptionsConfig>(builder.Configuration.GetSection("SyncOptions"));
builder.Services.Configure<CsvExportConfig>(builder.Configuration.GetSection("CsvExport"));

// Register Infrastructure Services
builder.Services.AddSingleton<IStagingService, SupabaseStagingService>();
builder.Services.AddSingleton<ISyncLogService, SupabaseSyncLogService>();
builder.Services.AddSingleton<IExecutionReportService, SupabaseExecutionReportService>();
builder.Services.AddSingleton<IScrapingService, PlaywrightScrapingService>();
builder.Services.AddSingleton<ScrapingProcessManager>();
builder.Services.AddSingleton<IScrapeControlService, NoOpScrapeControlService>();
builder.Services.AddHttpClient("OpenAI");
builder.Services.AddSingleton<IAIProcessorService, OpenAIProcessorService>();
builder.Services.AddHttpClient<IFlashlySyncService, FlashlySyncService>()
    .ConfigureHttpClient((sp, client) =>
    {
        var config = sp.GetRequiredService<IOptions<FlashlyApiConfig>>().Value;
        if (!string.IsNullOrWhiteSpace(config.BaseUrl))
        {
            client.BaseAddress = new Uri(config.BaseUrl);
        }

        if (!string.IsNullOrWhiteSpace(config.ApiKey))
        {
            client.DefaultRequestHeaders.Remove("X-API-Key");
            client.DefaultRequestHeaders.Add("X-API-Key", config.ApiKey);
        }
    });
builder.Services.AddSingleton<ICsvExportService, CsvExportService>();

// Register Worker and Initializer
builder.Services.AddSingleton<DbInitializer>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

// Run Database Initialization
using (var scope = host.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<DbInitializer>();
    await initializer.InitializeAsync();
}

host.Run();
