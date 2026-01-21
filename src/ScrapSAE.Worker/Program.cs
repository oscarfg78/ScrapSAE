using ScrapSAE.Core.Interfaces;
using ScrapSAE.Infrastructure.Data;
using ScrapSAE.Infrastructure.Scraping;
using ScrapSAE.Worker;

var builder = Host.CreateApplicationBuilder(args);

// Register Infrastructure Services
builder.Services.AddSingleton<IStagingService, SupabaseStagingService>();
builder.Services.AddSingleton<ISyncLogService, SupabaseSyncLogService>();
builder.Services.AddSingleton<IExecutionReportService, SupabaseExecutionReportService>();
builder.Services.AddSingleton<IScrapingService, PlaywrightScrapingService>();

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
