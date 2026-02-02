using ScrapSAE.Api.Models;
using ScrapSAE.Api.Services;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Interfaces;
using ScrapSAE.Infrastructure.AI;
using ScrapSAE.Infrastructure.Scraping;
using ScrapSAE.Infrastructure.Services;
using ScrapSAE.Infrastructure.Scraping.Strategies;


using Serilog; // Added for file logging

// Configure Serilog early
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/scrapsae_api-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog(); // Enable Serilog

builder.Configuration.AddJsonFile("appsettings.runtime.json", optional: true, reloadOnChange: true);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<SettingsStore>();
builder.Services.AddSingleton<DiagnosticsService>();
builder.Services.AddSingleton<ISupabaseRestClient, SupabaseRestClient>();
builder.Services.AddSingleton<IScrapeControlService, ScrapeControlService>();
builder.Services.AddSingleton<ISyncLogService, ApiSyncLogService>();
builder.Services.AddHttpClient("OpenAI");
builder.Services.AddSingleton<IAIProcessorService, OpenAIProcessorService>();

// Nuevos servicios para arquitectura adaptativa
builder.Services.AddSingleton<IPerformanceMetricsCollector, PerformanceMetricsCollector>();
builder.Services.AddSingleton<IPostExecutionAnalyzer, PostExecutionAnalyzerService>();
builder.Services.AddSingleton<IConfigurationUpdater, ScrapSAE.Api.Services.ConfigurationUpdaterService>();
builder.Services.AddSingleton<ILearningService, LearningService>();

// ===== SISTEMA ADAPTATIVO - FASE 1-4 =====
// Telemetría Enriquecida
builder.Services.AddSingleton<ITelemetryService, TelemetryService>();

// Actualización Automática de Configuración
builder.Services.AddSingleton<IStagingService, ApiStagingService>();
builder.Services.AddSingleton<IConfigurationUpdaterService, ScrapSAE.Infrastructure.Services.ConfigurationUpdaterService>();

// Estrategias de Scraping Multi-Modo
builder.Services.AddSingleton<IScrapingStrategy, DirectExtractionStrategy>();
builder.Services.AddSingleton<IScrapingStrategy, ListExtractionStrategy>();
builder.Services.AddSingleton<IScrapingStrategy, FamiliesExtractionStrategy>();

// Orquestador de Estrategias con Fallback
builder.Services.AddSingleton<IStrategyOrchestrator>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<StrategyOrchestrator>>();
    var strategies = sp.GetServices<IScrapingStrategy>();
    return new StrategyOrchestrator(logger, strategies);
});

builder.Services.AddSingleton(sp => new SupabaseTableService<SiteProfile>(sp.GetRequiredService<ISupabaseRestClient>(), "config_sites"));
builder.Services.AddSingleton(sp => new SupabaseTableService<StagingProduct>(sp.GetRequiredService<ISupabaseRestClient>(), "staging_products"));
builder.Services.AddSingleton(sp => new SupabaseTableService<CategoryMapping>(sp.GetRequiredService<ISupabaseRestClient>(), "category_mapping"));
builder.Services.AddSingleton(sp => new SupabaseTableService<SyncLog>(sp.GetRequiredService<ISupabaseRestClient>(), "sync_logs"));
builder.Services.AddSingleton(sp => new SupabaseTableService<ExecutionReport>(sp.GetRequiredService<ISupabaseRestClient>(), "execution_reports"));

// Browser sharing for persistence
builder.Services.AddSingleton<IBrowserSharingService, BrowserSharingService>();

builder.Services.AddSingleton<ScrapingProcessManager>();
builder.Services.AddSingleton<IScrapingService, PlaywrightScrapingService>();
builder.Services.AddSingleton<ScrapingRunner>();
builder.Services.AddSingleton<IScrapingSignalService, ScrapingSignalService>();

var saeProvider = builder.Configuration["SAE:Provider"] ?? "firebird";
if (string.Equals(saeProvider, "firebird", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<ISaeSdkService, FirebirdSaeSdkService>();
}
else
{
    builder.Services.AddSingleton<ISaeSdkService, AspelSaeSdkService>();
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/settings", (SettingsStore store) =>
{
    return Results.Ok(store.Get() ?? new ScrapSAE.Api.Models.AppSettingsDto());
});
app.MapPost("/api/settings", async (ScrapSAE.Api.Models.AppSettingsDto settings, SettingsStore store, CancellationToken token) =>
{
    await store.SaveAsync(settings, token);
    return Results.Ok(settings);
});
app.MapGet("/api/diagnostics", async (DiagnosticsService diagnostics, CancellationToken token) =>
{
    var result = await diagnostics.RunAsync(token);
    return Results.Ok(result);
});

var screenshotDir = Path.Combine(Path.GetTempPath(), "scrapsae-screens");
app.MapGet("/api/sync-logs/screenshot/{fileName}", (string fileName) =>
{
    if (string.IsNullOrWhiteSpace(fileName) ||
        fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
        fileName.Contains("..") ||
        fileName.Contains(Path.DirectorySeparatorChar) ||
        fileName.Contains(Path.AltDirectorySeparatorChar))
    {
        return Results.BadRequest();
    }

    var path = Path.Combine(screenshotDir, fileName);
    if (!System.IO.File.Exists(path))
    {
        return Results.NotFound();
    }

    return Results.File(path, "image/png");
});

MapCrud(app, "/api/sites", "Site", 
    app.Services.GetRequiredService<SupabaseTableService<SiteProfile>>(),
    entity =>
    {
        if (entity.Id == Guid.Empty)
        {
            entity.Id = Guid.NewGuid();
        }
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
    },
    entity => entity.UpdatedAt = DateTime.UtcNow);

MapCrud(app, "/api/staging-products", "StagingProduct",
    app.Services.GetRequiredService<SupabaseTableService<StagingProduct>>(),
    entity =>
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
    },
    entity => entity.UpdatedAt = DateTime.UtcNow);

app.MapPost("/api/staging-products/upsert", async (StagingProduct product, IStagingService stagingService) =>
{
    var result = await stagingService.UpsertProductAsync(product);
    return Results.Ok(result);
});

MapCrud(app, "/api/category-mappings", "CategoryMapping",
    app.Services.GetRequiredService<SupabaseTableService<CategoryMapping>>(),
    entity => entity.CreatedAt = DateTime.UtcNow,
    _ => { });

MapCrud(app, "/api/sync-logs", "SyncLog",
    app.Services.GetRequiredService<SupabaseTableService<SyncLog>>(),
    entity => entity.CreatedAt = DateTime.UtcNow,
    _ => { });

MapCrud(app, "/api/execution-reports", "ExecutionReport",
    app.Services.GetRequiredService<SupabaseTableService<ExecutionReport>>(),
    entity => entity.CreatedAt = DateTime.UtcNow,
    _ => { });

app.MapPost("/api/scraping/run/{siteId:guid}", async (
    Guid siteId,
    HttpRequest request,
    ScrapingRunner runner,
    CancellationToken token) =>
{
    var manualLogin = bool.TryParse(request.Query["manualLogin"], out var manual) && manual;
    var headless = !bool.TryParse(request.Query["headless"], out var headlessParsed) || headlessParsed;
    var keepBrowser = bool.TryParse(request.Query["keepBrowser"], out var keepBrowserParsed) && keepBrowserParsed;
    var screenshotFallback = bool.TryParse(request.Query["screenshotFallback"], out var screenshotParsed) && screenshotParsed;
    var scrapingMode = request.Query["mode"].ToString() ?? "traditional";
    if (scrapingMode.Contains("Familias")) scrapingMode = "families"; else if (scrapingMode.Contains("Tradicional")) scrapingMode = "traditional";

    var previousManual = Environment.GetEnvironmentVariable("SCRAPSAE_MANUAL_LOGIN");
    var previousForceManual = Environment.GetEnvironmentVariable("SCRAPSAE_FORCE_MANUAL_LOGIN");
    var previousHeadless = Environment.GetEnvironmentVariable("SCRAPSAE_HEADLESS");
    var previousKeepBrowser = Environment.GetEnvironmentVariable("SCRAPSAE_KEEP_BROWSER");
    var previousScreenshotFallback = Environment.GetEnvironmentVariable("SCRAPSAE_SCREENSHOT_FALLBACK");
    var previousMode = Environment.GetEnvironmentVariable("SCRAPSAE_MODE");

    try
    {
        Console.WriteLine($"[DEBUG] Scraping request for site {siteId}: manualLogin={manualLogin}, headless={headless}, keepBrowser={keepBrowser}, screenshotFallback={screenshotFallback}");
        Environment.SetEnvironmentVariable("SCRAPSAE_MANUAL_LOGIN", manualLogin ? "true" : "false");
        Environment.SetEnvironmentVariable("SCRAPSAE_FORCE_MANUAL_LOGIN", manualLogin ? "true" : "false");
        Environment.SetEnvironmentVariable("SCRAPSAE_HEADLESS", headless ? "true" : "false");
        Environment.SetEnvironmentVariable("SCRAPSAE_KEEP_BROWSER", keepBrowser ? "true" : "false");
        Environment.SetEnvironmentVariable("SCRAPSAE_SCREENSHOT_FALLBACK", screenshotFallback ? "true" : "false");
        Environment.SetEnvironmentVariable("SCRAPSAE_MODE", scrapingMode);
        
        Console.WriteLine($"[DEBUG] Env SCRAPSAE_MANUAL_LOGIN: {Environment.GetEnvironmentVariable("SCRAPSAE_MANUAL_LOGIN")}");
        Console.WriteLine($"[DEBUG] Env SCRAPSAE_HEADLESS: {Environment.GetEnvironmentVariable("SCRAPSAE_HEADLESS")}");

        var result = await runner.RunForSiteAsync(siteId, token);
        return Results.Ok(result);
    }
    finally
    {
        Environment.SetEnvironmentVariable("SCRAPSAE_MANUAL_LOGIN", previousManual);
        Environment.SetEnvironmentVariable("SCRAPSAE_FORCE_MANUAL_LOGIN", previousForceManual);
        Environment.SetEnvironmentVariable("SCRAPSAE_HEADLESS", previousHeadless);
        Environment.SetEnvironmentVariable("SCRAPSAE_KEEP_BROWSER", previousKeepBrowser);
        Environment.SetEnvironmentVariable("SCRAPSAE_SCREENSHOT_FALLBACK", previousScreenshotFallback);
        Environment.SetEnvironmentVariable("SCRAPSAE_MODE", previousMode);
    }
});


// Endpoint para inspeccionar/scrapear URLs específicas directamente
app.MapPost("/api/scraping/inspect/{siteId:guid}", async (
    Guid siteId,
    DirectUrlsRequest request,
    IScrapingService scrapingService,
    ScrapingRunner runner,
    SupabaseTableService<SiteProfile> siteService,
    IScrapeControlService control,
    CancellationToken token) =>
{
    var site = await siteService.GetByIdAsync(siteId);
    if (site == null)
        return Results.NotFound(new { error = "Site not found" });
    
    // Configurar ambiente
    var previousHeadless = Environment.GetEnvironmentVariable("SCRAPSAE_HEADLESS");
    var previousManual = Environment.GetEnvironmentVariable("SCRAPSAE_FORCE_MANUAL_LOGIN");
    
    try
    {
        Environment.SetEnvironmentVariable("SCRAPSAE_HEADLESS", request.Headless ? "true" : "false");
        Environment.SetEnvironmentVariable("SCRAPSAE_FORCE_MANUAL_LOGIN", request.ManualLogin ? "true" : "false");
        
        // Establecer las URLs a inspeccionar como variable de entorno
        var urlsJson = System.Text.Json.JsonSerializer.Serialize(request.Urls);
        Environment.SetEnvironmentVariable("SCRAPSAE_DIRECT_URLS", urlsJson);
        Environment.SetEnvironmentVariable("SCRAPSAE_INSPECT_ONLY", request.InspectOnly ? "true" : "false");
        
        Console.WriteLine($"[DEBUG] Direct URL inspection for site {siteId}: {request.Urls.Count} URLs");
        
        // Ejecutar scraping con las URLs directas
        var scraped = await scrapingService.ScrapeDirectUrlsAsync(request.Urls, siteId, request.InspectOnly, token);
        
        // Persistir resultados si no es "solo inspección" o si queremos guardar lo validado (según lo solicitado)
        // El usuario solicitó que las URLs agregadas para análisis deben agregar el producto
        var (created, updated, skipped) = await runner.ProcessScrapedProductsAsync(siteId, scraped, token);
        
        // Mapear de vuelta a DirectUrlResult para la respuesta del API (compatibilidad frontend)
        var results = scraped.Select(p => new DirectUrlResult {
            Url = p.SourceUrl ?? string.Empty,
            Success = !string.IsNullOrEmpty(p.SkuSource),
            Title = p.Title,
            Sku = p.SkuSource,
            Price = p.Price?.ToString(),
            ImageUrl = p.ImageUrl,
            ScreenshotBase64 = p.ScreenshotBase64,
            DetectedType = "ProductDetail"
        }).ToList();
        
        return Results.Ok(new 
        { 
            totalUrls = request.Urls.Count,
            successCount = results.Count(r => r.Success),
            productsCreated = created,
            productsUpdated = updated,
            results = results,
            inspectOnly = request.InspectOnly
        });
    }
    finally
    {
        Environment.SetEnvironmentVariable("SCRAPSAE_HEADLESS", previousHeadless);
        Environment.SetEnvironmentVariable("SCRAPSAE_FORCE_MANUAL_LOGIN", previousManual);
        Environment.SetEnvironmentVariable("SCRAPSAE_DIRECT_URLS", null);
        Environment.SetEnvironmentVariable("SCRAPSAE_INSPECT_ONLY", null);
    }
});

app.MapPost("/api/scraping/session/confirm/{siteId}", (string siteId, IScrapingSignalService signal) =>
{
    signal.ConfirmLogin(siteId);
    return Results.Ok(new { message = "Login confirmed" });
});


app.MapPost("/api/scraping/pause/{siteId:guid}", (Guid siteId, IScrapeControlService control) =>
{
    control.Pause(siteId);
    return Results.Ok(new { state = control.GetStatus(siteId).State.ToString() });
});

app.MapPost("/api/scraping/resume/{siteId:guid}", (Guid siteId, IScrapeControlService control) =>
{
    control.Resume(siteId);
    return Results.Ok(new { state = control.GetStatus(siteId).State.ToString() });
});

app.MapPost("/api/scraping/stop/{siteId:guid}", (Guid siteId, IScrapeControlService control) =>
{
    control.Stop(siteId);
    return Results.Ok(new { state = control.GetStatus(siteId).State.ToString() });
});

app.MapGet("/api/scraping/status/{siteId:guid}", (Guid siteId, IScrapeControlService control) =>
{
    return Results.Ok(control.GetStatus(siteId));
});

app.MapPost("/api/ai/analyze-selectors", async (
    SelectorAnalysisRequest request,
    IAIProcessorService ai,
    CancellationToken token) =>
{
    var result = await ai.AnalyzeSelectorsAsync(request, token);
    return Results.Ok(result);
});

// Endpoint para aprender de URLs de ejemplo
app.MapPost("/api/scraping/learn/{siteId:guid}", async (
    Guid siteId,
    LearnUrlsRequest request,
    ILearningService learningService,
    CancellationToken token) =>
{
    var exampleUrls = request.Urls.Select(u => new ExampleUrl
    {
        Url = u.Url,
        Type = Enum.Parse<UrlType>(u.Type, ignoreCase: true)
    }).ToList();
    
    var results = await learningService.LearnFromUrlsAsync(siteId, exampleUrls, token);
    var patterns = await learningService.GetLearnedPatternsAsync(siteId);
    
    return Results.Ok(new { results, patterns });
});

app.MapGet("/api/scraping/patterns/{siteId:guid}", async (
    Guid siteId,
    ILearningService learningService) =>
{
    var patterns = await learningService.GetLearnedPatternsAsync(siteId);
    return patterns != null ? Results.Ok(patterns) : Results.NotFound();
});





app.MapGet("/api/sync-logs/live", async (
    Guid? siteId,
    DateTime? sinceUtc,
    ISupabaseRestClient supabase) =>
{
    var query = "sync_logs?select=*";
    if (siteId.HasValue)
    {
        query += $"&site_id=eq.{siteId}";
    }

    if (sinceUtc.HasValue)
    {
        query += $"&created_at=gt.{sinceUtc:O}";
    }

    query += "&order=created_at.asc";

    var result = await supabase.GetAsync<SyncLog>(query);
    return Results.Ok(result);
});

app.MapPost("/api/sae/send/{productId:guid}", async (
    Guid productId,
    SupabaseTableService<StagingProduct> stagingService,
    ISaeSdkService saeSdk,
    CancellationToken token) =>
{
    var product = await stagingService.GetByIdAsync(productId);
    if (product == null)
    {
        return Results.NotFound();
    }

    if (product.ExcludeFromSae)
    {
        return Results.BadRequest(new { message = "Product is excluded from SAE sync." });
    }

    var ok = await saeSdk.SendProductAsync(product, token);
    if (!ok)
    {
        return Results.StatusCode(StatusCodes.Status501NotImplemented);
    }

    product.Status = "synced";
    product.UpdatedAt = DateTime.UtcNow;
    await stagingService.UpdateAsync(product.Id, product);

    return Results.Ok(new { success = true });
});

app.MapPost("/api/sae/send-pending", async (
    SupabaseTableService<StagingProduct> stagingService,
    ISaeSdkService saeSdk,
    CancellationToken token) =>
{
    var products = await stagingService.GetAllAsync();
    var toSend = products
        .Where(p => !p.ExcludeFromSae && string.Equals(p.Status, "validated", StringComparison.OrdinalIgnoreCase))
        .ToList();

    var sent = 0;
    foreach (var product in toSend)
    {
        if (await saeSdk.SendProductAsync(product, token))
        {
            product.Status = "synced";
            product.UpdatedAt = DateTime.UtcNow;
            await stagingService.UpdateAsync(product.Id, product);
            sent++;
        }
    }

    return Results.Ok(new { total = toSend.Count, sent });
});

app.Run();
Log.CloseAndFlush();

static void MapCrud<T>(
    WebApplication app,
    string prefix,
    string tag,
    SupabaseTableService<T> service,
    Action<T> onCreate,
    Action<T> onUpdate) where T : class
{
    var group = app.MapGroup(prefix).WithTags(tag);

    group.MapGet("/", async () => Results.Ok(await service.GetAllAsync()));
    group.MapGet("/{id:guid}", async (Guid id) =>
    {
        var entity = await service.GetByIdAsync(id);
        return entity == null ? Results.NotFound() : Results.Ok(entity);
    });
    group.MapPost("/", async (T entity) =>
    {
        onCreate(entity);
        var created = await service.CreateAsync(entity);
        return Results.Ok(created);
    });
    group.MapPut("/{id:guid}", async (Guid id, T entity) =>
    {
        onUpdate(entity);
        var updated = await service.UpdateAsync(id, entity);
        return Results.Ok(updated);
    });
    group.MapDelete("/{id:guid}", async (Guid id) =>
    {
        await service.DeleteAsync(id);
        return Results.NoContent();
    });
}

public partial class Program
{
}
