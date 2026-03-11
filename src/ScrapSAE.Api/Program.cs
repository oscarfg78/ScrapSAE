using ScrapSAE.Api.Models;
using ScrapSAE.Api.Services;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Interfaces;
using ScrapSAE.Infrastructure.AI;
using ScrapSAE.Infrastructure.Scraping;
using ScrapSAE.Infrastructure.Services;
using ScrapSAE.Infrastructure.Scraping.Strategies;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;


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
builder.Services.AddHttpClient("OnlineStore");
builder.Services.AddHttpClient("AttachmentAnalyzer");
builder.Services.AddSingleton<IAIProcessorService, OpenAIProcessorService>();
builder.Services.AddSingleton<IPdfAttachmentAnalyzer, PdfAttachmentAnalyzer>();

// Nuevos servicios para arquitectura adaptativa
builder.Services.AddSingleton<IPerformanceMetricsCollector, PerformanceMetricsCollector>();
builder.Services.AddSingleton<IPostExecutionAnalyzer, PostExecutionAnalyzerService>();
builder.Services.AddSingleton<IConfigurationUpdater, ScrapSAE.Api.Services.ConfigurationUpdaterService>();
builder.Services.AddSingleton<ILearningService, LearningService>();

// ===== SISTEMA ADAPTATIVO - FASE 1-4 =====
// TelemetrÃ­a Enriquecida
builder.Services.AddSingleton<ITelemetryService, TelemetryService>();

// ActualizaciÃ³n AutomÃ¡tica de ConfiguraciÃ³n
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
builder.Services.AddSingleton(sp => new SupabaseTableService<RescrapeJob>(sp.GetRequiredService<ISupabaseRestClient>(), "rescrape_jobs"));
builder.Services.AddSingleton(sp => new SupabaseTableService<RescrapeJobItem>(sp.GetRequiredService<ISupabaseRestClient>(), "rescrape_job_items"));
builder.Services.AddSingleton(sp => new SupabaseTableService<RescrapeJobLog>(sp.GetRequiredService<ISupabaseRestClient>(), "rescrape_job_logs"));

// Browser sharing for persistence
builder.Services.AddSingleton<IBrowserSharingService, BrowserSharingService>();

builder.Services.AddSingleton<ScrapingProcessManager>();
builder.Services.AddSingleton<IScrapingService, PlaywrightScrapingService>();
builder.Services.AddSingleton<ScrapingRunner>();
builder.Services.AddSingleton<IScrapingSignalService, ScrapingSignalService>();
builder.Services.AddSingleton<IRescrapeJobService, RescrapeJobService>();
builder.Services.AddHostedService<RescrapeJobBackgroundService>();

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

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (SupabaseConfigurationException ex)
    {
        Log.Warning(ex, "Supabase no configurado correctamente para la solicitud {Path}", context.Request.Path);
        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "supabase_not_configured",
                message = ex.Message
            });
        }
    }
});

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

MapSiteCrud(
    app,
    app.Services.GetRequiredService<SupabaseTableService<SiteProfile>>(),
    app.Services.GetRequiredService<ISupabaseRestClient>());

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


// Endpoint para inspeccionar/scrapear URLs especÃ­ficas directamente
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
        scrapingService.RegisterSite(site);

        // Ejecutar scraping con las URLs directas.
        var scraped = await scrapingService.ScrapeDirectUrlsAsync(
            request.Urls,
            siteId,
            new DirectUrlScrapeOptions
            {
                InspectOnly = request.InspectOnly,
                SingleProductOnly = false,
                ExpandRelated = true
            },
            token);

        var created = 0;
        var updated = 0;
        if (!request.InspectOnly)
        {
            var processed = await runner.ProcessScrapedProductsAsync(siteId, scraped, token);
            created = processed.created;
            updated = processed.updated;
        }
        
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
        
        var response = new InspectUrlsResponse
        {
            TotalUrls = request.Urls.Count,
            SuccessCount = results.Count(r => r.Success),
            ProductsCreated = created,
            ProductsUpdated = updated,
            Results = results,
            InspectOnly = request.InspectOnly
        };

        return Results.Ok(response);
    }
    finally
    {
        Environment.SetEnvironmentVariable("SCRAPSAE_HEADLESS", previousHeadless);
        Environment.SetEnvironmentVariable("SCRAPSAE_FORCE_MANUAL_LOGIN", previousManual);
        Environment.SetEnvironmentVariable("SCRAPSAE_DIRECT_URLS", null);
        Environment.SetEnvironmentVariable("SCRAPSAE_INSPECT_ONLY", null);
    }
});

app.MapPost("/api/scraping/rescrape", async (
    RescrapeRequest request,
    IRescrapeJobService rescrapeJobs,
    CancellationToken token) =>
{
    if (request.ProductIds == null || request.ProductIds.Count == 0)
    {
        return Results.BadRequest(new { message = "Se requiere al menos un productId." });
    }

    try
    {
        var created = await rescrapeJobs.EnqueueAsync(request, token);
        return Results.Accepted($"/api/scraping/rescrape/{created.JobId}", created);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapGet("/api/scraping/rescrape/{jobId:guid}", async (
    Guid jobId,
    IRescrapeJobService rescrapeJobs,
    CancellationToken token) =>
{
    var status = await rescrapeJobs.GetStatusAsync(jobId, token);
    return status == null ? Results.NotFound(new { message = "Job no encontrado." }) : Results.Ok(status);
});

app.MapGet("/api/scraping/rescrape/{jobId:guid}/items", async (
    Guid jobId,
    IRescrapeJobService rescrapeJobs,
    CancellationToken token) =>
{
    var status = await rescrapeJobs.GetStatusAsync(jobId, token);
    if (status == null)
    {
        return Results.NotFound(new { message = "Job no encontrado." });
    }

    var items = await rescrapeJobs.GetItemsAsync(jobId, token);
    return Results.Ok(items);
});

app.MapGet("/api/scraping/rescrape/{jobId:guid}/logs", async (
    Guid jobId,
    int? take,
    IRescrapeJobService rescrapeJobs,
    CancellationToken token) =>
{
    var status = await rescrapeJobs.GetStatusAsync(jobId, token);
    if (status == null)
    {
        return Results.NotFound(new { message = "Job no encontrado." });
    }

    var logs = await rescrapeJobs.GetLogsAsync(jobId, take ?? 200, token);
    return Results.Ok(logs);
});

app.MapPost("/api/scraping/rescrape/{jobId:guid}/pause", async (
    Guid jobId,
    IRescrapeJobService rescrapeJobs,
    CancellationToken token) =>
{
    var paused = await rescrapeJobs.PauseAsync(jobId, token);
    return paused ? Results.Ok(new { jobId, status = "paused" }) : Results.NotFound(new { message = "Job no encontrado." });
});

app.MapPost("/api/scraping/rescrape/{jobId:guid}/resume", async (
    Guid jobId,
    IRescrapeJobService rescrapeJobs,
    CancellationToken token) =>
{
    var resumed = await rescrapeJobs.ResumeAsync(jobId, token);
    return resumed ? Results.Ok(new { jobId, status = "queued" }) : Results.NotFound(new { message = "Job no encontrado." });
});

app.MapPost("/api/scraping/rescrape/{jobId:guid}/cancel", async (
    Guid jobId,
    IRescrapeJobService rescrapeJobs,
    CancellationToken token) =>
{
    var cancelled = await rescrapeJobs.CancelAsync(jobId, token);
    return cancelled ? Results.Ok(new { jobId, status = "cancelled" }) : Results.NotFound(new { message = "Job no encontrado." });
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
    try
    {
        var product = await stagingService.GetByIdAsync(productId);
        if (product == null)
        {
            return Results.NotFound(new { message = "Producto no encontrado." });
        }

        if (product.ExcludeFromSae)
        {
            return Results.BadRequest(new { message = "El producto estÃ¡ excluido de sincronizaciÃ³n SAE." });
        }

        var ok = await saeSdk.SendProductAsync(product, token);
        if (!ok)
        {
            return Results.Problem(
                title: "No fue posible enviar el producto a SAE.",
                detail: "Verifica configuraciÃ³n SAE (ruta SDK o conexiÃ³n DB), credenciales y logs del backend.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        product.Status = "synced";
        product.UpdatedAt = DateTime.UtcNow;
        await stagingService.UpdateAsync(product.Id, product);

        return Results.Ok(new { success = true });
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Error inesperado al enviar a SAE.",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
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

app.MapPost("/api/online-store/send-pending", async (
    SupabaseTableService<StagingProduct> stagingService,
    IStagingService stagingOps,
    ISyncLogService syncLogService,
    SettingsStore settingsStore,
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    CancellationToken token) =>
{
    var settings = settingsStore.Get();
    var endpoint = ResolveOnlineStoreEndpoint(settings?.OnlineStoreBaseUrl, configuration["FlashlyApi:BaseUrl"]);
    var apiKey = FirstNonEmpty(settings?.OnlineStoreApiKey, configuration["FlashlyApi:ApiKey"]);

    if (endpoint == null || string.IsNullOrWhiteSpace(apiKey))
    {
        return Results.BadRequest(new
        {
            message = "ConfiguraciÃ³n incompleta de tienda en lÃ­nea. Revisa Base URL y API Key."
        });
    }

    var products = await stagingService.GetAllAsync();
    var toSend = products
        .Where(p => !p.IsApartado)
        .Where(p => !string.Equals(p.FlashlySyncStatus, "synced", StringComparison.OrdinalIgnoreCase))
        .Where(p => !string.IsNullOrWhiteSpace(p.SkuSource))
        .ToList();

    if (toSend.Count == 0)
    {
        return Results.Ok(new
        {
            total = 0,
            sent = 0,
            failed = 0,
            message = "No hay productos pendientes por enviar a tienda en lÃ­nea.",
            results = Array.Empty<object>()
        });
    }

    var client = httpClientFactory.CreateClient("OnlineStore");
    client.DefaultRequestHeaders.Remove("X-API-Key");
    client.DefaultRequestHeaders.Add("X-API-Key", apiKey);

    var sent = 0;
    var failed = 0;
    var results = new List<object>(toSend.Count);

    foreach (var product in toSend)
    {
        var sku = (product.SkuSource ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(sku))
        {
            failed++;
            var noSkuMessage = "SKU fuente vacÃ­o.";
            await stagingOps.UpdateFlashlySyncInfoAsync(product.Id, "error", product.FlashlyProductId, product.FlashlySyncedAt, noSkuMessage);
            results.Add(new { productId = product.Id, sourceSku = product.SkuSource, success = false, message = noSkuMessage });
            continue;
        }

        try
        {
            var payloadProduct = BuildOnlineStoreProductPayload(product, settings?.OnlineStoreName);
            if (!ValidateOnlineStorePayload(payloadProduct, out var validationError))
            {
                var invalidPayload = JsonSerializer.Serialize(new { products = new[] { payloadProduct } });
                failed++;
                var validationMessage = BuildOnlineStoreErrorMessage(validationError!, endpoint, invalidPayload, null, null);
                await stagingOps.UpdateFlashlySyncInfoAsync(product.Id, "error", product.FlashlyProductId, product.FlashlySyncedAt, validationMessage);
                results.Add(new { productId = product.Id, sourceSku = sku, success = false, message = validationMessage });
                continue;
            }

            var requestPayload = JsonSerializer.Serialize(new { products = new[] { payloadProduct } });
            using var content = new StringContent(requestPayload, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(endpoint, content, token);
            var upstreamResponseBody = await response.Content.ReadAsStringAsync(token);

            var ok = response.IsSuccessStatusCode;
            string? message = null;

            if (ok)
            {
                var parsed = TryParseFlashlyResponse(upstreamResponseBody);
                var error = parsed?.Results?.Errors?.FirstOrDefault();
                if (error != null && !string.IsNullOrWhiteSpace(error.Error))
                {
                    ok = false;
                    message = BuildOnlineStoreErrorMessage(
                        error.Error,
                        endpoint,
                        requestPayload,
                        (int)response.StatusCode,
                        upstreamResponseBody);
                }
                else
                {
                    message = parsed?.Message ?? "Enviado correctamente.";
                }
            }
            else
            {
                message = BuildOnlineStoreErrorMessage(
                    $"HTTP {(int)response.StatusCode}",
                    endpoint,
                    requestPayload,
                    (int)response.StatusCode,
                    upstreamResponseBody);
            }

            if (ok)
            {
                sent++;
                await stagingOps.UpdateFlashlySyncInfoAsync(product.Id, "synced", product.FlashlyProductId, DateTime.UtcNow, null);
            }
            else
            {
                failed++;
                await stagingOps.UpdateFlashlySyncInfoAsync(product.Id, "error", product.FlashlyProductId, product.FlashlySyncedAt, message);
            }

            results.Add(new { productId = product.Id, sourceSku = sku, success = ok, message });
        }
        catch (Exception ex)
        {
            failed++;
            var exceptionMessage = BuildOnlineStoreErrorMessage(ex.Message, endpoint, null, null, null);
            await stagingOps.UpdateFlashlySyncInfoAsync(product.Id, "error", product.FlashlyProductId, product.FlashlySyncedAt, exceptionMessage);
            results.Add(new { productId = product.Id, sourceSku = sku, success = false, message = exceptionMessage });
        }
    }

    var summaryMessage = $"Envio tienda en linea finalizado. Enviados: {sent}. Fallidos: {failed}.";
    await syncLogService.LogOperationAsync(new SyncLog
    {
        OperationType = "online_store_sync",
        Status = failed == 0 ? "success" : "warning",
        Message = summaryMessage,
        Details = JsonSerializer.Serialize(results),
        CreatedAt = DateTime.UtcNow
    });

    return Results.Ok(new
    {
        total = toSend.Count,
        sent,
        failed,
        message = summaryMessage,
        results
    });
});

app.MapPost("/api/online-store/send/{productId:guid}", async (
    Guid productId,
    SupabaseTableService<StagingProduct> stagingService,
    IStagingService stagingOps,
    SettingsStore settingsStore,
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    CancellationToken token) =>
{
    var product = await stagingService.GetByIdAsync(productId);
    if (product == null)
    {
        return Results.NotFound(new { message = "Producto no encontrado." });
    }

    if (product.IsApartado)
    {
        return Results.BadRequest(new { message = "El producto esta apartado y no puede enviarse." });
    }

    var settings = settingsStore.Get();
    var endpoint = ResolveOnlineStoreEndpoint(settings?.OnlineStoreBaseUrl, configuration["FlashlyApi:BaseUrl"]);
    var apiKey = FirstNonEmpty(settings?.OnlineStoreApiKey, configuration["FlashlyApi:ApiKey"]);

    if (endpoint == null || string.IsNullOrWhiteSpace(apiKey))
    {
        return Results.BadRequest(new { message = "Configuracion incompleta de tienda en linea (Base URL / API Key)." });
    }

    if (string.IsNullOrWhiteSpace(product.SkuSource))
    {
        return Results.BadRequest(new { message = "SKU fuente vacio." });
    }

    try
    {
        var client = httpClientFactory.CreateClient("OnlineStore");
        client.DefaultRequestHeaders.Remove("X-API-Key");
        client.DefaultRequestHeaders.Add("X-API-Key", apiKey);

        var payloadProduct = BuildOnlineStoreProductPayload(product, settings?.OnlineStoreName);
        if (!ValidateOnlineStorePayload(payloadProduct, out var validationError))
        {
            var invalidPayload = JsonSerializer.Serialize(new { products = new[] { payloadProduct } });
            var validationMessage = BuildOnlineStoreErrorMessage(validationError!, endpoint, invalidPayload, null, null);
            await stagingOps.UpdateFlashlySyncInfoAsync(product.Id, "error", product.FlashlyProductId, product.FlashlySyncedAt, validationMessage);
            return Results.BadRequest(new { message = validationMessage, payload = invalidPayload });
        }

        var requestPayload = JsonSerializer.Serialize(new { products = new[] { payloadProduct } });
        using var content = new StringContent(requestPayload, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(endpoint, content, token);
        var upstreamResponseBody = await response.Content.ReadAsStringAsync(token);

        var ok = response.IsSuccessStatusCode;
        string? message = null;
        if (ok)
        {
            var parsed = TryParseFlashlyResponse(upstreamResponseBody);
            var error = parsed?.Results?.Errors?.FirstOrDefault();
            if (error != null && !string.IsNullOrWhiteSpace(error.Error))
            {
                ok = false;
                message = BuildOnlineStoreErrorMessage(
                    error.Error,
                    endpoint,
                    requestPayload,
                    (int)response.StatusCode,
                    upstreamResponseBody);
            }
            else
            {
                message = parsed?.Message ?? "Enviado correctamente.";
            }
        }
        else
        {
            message = BuildOnlineStoreErrorMessage(
                $"HTTP {(int)response.StatusCode}",
                endpoint,
                requestPayload,
                (int)response.StatusCode,
                upstreamResponseBody);
        }

        if (ok)
        {
            await stagingOps.UpdateFlashlySyncInfoAsync(product.Id, "synced", product.FlashlyProductId, DateTime.UtcNow, null);
            return Results.Ok(new { success = true, message });
        }

        await stagingOps.UpdateFlashlySyncInfoAsync(product.Id, "error", product.FlashlyProductId, product.FlashlySyncedAt, message);
        return Results.BadRequest(new
        {
            message = message ?? "Error al enviar producto.",
            endpoint = endpoint.ToString(),
            payload = requestPayload,
            upstreamStatusCode = (int)response.StatusCode,
            upstreamResponseBody
        });
    }
    catch (Exception ex)
    {
        var message = BuildOnlineStoreErrorMessage(ex.Message, endpoint, null, null, null);
        await stagingOps.UpdateFlashlySyncInfoAsync(product.Id, "error", product.FlashlyProductId, product.FlashlySyncedAt, message);
        return Results.Problem(title: "Error inesperado en envio a tienda en linea.", detail: message);
    }
});

app.Run();
Log.CloseAndFlush();

static void MapSiteCrud(
    WebApplication app,
    SupabaseTableService<SiteProfile> service,
    ISupabaseRestClient supabase)
{
    var group = app.MapGroup("/api/sites").WithTags("Site");

    group.MapGet("/", async () =>
    {
        var sites = await service.GetAllAsync();
        var normalized = sites
            .Select(SiteProfileSchemaCompatibility.NormalizeFromStorage)
            .ToList();
        return Results.Ok(normalized);
    });

    group.MapGet("/{id:guid}", async (Guid id) =>
    {
        var entity = await service.GetByIdAsync(id);
        if (entity == null)
        {
            return Results.NotFound();
        }

        return Results.Ok(SiteProfileSchemaCompatibility.NormalizeFromStorage(entity));
    });

    group.MapPost("/", async (SiteProfile entity) =>
    {
        if (entity.Id == Guid.Empty)
        {
            entity.Id = Guid.NewGuid();
        }

        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        var created = await SiteProfileSchemaCompatibility.CreateWithFallbackAsync(service, supabase, entity);
        return Results.Ok(created);
    });

    group.MapPut("/{id:guid}", async (Guid id, SiteProfile entity) =>
    {
        entity.Id = id;
        entity.UpdatedAt = DateTime.UtcNow;
        var updated = await SiteProfileSchemaCompatibility.UpdateWithFallbackAsync(service, supabase, id, entity);
        return Results.Ok(updated);
    });

    group.MapDelete("/{id:guid}", async (Guid id) =>
    {
        await service.DeleteAsync(id);
        return Results.NoContent();
    });
}

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

static string BuildOnlineStoreErrorMessage(
    string headline,
    Uri endpoint,
    string? requestPayload,
    int? upstreamStatusCode,
    string? upstreamResponseBody)
{
    var lines = new List<string> { headline };
    lines.Add($"endpoint: {endpoint}");
    if (upstreamStatusCode.HasValue)
    {
        lines.Add($"upstream_status: {upstreamStatusCode.Value}");
    }

    if (!string.IsNullOrWhiteSpace(requestPayload))
    {
        lines.Add($"payload: {requestPayload}");
    }

    if (!string.IsNullOrWhiteSpace(upstreamResponseBody))
    {
        lines.Add($"upstream_response: {upstreamResponseBody}");
    }

    return string.Join(Environment.NewLine, lines);
}

static bool ValidateOnlineStorePayload(JsonObject payload, out string? error)
{
    var missing = new List<string>();
    var invalid = new List<string>();

    bool MissingString(string key)
    {
        if (!payload.TryGetPropertyValue(key, out var node) || node == null)
        {
            return true;
        }

        if (node is JsonValue valueNode && valueNode.TryGetValue<string>(out var value))
        {
            return string.IsNullOrWhiteSpace(value);
        }

        return true;
    }

    bool MissingNumber(string key, Func<decimal, bool>? predicate = null)
    {
        if (!payload.TryGetPropertyValue(key, out var node) || node == null)
        {
            return true;
        }

        if (node is JsonValue valueNode)
        {
            decimal numericValue;
            if (valueNode.TryGetValue<decimal>(out var asDecimal))
            {
                numericValue = asDecimal;
            }
            else if (valueNode.TryGetValue<int>(out var asInt))
            {
                numericValue = asInt;
            }
            else if (valueNode.TryGetValue<long>(out var asLong))
            {
                numericValue = asLong;
            }
            else if (valueNode.TryGetValue<double>(out var asDouble))
            {
                numericValue = (decimal)asDouble;
            }
            else
            {
                return true;
            }

            if (predicate == null)
            {
                return false;
            }

            return !predicate(numericValue);
        }

        return true;
    }

    bool InvalidArrayContent(string key)
    {
        if (!payload.TryGetPropertyValue(key, out var node) || node is not JsonArray arr)
        {
            return true;
        }

        return arr.Count == 0 || arr.Any(x =>
        {
            if (x is not JsonValue valueNode || !valueNode.TryGetValue<string>(out var value))
            {
                return true;
            }

            return string.IsNullOrWhiteSpace(value);
        });
    }

    bool MissingArray(string key)
    {
        if (!payload.TryGetPropertyValue(key, out var node) || node == null)
        {
            return true;
        }

        return node is not JsonArray;
    }

    bool MissingObject(string key)
    {
        if (!payload.TryGetPropertyValue(key, out var node) || node == null)
        {
            return true;
        }

        return node is not JsonObject;
    }

    if (MissingString("source_sku")) missing.Add("source_sku");
    if (MissingString("name")) missing.Add("name");
    if (MissingString("description")) missing.Add("description");
    if (MissingNumber("purchase_price", x => x >= 0m)) missing.Add("purchase_price");
    if (MissingString("supplier_name")) missing.Add("supplier_name");
    if (MissingString("supplier_sku")) missing.Add("supplier_sku");
    if (MissingArray("category_path")) missing.Add("category_path");
    if (MissingArray("images")) missing.Add("images");
    if (MissingArray("attachments")) missing.Add("attachments");
    if (MissingObject("specifications")) missing.Add("specifications");
    if (MissingNumber("stock", x => x >= 0m)) missing.Add("stock");
    if (!MissingArray("category_path") && InvalidArrayContent("category_path")) invalid.Add("category_path");

    if (missing.Count == 0 && invalid.Count == 0)
    {
        error = null;
        return true;
    }

    var problems = new List<string>();
    if (missing.Count > 0)
    {
        problems.Add($"faltantes/invalidos: {string.Join(", ", missing)}");
    }

    if (invalid.Count > 0)
    {
        problems.Add($"contenido invalido: {string.Join(", ", invalid)}");
    }

    error = $"Payload invalido. Campos requeridos con error -> {string.Join(" | ", problems)}";
    return false;
}

static Uri? ResolveOnlineStoreEndpoint(string? runtimeUrl, string? fallbackBaseUrl)
{
    const string syncPath = "/api/v1/products/sync";
    var raw = FirstNonEmpty(runtimeUrl, fallbackBaseUrl);
    if (string.IsNullOrWhiteSpace(raw))
    {
        return null;
    }

    if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
    {
        return null;
    }

    if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
    {
        return null;
    }

    if (string.Equals(uri.AbsolutePath, syncPath, StringComparison.OrdinalIgnoreCase))
    {
        var normalized = new UriBuilder(uri)
        {
            Path = syncPath,
            Query = string.Empty
        };
        return normalized.Uri;
    }

    // The integration contract requires posting to /api/v1/products/sync.
    // Normalize any base/admin URL to the required endpoint to avoid 404.
    var builder = new UriBuilder(uri)
    {
        Path = syncPath,
        Query = string.Empty
    };
    return builder.Uri;
}

static string? FirstNonEmpty(params string?[] values)
{
    return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}

static FlashlySyncResponse? TryParseFlashlyResponse(string payload)
{
    if (string.IsNullOrWhiteSpace(payload))
    {
        return null;
    }

    try
    {
        return JsonSerializer.Deserialize<FlashlySyncResponse>(payload, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }
    catch
    {
        return null;
    }
}

static JsonObject BuildOnlineStoreProductPayload(StagingProduct product, string? defaultSupplierName)
{
    var sourceSku = (product.SkuSource ?? string.Empty).Trim();
    var name = sourceSku;
    var description = sourceSku;
    var purchasePrice = 0m;
    var supplierName = string.IsNullOrWhiteSpace(defaultSupplierName) ? "Proveedor" : defaultSupplierName!.Trim();
    var supplierSku = sourceSku;
    var categoryPath = new List<string> { "General" };
    var images = new List<string>();
    var attachments = new JsonArray();
    var specificationsObject = new JsonObject();
    var stock = 0;
    var productUrl = string.IsNullOrWhiteSpace(product.SourceUrl) ? null : product.SourceUrl!.Trim();

    if (!string.IsNullOrWhiteSpace(product.AIProcessedJson))
    {
        try
        {
            using var document = JsonDocument.Parse(product.AIProcessedJson);
            var root = document.RootElement;

            sourceSku = FirstNonEmpty(
                ReadJsonString(root, "sourceSku", "source_sku", "skuSource", "sku_source"),
                ReadJsonString(root, "sku", "Sku"),
                sourceSku) ?? sourceSku;

            name = FirstNonEmpty(ReadJsonString(root, "name", "Name", "title", "Title"), name) ?? name;
            description = FirstNonEmpty(ReadJsonString(root, "description", "Description"), description, name) ?? name;
            purchasePrice = ReadJsonDecimal(root, "purchasePrice", "purchase_price", "price", "Price");
            supplierName = FirstNonEmpty(ReadJsonString(root, "supplierName", "supplier_name", "supplier", "brand", "Brand"), supplierName) ?? supplierName;
            supplierSku = FirstNonEmpty(
                ReadJsonString(root, "supplierSku", "supplier_sku"),
                ReadJsonString(root, "skuSupplier", "supplier_code"),
                sourceSku) ?? sourceSku;
            productUrl = FirstNonEmpty(
                ReadJsonString(root, "product_url", "productUrl", "source_url", "sourceUrl", "url", "Url"),
                productUrl);
            categoryPath = ReadJsonStringArray(root, "category_path", "categoryPath", "categories", "Categories");
            if (categoryPath.Count == 0)
            {
                var category = ReadJsonString(root, "category", "Category");
                if (!string.IsNullOrWhiteSpace(category))
                {
                    categoryPath = SplitCategoryPath(category!);
                }
            }

            images = ReadJsonStringArray(
                root,
                "images", "Images",
                "imageUrls", "image_urls", "ImageUrls",
                "primaryImageUrls", "primary_image_urls", "PrimaryImageUrls");
            if (images.Count == 0)
            {
                var imageUrl = ReadJsonString(
                    root,
                    "imageUrl", "image_url", "ImageUrl",
                    "primaryImageUrl", "primary_image_url", "PrimaryImageUrl",
                    "thumbnailUrl", "thumbnail_url", "ThumbnailUrl");
                if (!string.IsNullOrWhiteSpace(imageUrl))
                {
                    images.Add(imageUrl!.Trim());
                }
            }

            attachments = ReadAttachments(root);
            stock = ReadJsonInt(root, "stock", "Stock");

            if (TryGetPropertyIgnoreCase(root, "specifications", out var specs) ||
                TryGetPropertyIgnoreCase(root, "Specifications", out specs))
            {
                var parsedSpecs = JsonNode.Parse(specs.GetRawText());
                if (parsedSpecs is JsonObject jsonObject)
                {
                    specificationsObject = jsonObject;
                }
            }

            MergeSpecificationsFromAttributes(specificationsObject, root);
        }
        catch
        {
            // Invalid JSON should not block outbound sync.
        }
    }

    // Fallback para datos legacy almacenados en raw_data (cuando ai_processed_json no trae todo).
    if ((images.Count == 0 || attachments.Count == 0 || specificationsObject.Count == 0) &&
        !string.IsNullOrWhiteSpace(product.RawData))
    {
        try
        {
            using var rawDoc = JsonDocument.Parse(product.RawData);
            var rawRoot = rawDoc.RootElement;

            if (images.Count == 0)
            {
                images = ReadJsonStringArray(
                    rawRoot,
                    "images", "Images",
                    "imageUrls", "image_urls", "ImageUrls",
                    "primaryImageUrls", "primary_image_urls", "PrimaryImageUrls");
                if (images.Count == 0)
                {
                    var imageUrl = ReadJsonString(
                        rawRoot,
                        "imageUrl", "image_url", "ImageUrl",
                        "primaryImageUrl", "primary_image_url", "PrimaryImageUrl",
                        "thumbnailUrl", "thumbnail_url", "ThumbnailUrl");
                    if (!string.IsNullOrWhiteSpace(imageUrl))
                    {
                        images.Add(imageUrl.Trim());
                    }
                }
            }

            productUrl = FirstNonEmpty(
                ReadJsonString(rawRoot, "product_url", "productUrl", "source_url", "sourceUrl", "url", "Url"),
                productUrl);

            if (attachments.Count == 0)
            {
                attachments = ReadAttachments(rawRoot);
            }

            if (specificationsObject.Count == 0)
            {
                if (TryGetPropertyIgnoreCase(rawRoot, "specifications", out var rawSpecs) ||
                    TryGetPropertyIgnoreCase(rawRoot, "Specifications", out rawSpecs))
                {
                    var parsedSpecs = JsonNode.Parse(rawSpecs.GetRawText());
                    if (parsedSpecs is JsonObject jsonObject)
                    {
                        specificationsObject = jsonObject;
                    }
                }

                MergeSpecificationsFromAttributes(specificationsObject, rawRoot);
            }
        }
        catch
        {
            // Ignore invalid raw_data payloads.
        }
    }

    if (string.IsNullOrWhiteSpace(name))
    {
        name = sourceSku;
    }

    if (string.IsNullOrWhiteSpace(description))
    {
        description = name;
    }

    if (string.IsNullOrWhiteSpace(supplierName))
    {
        supplierName = "Proveedor";
    }

    if (string.IsNullOrWhiteSpace(supplierSku))
    {
        supplierSku = sourceSku;
    }

    images = images
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(x => x.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    if (categoryPath.Count == 0)
    {
        categoryPath.Add("General");
    }

    if (string.IsNullOrWhiteSpace(description))
    {
        description = name;
    }

    if (!string.IsNullOrWhiteSpace(product.SourceUrl))
    {
        specificationsObject["source_url"] = product.SourceUrl;
    }

    var payload = new JsonObject
    {
        ["source_sku"] = sourceSku,
        ["name"] = name,
        ["description"] = description,
        ["purchase_price"] = purchasePrice,
        ["supplier_name"] = supplierName,
        ["supplier_sku"] = supplierSku,
        ["product_url"] = string.IsNullOrWhiteSpace(productUrl) ? null : productUrl,
        ["source_url"] = string.IsNullOrWhiteSpace(productUrl) ? null : productUrl,
        ["categories"] = new JsonArray(categoryPath.Select(v => JsonValue.Create(v)).ToArray()),
        ["category_path"] = new JsonArray(categoryPath.Select(v => JsonValue.Create(v)).ToArray()),
        ["image_urls"] = new JsonArray(images.Select(v => JsonValue.Create(v)).ToArray()),
        ["images"] = new JsonArray(images.Select(v => JsonValue.Create(v)).ToArray()),
        ["attachments"] = attachments,
        ["specifications"] = specificationsObject,
        ["stock"] = stock
    };

    return payload;
}

static List<string> SplitCategoryPath(string rawCategory)
{
    return rawCategory
        .Split(new[] { '>', '|', ';' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(v => v.Trim())
        .Where(v => !string.IsNullOrWhiteSpace(v))
        .ToList();
}

static JsonArray ReadAttachments(JsonElement root)
{
    if (TryGetPropertyIgnoreCase(root, "attachments", out var attachmentsElement) ||
        TryGetPropertyIgnoreCase(root, "Attachments", out attachmentsElement) ||
        TryGetPropertyIgnoreCase(root, "files", out attachmentsElement) ||
        TryGetPropertyIgnoreCase(root, "Files", out attachmentsElement))
    {
        if (attachmentsElement.ValueKind == JsonValueKind.Array)
        {
            var attachments = new JsonArray();
            foreach (var item in attachmentsElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var directUrl = item.GetString();
                    if (!string.IsNullOrWhiteSpace(directUrl))
                    {
                        attachments.Add(new JsonObject { ["url"] = directUrl.Trim() });
                    }
                    continue;
                }

                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var url = ReadJsonString(item, "url", "Url", "fileUrl", "file_url", "href", "Href", "link", "Link");
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                var name = ReadJsonString(item, "name", "Name", "fileName", "filename");
                var type = ReadJsonString(item, "type", "Type", "mimeType", "mime_type");

                var attachment = new JsonObject
                {
                    ["url"] = url!.Trim()
                };

                if (!string.IsNullOrWhiteSpace(name))
                {
                    attachment["name"] = name!.Trim();
                }

                if (!string.IsNullOrWhiteSpace(type))
                {
                    attachment["type"] = type!.Trim();
                }

                attachments.Add(attachment);
            }

            return attachments;
        }
    }

    return new JsonArray();
}

static string? ReadJsonString(JsonElement root, params string[] names)
{
    foreach (var name in names)
    {
        if (!TryGetPropertyIgnoreCase(root, name, out var element))
        {
            continue;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString();
        }

        if (element.ValueKind == JsonValueKind.Number || element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False)
        {
            return element.ToString();
        }
    }

    return null;
}

static decimal ReadJsonDecimal(JsonElement root, params string[] names)
{
    foreach (var name in names)
    {
        if (!TryGetPropertyIgnoreCase(root, name, out var element))
        {
            continue;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var value))
        {
            return value;
        }

        if (element.ValueKind == JsonValueKind.String && decimal.TryParse(element.GetString(), out var parsed))
        {
            return parsed;
        }
    }

    return 0m;
}

static int ReadJsonInt(JsonElement root, params string[] names)
{
    foreach (var name in names)
    {
        if (!TryGetPropertyIgnoreCase(root, name, out var element))
        {
            continue;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value))
        {
            return value;
        }

        if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out var parsed))
        {
            return parsed;
        }
    }

    return 0;
}

static List<string> ReadJsonStringArray(JsonElement root, params string[] names)
{
    foreach (var name in names)
    {
        if (!TryGetPropertyIgnoreCase(root, name, out var element))
        {
            continue;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray()
                .Select(x =>
                {
                    if (x.ValueKind == JsonValueKind.String)
                    {
                        return x.GetString();
                    }

                    if (x.ValueKind == JsonValueKind.Object)
                    {
                        return ReadJsonString(x, "url", "Url", "src", "Src", "imageUrl", "image_url", "href", "Href", "fileUrl", "file_url");
                    }

                    return x.ToString();
                })
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim())
                .ToList();
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            var single = ReadJsonString(element, "url", "Url", "src", "Src", "imageUrl", "image_url", "href", "Href");
            if (!string.IsNullOrWhiteSpace(single))
            {
                return new List<string> { single.Trim() };
            }
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var raw = element.GetString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new List<string>();
            }

            return raw.Split('|', ';', ',')
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
        }
    }

    return new List<string>();
}

static void MergeSpecificationsFromAttributes(JsonObject target, JsonElement root)
{
    // 1) Estructura convencional "attributes".
    if (TryGetPropertyIgnoreCase(root, "attributes", out var attributesElement) ||
        TryGetPropertyIgnoreCase(root, "Attributes", out attributesElement))
    {
        if (attributesElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in attributesElement.EnumerateObject())
            {
                var value = JsonElementToString(prop.Value);
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                var key = NormalizeSpecificationKey(prop.Name);
                if (!target.ContainsKey(key))
                {
                    target[key] = value;
                }
            }
        }
    }

    // 2) Formato plano legacy con claves tech_* en raíz.
    foreach (var prop in root.EnumerateObject())
    {
        if (!prop.Name.StartsWith("tech_", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        var value = JsonElementToString(prop.Value);
        if (string.IsNullOrWhiteSpace(value))
        {
            continue;
        }

        var key = NormalizeSpecificationKey(prop.Name);
        if (!target.ContainsKey(key))
        {
            target[key] = value;
        }
    }
}

static string NormalizeSpecificationKey(string rawKey)
{
    var key = rawKey.Trim();
    if (key.StartsWith("tech_", StringComparison.OrdinalIgnoreCase))
    {
        key = key.Substring(5);
    }

    key = key.Replace("_", " ").Trim();
    return key;
}

static string? JsonElementToString(JsonElement value)
{
    return value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()?.Trim(),
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToString(),
        _ => null
    };
}

static bool TryGetPropertyIgnoreCase(JsonElement root, string propertyName, out JsonElement value)
{
    foreach (var property in root.EnumerateObject())
    {
        if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
        {
            value = property.Value;
            return true;
        }
    }

    value = default;
    return false;
}

public partial class Program
{
}

