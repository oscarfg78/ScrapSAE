using ScrapSAE.Api.Models;
using ScrapSAE.Api.Services;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Interfaces;
using ScrapSAE.Infrastructure.AI;
using ScrapSAE.Infrastructure.Scraping;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.runtime.json", optional: true, reloadOnChange: true);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<SettingsStore>();
builder.Services.AddSingleton<DiagnosticsService>();
builder.Services.AddSingleton<ISupabaseRestClient, SupabaseRestClient>();
builder.Services.AddSingleton<IScrapeControlService, ScrapeControlService>();
builder.Services.AddHttpClient("OpenAI");
builder.Services.AddSingleton<IAIProcessorService, OpenAIProcessorService>();

builder.Services.AddSingleton(sp => new SupabaseTableService<SiteProfile>(sp.GetRequiredService<ISupabaseRestClient>(), "config_sites"));
builder.Services.AddSingleton(sp => new SupabaseTableService<StagingProduct>(sp.GetRequiredService<ISupabaseRestClient>(), "staging_products"));
builder.Services.AddSingleton(sp => new SupabaseTableService<CategoryMapping>(sp.GetRequiredService<ISupabaseRestClient>(), "category_mapping"));
builder.Services.AddSingleton(sp => new SupabaseTableService<SyncLog>(sp.GetRequiredService<ISupabaseRestClient>(), "sync_logs"));
builder.Services.AddSingleton(sp => new SupabaseTableService<ExecutionReport>(sp.GetRequiredService<ISupabaseRestClient>(), "execution_reports"));

builder.Services.AddSingleton<IScrapingService, PlaywrightScrapingService>();
builder.Services.AddSingleton<ScrapingRunner>();
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

MapCrud(app, "/api/sites", "Site", 
    app.Services.GetRequiredService<SupabaseTableService<SiteProfile>>(),
    entity =>
    {
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

    var previousManual = Environment.GetEnvironmentVariable("SCRAPSAE_MANUAL_LOGIN");
    var previousForceManual = Environment.GetEnvironmentVariable("SCRAPSAE_FORCE_MANUAL_LOGIN");
    var previousHeadless = Environment.GetEnvironmentVariable("SCRAPSAE_HEADLESS");

    try
    {
        Environment.SetEnvironmentVariable("SCRAPSAE_MANUAL_LOGIN", manualLogin ? "true" : "false");
        Environment.SetEnvironmentVariable("SCRAPSAE_FORCE_MANUAL_LOGIN", manualLogin ? "true" : "false");
        Environment.SetEnvironmentVariable("SCRAPSAE_HEADLESS", headless ? "true" : "false");

        var result = await runner.RunForSiteAsync(siteId, token);
        return Results.Ok(result);
    }
    finally
    {
        Environment.SetEnvironmentVariable("SCRAPSAE_MANUAL_LOGIN", previousManual);
        Environment.SetEnvironmentVariable("SCRAPSAE_FORCE_MANUAL_LOGIN", previousForceManual);
        Environment.SetEnvironmentVariable("SCRAPSAE_HEADLESS", previousHeadless);
    }
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
