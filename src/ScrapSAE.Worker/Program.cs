using ScrapSAE.Core.Interfaces;
using ScrapSAE.Infrastructure.AI;
using ScrapSAE.Infrastructure.Data;
using ScrapSAE.Infrastructure.Scraping;
using ScrapSAE.Infrastructure.Services;
using ScrapSAE.Worker;

using Serilog;

var builder = Host.CreateApplicationBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/scrapsae_worker-.log", rollingInterval: RollingInterval.Day, outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Logging.AddSerilog();

// Register Infrastructure Services
builder.Services.AddSingleton<IStagingService, SupabaseStagingService>();
builder.Services.AddSingleton<ISyncLogService, SupabaseSyncLogService>();
builder.Services.AddSingleton<IExecutionReportService, SupabaseExecutionReportService>();
builder.Services.AddSingleton<IScrapingService, PlaywrightScrapingService>();
builder.Services.AddSingleton<ScrapingProcessManager>();
builder.Services.AddSingleton<IScrapeControlService, NoOpScrapeControlService>();
builder.Services.AddHttpClient("OpenAI");
builder.Services.AddSingleton<IAIProcessorService, OpenAIProcessorService>();

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
