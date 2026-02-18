using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Interfaces;

namespace ScrapSAE.Api.Services;

public sealed class RescrapeJobService : IRescrapeJobService
{
    private static readonly string[] TerminalItemStatuses = { "succeeded", "failed", "skipped" };
    private const int ItemTimeoutMinutes = 6;

    private readonly SupabaseTableService<RescrapeJob> _jobsTable;
    private readonly SupabaseTableService<RescrapeJobItem> _jobItemsTable;
    private readonly SupabaseTableService<RescrapeJobLog> _jobLogsTable;
    private readonly SupabaseTableService<SiteProfile> _siteTable;
    private readonly SupabaseTableService<StagingProduct> _stagingTable;
    private readonly IScrapingService _scrapingService;
    private readonly ScrapingRunner _scrapingRunner;
    private readonly IPdfAttachmentAnalyzer _pdfAttachmentAnalyzer;
    private readonly ILogger<RescrapeJobService> _logger;
    private readonly SemaphoreSlim _singleProcessor = new(1, 1);
    private bool _disableJobLogPersistence;
    private int _jobLogPersistenceWarningLogged;
    private int _jobLogReadWarningLogged;

    public RescrapeJobService(
        SupabaseTableService<RescrapeJob> jobsTable,
        SupabaseTableService<RescrapeJobItem> jobItemsTable,
        SupabaseTableService<RescrapeJobLog> jobLogsTable,
        SupabaseTableService<SiteProfile> siteTable,
        SupabaseTableService<StagingProduct> stagingTable,
        IScrapingService scrapingService,
        ScrapingRunner scrapingRunner,
        IPdfAttachmentAnalyzer pdfAttachmentAnalyzer,
        ILogger<RescrapeJobService> logger)
    {
        _jobsTable = jobsTable;
        _jobItemsTable = jobItemsTable;
        _jobLogsTable = jobLogsTable;
        _siteTable = siteTable;
        _stagingTable = stagingTable;
        _scrapingService = scrapingService;
        _scrapingRunner = scrapingRunner;
        _pdfAttachmentAnalyzer = pdfAttachmentAnalyzer;
        _logger = logger;
    }

    public async Task<RescrapeJobResponse> EnqueueAsync(RescrapeRequest request, CancellationToken cancellationToken = default)
    {
        var productIds = request.ProductIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (productIds.Count == 0)
        {
            throw new InvalidOperationException("No se recibieron IDs de producto validos para rescrape.");
        }

        var now = DateTime.UtcNow;
        var allProducts = await _stagingTable.GetAllAsync();
        var selectedProducts = allProducts.Where(p => productIds.Contains(p.Id)).ToList();
        if (selectedProducts.Count == 0)
        {
            throw new InvalidOperationException("No se encontraron productos de staging para los IDs solicitados.");
        }

        var jobOptions = new RescrapeJobOptions
        {
            ManualLogin = request.ManualLogin
        };

        var job = new RescrapeJob
        {
            Id = Guid.NewGuid(),
            Status = "queued",
            RequestedAt = now,
            TotalItems = selectedProducts.Count,
            ProcessedItems = 0,
            SuccessItems = 0,
            FailedItems = 0,
            SkippedItems = 0,
            OptionsJson = JsonSerializer.Serialize(jobOptions),
            CreatedAt = now,
            UpdatedAt = now
        };

        await _jobsTable.CreateAsync(job);

        foreach (var product in selectedProducts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = new RescrapeJobItem
            {
                Id = Guid.NewGuid(),
                JobId = job.Id,
                StagingProductId = product.Id,
                SiteId = product.SiteId,
                SourceUrl = product.SourceUrl,
                Status = "pending",
                Changed = false,
                ErrorMessage = "Pendiente en cola.",
                CreatedAt = now,
                UpdatedAt = now
            };
            await _jobItemsTable.CreateAsync(item);
        }

        await WriteJobLogAsync(job.Id, "info", $"Job encolado con {selectedProducts.Count} item(s).", details: new
        {
            manualLogin = jobOptions.ManualLogin
        });

        return new RescrapeJobResponse
        {
            JobId = job.Id,
            TotalItems = job.TotalItems,
            QueuedAt = job.RequestedAt
        };
    }

    public async Task<RescrapeJobStatusResponse?> GetStatusAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var job = await _jobsTable.GetByIdAsync(jobId);
        if (job == null)
        {
            return null;
        }

        return new RescrapeJobStatusResponse
        {
            JobId = job.Id,
            Status = job.Status,
            RequestedAt = job.RequestedAt,
            StartedAt = job.StartedAt,
            CompletedAt = job.CompletedAt,
            TotalItems = job.TotalItems,
            ProcessedItems = job.ProcessedItems,
            SuccessItems = job.SuccessItems,
            FailedItems = job.FailedItems,
            SkippedItems = job.SkippedItems,
            ErrorMessage = job.ErrorMessage
        };
    }

    public async Task<IReadOnlyList<RescrapeJobItemResponse>> GetItemsAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var items = await _jobItemsTable.GetAllAsync();
        return items
            .Where(i => i.JobId == jobId)
            .OrderBy(i => i.CreatedAt)
            .Select(i => new RescrapeJobItemResponse
            {
                ItemId = i.Id,
                JobId = i.JobId,
                StagingProductId = i.StagingProductId,
                SiteId = i.SiteId,
                SourceUrl = i.SourceUrl,
                Status = i.Status,
                Changed = i.Changed,
                ErrorMessage = i.ErrorMessage,
                ResultJson = i.ResultJson,
                UpdatedAt = i.UpdatedAt
            })
            .ToList();
    }

    public async Task<IReadOnlyList<RescrapeJobLogResponse>> GetLogsAsync(Guid jobId, int take = 200, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var logs = await _jobLogsTable.GetAllAsync();
            return logs
                .Where(l => l.JobId == jobId)
                .OrderBy(l => l.CreatedAt)
                .TakeLast(Math.Max(1, Math.Min(500, take)))
                .Select(l => new RescrapeJobLogResponse
                {
                    LogId = l.Id,
                    JobId = l.JobId,
                    ItemId = l.ItemId,
                    StagingProductId = l.StagingProductId,
                    Level = l.Level,
                    Message = l.Message,
                    DetailsJson = l.DetailsJson,
                    CreatedAt = l.CreatedAt
                })
                .ToList();
        }
        catch (Exception ex) when (IsMissingRescrapeJobLogsTable(ex))
        {
            if (Interlocked.Exchange(ref _jobLogReadWarningLogged, 1) == 0)
            {
                _logger.LogWarning(ex,
                    "Tabla rescrape_job_logs no disponible. Ejecuta migration_add_rescrape_jobs.sql y recarga cache de Supabase.");
            }

            _disableJobLogPersistence = true;
            return new List<RescrapeJobLogResponse>
            {
                new()
                {
                    LogId = jobId,
                    JobId = jobId,
                    Level = "warning",
                    Message = "No se pudo leer rescrape_job_logs. Ejecuta migration_add_rescrape_jobs.sql y recarga cache de Supabase.",
                    CreatedAt = DateTime.UtcNow
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudieron leer logs del job {JobId}.", jobId);
            return Array.Empty<RescrapeJobLogResponse>();
        }
    }

    public async Task<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var job = await _jobsTable.GetByIdAsync(jobId);
        if (job == null)
        {
            return false;
        }

        if (string.Equals(job.Status, "completed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(job.Status, "completed_with_errors", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(job.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        job.Status = "cancelled";
        job.ErrorMessage = "Cancelado por usuario.";
        job.CompletedAt = DateTime.UtcNow;
        job.UpdatedAt = DateTime.UtcNow;
        await _jobsTable.UpdateAsync(job.Id, job);
        await WriteJobLogAsync(job.Id, "warning", "Job cancelado por usuario.");
        return true;
    }

    public async Task<bool> PauseAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var job = await _jobsTable.GetByIdAsync(jobId);
        if (job == null)
        {
            return false;
        }

        if (string.Equals(job.Status, "completed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(job.Status, "completed_with_errors", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(job.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(job.Status, "paused", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        job.Status = "paused";
        job.ErrorMessage = "Pausado por usuario.";
        job.UpdatedAt = DateTime.UtcNow;
        await _jobsTable.UpdateAsync(job.Id, job);
        await WriteJobLogAsync(job.Id, "warning", "Job pausado por usuario.");
        return true;
    }

    public async Task<bool> ResumeAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var job = await _jobsTable.GetByIdAsync(jobId);
        if (job == null)
        {
            return false;
        }

        if (string.Equals(job.Status, "completed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(job.Status, "completed_with_errors", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(job.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.Equals(job.Status, "paused", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        job.Status = "queued";
        job.ErrorMessage = null;
        job.UpdatedAt = DateTime.UtcNow;
        await _jobsTable.UpdateAsync(job.Id, job);
        await WriteJobLogAsync(job.Id, "info", "Job reanudado por usuario.");
        return true;
    }

    public async Task ProcessNextQueuedJobAsync(CancellationToken cancellationToken = default)
    {
        if (!await _singleProcessor.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            var jobs = await _jobsTable.GetAllAsync();

            var nextQueued = jobs
                .Where(j => string.Equals(j.Status, "queued", StringComparison.OrdinalIgnoreCase))
                .OrderBy(j => j.RequestedAt)
                .FirstOrDefault();

            var nextRunning = jobs
                .Where(j => string.Equals(j.Status, "running", StringComparison.OrdinalIgnoreCase))
                .OrderBy(j => j.RequestedAt)
                .FirstOrDefault();

            var nextJob = nextQueued ?? nextRunning;
            if (nextJob == null)
            {
                return;
            }

            await ProcessJobAsync(nextJob, cancellationToken);
        }
        finally
        {
            _singleProcessor.Release();
        }
    }

    private async Task ProcessJobAsync(RescrapeJob job, CancellationToken cancellationToken)
    {
        var options = ParseOptions(job.OptionsJson);
        var previousHeadless = Environment.GetEnvironmentVariable("SCRAPSAE_HEADLESS");
        var previousManual = Environment.GetEnvironmentVariable("SCRAPSAE_FORCE_MANUAL_LOGIN");
        var previousManualFlag = Environment.GetEnvironmentVariable("SCRAPSAE_MANUAL_LOGIN");

        try
        {
            if (!string.Equals(job.Status, "running", StringComparison.OrdinalIgnoreCase))
            {
                job.Status = "running";
                job.StartedAt = DateTime.UtcNow;
                job.ErrorMessage = null;
                job.UpdatedAt = DateTime.UtcNow;
                await _jobsTable.UpdateAsync(job.Id, job);
            }

            Environment.SetEnvironmentVariable("SCRAPSAE_FORCE_MANUAL_LOGIN", options.ManualLogin ? "true" : "false");
            Environment.SetEnvironmentVariable("SCRAPSAE_MANUAL_LOGIN", options.ManualLogin ? "true" : "false");
            Environment.SetEnvironmentVariable("SCRAPSAE_HEADLESS", options.ManualLogin ? "false" : "true");

            await WriteJobLogAsync(job.Id, "info", "Procesamiento de job iniciado.", details: new
            {
                manualLogin = options.ManualLogin
            });

            var allItems = (await _jobItemsTable.GetAllAsync())
                .Where(i => i.JobId == job.Id)
                .OrderBy(i => i.CreatedAt)
                .ToList();

            var pendingItems = allItems
                .Where(i => !IsTerminal(i.Status))
                .ToList();

            if (pendingItems.Count == 0)
            {
                await RefreshJobCountersAsync(job.Id, cancellationToken);
                await FinalizeJobAsync(job.Id, cancellationToken);
                return;
            }

            if (options.ManualLogin)
            {
                await WarmupManualLoginAsync(job.Id, pendingItems, cancellationToken);
            }

            foreach (var siteGroup in pendingItems.GroupBy(i => i.SiteId).OrderBy(g => g.Key))
            {
                foreach (var item in siteGroup)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var currentJob = await _jobsTable.GetByIdAsync(job.Id);
                    if (currentJob == null ||
                        string.Equals(currentJob.Status, "cancelled", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(currentJob.Status, "paused", StringComparison.OrdinalIgnoreCase))
                    {
                        var reason = currentJob == null
                            ? "job inexistente"
                            : string.Equals(currentJob.Status, "paused", StringComparison.OrdinalIgnoreCase)
                                ? "job pausado"
                                : "job cancelado";
                        await WriteJobLogAsync(job.Id, "warning", $"Procesamiento detenido: {reason}.");
                        break;
                    }

                    await ProcessItemAsync(job.Id, item, cancellationToken);
                    await RefreshJobCountersAsync(job.Id, cancellationToken);
                }
            }

            await FinalizeJobAsync(job.Id, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ejecutando job de rescrape {JobId}", job.Id);
            await WriteJobLogAsync(job.Id, "error", $"Error fatal de job: {ex.Message}");

            var failedJob = await _jobsTable.GetByIdAsync(job.Id);
            if (failedJob != null)
            {
                failedJob.Status = "completed_with_errors";
                failedJob.ErrorMessage = ex.Message;
                failedJob.CompletedAt = DateTime.UtcNow;
                failedJob.UpdatedAt = DateTime.UtcNow;
                await _jobsTable.UpdateAsync(failedJob.Id, failedJob);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("SCRAPSAE_HEADLESS", previousHeadless);
            Environment.SetEnvironmentVariable("SCRAPSAE_FORCE_MANUAL_LOGIN", previousManual);
            Environment.SetEnvironmentVariable("SCRAPSAE_MANUAL_LOGIN", previousManualFlag);
            Environment.SetEnvironmentVariable("SCRAPSAE_MANUAL_BROWSER_READY", null);
        }
    }

    private async Task WarmupManualLoginAsync(Guid jobId, IReadOnlyList<RescrapeJobItem> pendingItems, CancellationToken cancellationToken)
    {
        foreach (var firstSiteItem in pendingItems
                     .Where(i => !string.IsNullOrWhiteSpace(i.SourceUrl))
                     .GroupBy(i => i.SiteId)
                     .Select(g => g.First()))
        {
            await WriteJobLogAsync(jobId, "info",
                $"Login manual habilitado. Abre el navegador y confirma sesión para sitio {firstSiteItem.SiteId}.",
                firstSiteItem.Id,
                firstSiteItem.StagingProductId);

            try
            {
                // Warmup por sitio para forzar sesión activa antes del lote.
                await _scrapingService.ScrapeDirectUrlsAsync(
                    new List<string> { firstSiteItem.SourceUrl! },
                    firstSiteItem.SiteId,
                    new DirectUrlScrapeOptions
                    {
                        InspectOnly = true,
                        SingleProductOnly = true,
                        ExpandRelated = false
                    },
                    cancellationToken);

                await WriteJobLogAsync(jobId, "success",
                    "Warmup de sesión manual completado.",
                    firstSiteItem.Id,
                    firstSiteItem.StagingProductId,
                    new { siteId = firstSiteItem.SiteId, sourceUrl = firstSiteItem.SourceUrl });
            }
            catch (Exception ex)
            {
                await WriteJobLogAsync(jobId, "warning",
                    $"Warmup de sesión manual falló: {ex.Message}",
                    firstSiteItem.Id,
                    firstSiteItem.StagingProductId);
            }
        }
    }

    private async Task ProcessItemAsync(Guid jobId, RescrapeJobItem item, CancellationToken cancellationToken)
    {
        if (IsTerminal(item.Status))
        {
            return;
        }

        var startedAt = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        item.Status = "running";
        item.ErrorMessage = "Iniciando extracción desde URL origen...";
        item.UpdatedAt = DateTime.UtcNow;
        await _jobItemsTable.UpdateAsync(item.Id, item);
        await WriteJobLogAsync(jobId, "info", "Item iniciado.", item.Id, item.StagingProductId, new
        {
            item.SiteId,
            item.SourceUrl
        });

        if (string.IsNullOrWhiteSpace(item.SourceUrl))
        {
            await SetItemAsSkippedAsync(jobId, item, "Producto sin source_url.");
            return;
        }

        var stagingProduct = await _stagingTable.GetByIdAsync(item.StagingProductId);
        if (stagingProduct == null)
        {
            await SetItemAsSkippedAsync(jobId, item, "Producto de staging no encontrado.");
            return;
        }

        var site = await _siteTable.GetByIdAsync(item.SiteId);
        if (site == null)
        {
            await SetItemAsFailedAsync(jobId, item, $"No se encontro config_sites para site_id={item.SiteId}.", new
            {
                item.SiteId,
                item.SourceUrl
            });
            return;
        }

        _scrapingService.RegisterSite(site);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(ItemTimeoutMinutes));

        try
        {
            var scraped = await _scrapingService.ScrapeDirectUrlsAsync(
                new List<string> { item.SourceUrl },
                item.SiteId,
                new DirectUrlScrapeOptions
                {
                    InspectOnly = false,
                    SingleProductOnly = true,
                    ExpandRelated = false
                },
                timeoutCts.Token);

            var scrapedProduct = scraped.FirstOrDefault();
            if (scrapedProduct == null)
            {
                await SetItemAsFailedAsync(jobId, item, $"No se pudo extraer producto desde la URL origen: {item.SourceUrl}", new
                {
                    item.SourceUrl,
                    item.SiteId
                });
                return;
            }

            item.ErrorMessage = "Extracción completada. Ejecutando IA y merge conservador...";
            item.UpdatedAt = DateTime.UtcNow;
            await _jobItemsTable.UpdateAsync(item.Id, item);

            var incomingAiJson = await _scrapingRunner.BuildAiJsonFromScrapedAsync(scrapedProduct, timeoutCts.Token) ?? "{}";
            var pdfSpecs = await _pdfAttachmentAnalyzer.ExtractSpecificationsAsync(scrapedProduct.Attachments, timeoutCts.Token);
            var mergedAiJson = MergeConservative(stagingProduct.AIProcessedJson, incomingAiJson, pdfSpecs);

            var newRawData = JsonSerializer.Serialize(scrapedProduct);
            var newSourceUrl = string.IsNullOrWhiteSpace(stagingProduct.SourceUrl)
                ? scrapedProduct.SourceUrl ?? item.SourceUrl
                : stagingProduct.SourceUrl;

            var changed = !JsonEquivalent(stagingProduct.AIProcessedJson, mergedAiJson) ||
                          !string.Equals(stagingProduct.RawData, newRawData, StringComparison.Ordinal) ||
                          !string.Equals(stagingProduct.SourceUrl, newSourceUrl, StringComparison.Ordinal);

            if (changed)
            {
                stagingProduct.AIProcessedJson = mergedAiJson;
                stagingProduct.RawData = newRawData;
                stagingProduct.SourceUrl = newSourceUrl;
                stagingProduct.UpdatedAt = DateTime.UtcNow;

                if (string.Equals(stagingProduct.FlashlySyncStatus, "synced", StringComparison.OrdinalIgnoreCase))
                {
                    stagingProduct.FlashlySyncStatus = "pending";
                    stagingProduct.FlashlySyncedAt = null;
                }

                await _stagingTable.UpdateAsync(stagingProduct.Id, stagingProduct);
            }

            stopwatch.Stop();
            item.Status = "succeeded";
            item.Changed = changed;
            item.ErrorMessage = changed
                ? "Procesado correctamente. Se detectaron cambios."
                : "Procesado correctamente. Sin cambios detectados.";
            item.ResultJson = JsonSerializer.Serialize(new
            {
                sku = scrapedProduct.SkuSource,
                title = scrapedProduct.Title,
                changed,
                durationMs = stopwatch.ElapsedMilliseconds,
                sourceUrl = item.SourceUrl,
                images = scrapedProduct.ImageUrls.Count,
                attachments = scrapedProduct.Attachments.Count,
                pdfSpecs = pdfSpecs.Count
            });
            item.UpdatedAt = DateTime.UtcNow;
            await _jobItemsTable.UpdateAsync(item.Id, item);

            await WriteJobLogAsync(jobId, "success", item.ErrorMessage, item.Id, item.StagingProductId, new
            {
                item.SourceUrl,
                durationMs = stopwatch.ElapsedMilliseconds,
                changed
            });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await SetItemAsFailedAsync(jobId, item,
                $"Timeout procesando item (> {ItemTimeoutMinutes} min) para URL: {item.SourceUrl}",
                new { item.SourceUrl, startedAt });
        }
        catch (Exception ex)
        {
            await SetItemAsFailedAsync(jobId, item, ex.Message, new { item.SourceUrl });
        }
    }

    private async Task SetItemAsSkippedAsync(Guid jobId, RescrapeJobItem item, string reason)
    {
        item.Status = "skipped";
        item.ErrorMessage = reason;
        item.UpdatedAt = DateTime.UtcNow;
        await _jobItemsTable.UpdateAsync(item.Id, item);
        await WriteJobLogAsync(jobId, "warning", reason, item.Id, item.StagingProductId, new { item.SourceUrl });
    }

    private async Task SetItemAsFailedAsync(Guid jobId, RescrapeJobItem item, string reason, object? details = null)
    {
        item.Status = "failed";
        item.ErrorMessage = reason;
        item.UpdatedAt = DateTime.UtcNow;
        await _jobItemsTable.UpdateAsync(item.Id, item);
        await WriteJobLogAsync(jobId, "error", reason, item.Id, item.StagingProductId, details);
    }

    private async Task RefreshJobCountersAsync(Guid jobId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var job = await _jobsTable.GetByIdAsync(jobId);
        if (job == null)
        {
            return;
        }

        var items = (await _jobItemsTable.GetAllAsync()).Where(i => i.JobId == jobId).ToList();
        job.TotalItems = items.Count;
        job.SuccessItems = items.Count(i => string.Equals(i.Status, "succeeded", StringComparison.OrdinalIgnoreCase));
        job.FailedItems = items.Count(i => string.Equals(i.Status, "failed", StringComparison.OrdinalIgnoreCase));
        job.SkippedItems = items.Count(i => string.Equals(i.Status, "skipped", StringComparison.OrdinalIgnoreCase));
        job.ProcessedItems = items.Count(i => IsTerminal(i.Status));
        job.UpdatedAt = DateTime.UtcNow;
        await _jobsTable.UpdateAsync(job.Id, job);
    }

    private async Task FinalizeJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var job = await _jobsTable.GetByIdAsync(jobId);
        if (job == null)
        {
            return;
        }

        if (string.Equals(job.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
        {
            job.CompletedAt = DateTime.UtcNow;
            job.UpdatedAt = DateTime.UtcNow;
            await _jobsTable.UpdateAsync(job.Id, job);
            await WriteJobLogAsync(job.Id, "warning", "Job finalizado como cancelado.");
            return;
        }

        if (string.Equals(job.Status, "paused", StringComparison.OrdinalIgnoreCase))
        {
            job.UpdatedAt = DateTime.UtcNow;
            await _jobsTable.UpdateAsync(job.Id, job);
            await WriteJobLogAsync(job.Id, "info", "Job quedó en pausa.");
            return;
        }

        var items = (await _jobItemsTable.GetAllAsync()).Where(i => i.JobId == jobId).ToList();
        var unresolved = items.Count(i => !IsTerminal(i.Status));
        if (unresolved > 0)
        {
            job.Status = "running";
            job.ErrorMessage = $"Aun hay {unresolved} item(s) en estado no terminal.";
            job.UpdatedAt = DateTime.UtcNow;
            await _jobsTable.UpdateAsync(job.Id, job);
            return;
        }

        job.Status = job.FailedItems > 0 ? "completed_with_errors" : "completed";
        job.CompletedAt = DateTime.UtcNow;
        job.ErrorMessage = null;
        job.SummaryJson = JsonSerializer.Serialize(new
        {
            job.TotalItems,
            job.ProcessedItems,
            job.SuccessItems,
            job.FailedItems,
            job.SkippedItems
        });
        job.UpdatedAt = DateTime.UtcNow;
        await _jobsTable.UpdateAsync(job.Id, job);
        await WriteJobLogAsync(job.Id, "success",
            $"Job finalizado con estado {job.Status}. Exitosos={job.SuccessItems}, Fallidos={job.FailedItems}, Omitidos={job.SkippedItems}.");
    }

    private async Task WriteJobLogAsync(
        Guid jobId,
        string level,
        string message,
        Guid? itemId = null,
        Guid? stagingProductId = null,
        object? details = null)
    {
        if (!_disableJobLogPersistence)
        {
            try
            {
                await _jobLogsTable.CreateAsync(new RescrapeJobLog
                {
                    Id = Guid.NewGuid(),
                    JobId = jobId,
                    ItemId = itemId,
                    StagingProductId = stagingProductId,
                    Level = level,
                    Message = message,
                    DetailsJson = details != null ? JsonSerializer.Serialize(details) : null,
                    CreatedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex) when (IsMissingRescrapeJobLogsTable(ex))
            {
                _disableJobLogPersistence = true;
                if (Interlocked.Exchange(ref _jobLogPersistenceWarningLogged, 1) == 0)
                {
                    _logger.LogWarning(ex,
                        "No se pudo persistir log de rescrape por tabla ausente (rescrape_job_logs). Se continuara sin persistencia de logs.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo persistir log de job {JobId}. Mensaje={Message}", jobId, message);
            }
        }

        _logger.LogInformation("RescrapeJob {JobId} [{Level}] {Message}", jobId, level, message);
    }

    private static bool IsMissingRescrapeJobLogsTable(Exception ex)
    {
        var msg = ex.Message;
        if (string.IsNullOrWhiteSpace(msg))
        {
            return false;
        }

        return msg.Contains("rescrape_job_logs", StringComparison.OrdinalIgnoreCase) &&
               (msg.Contains("PGRST205", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("Could not find the table", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("NotFound", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTerminal(string? status)
    {
        return !string.IsNullOrWhiteSpace(status) &&
               TerminalItemStatuses.Contains(status, StringComparer.OrdinalIgnoreCase);
    }

    private static RescrapeJobOptions ParseOptions(string? optionsJson)
    {
        if (string.IsNullOrWhiteSpace(optionsJson))
        {
            return new RescrapeJobOptions();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<RescrapeJobOptions>(
                optionsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return parsed ?? new RescrapeJobOptions();
        }
        catch
        {
            return new RescrapeJobOptions();
        }
    }

    private sealed class RescrapeJobOptions
    {
        public bool ManualLogin { get; set; }
    }

    private static string MergeConservative(string? existingJson, string? incomingJson, IReadOnlyDictionary<string, string> pdfSpecs)
    {
        var baseNode = ParseJsonObject(existingJson);
        var incomingNode = ParseJsonObject(incomingJson);
        MergeObject(baseNode, incomingNode);

        if (pdfSpecs.Count > 0)
        {
            if (baseNode["specifications"] is not JsonObject specsObj)
            {
                specsObj = new JsonObject();
                baseNode["specifications"] = specsObj;
            }

            foreach (var (key, value) in pdfSpecs)
            {
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (!specsObj.ContainsKey(key) || IsNullOrEmpty(specsObj[key]))
                {
                    specsObj[key] = value;
                }
            }
        }

        return baseNode.ToJsonString();
    }

    private static JsonObject ParseJsonObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new JsonObject();
        }

        try
        {
            var parsed = JsonNode.Parse(json);
            return parsed as JsonObject ?? new JsonObject();
        }
        catch
        {
            return new JsonObject();
        }
    }

    private static void MergeObject(JsonObject target, JsonObject incoming)
    {
        foreach (var incomingProperty in incoming)
        {
            if (!target.ContainsKey(incomingProperty.Key))
            {
                target[incomingProperty.Key] = incomingProperty.Value?.DeepClone();
                continue;
            }

            var existingValue = target[incomingProperty.Key];
            var incomingValue = incomingProperty.Value;
            if (IsNullOrEmpty(existingValue))
            {
                target[incomingProperty.Key] = incomingValue?.DeepClone();
                continue;
            }

            if (existingValue is JsonObject existingObj && incomingValue is JsonObject incomingObj)
            {
                MergeObject(existingObj, incomingObj);
                continue;
            }

            if (existingValue is JsonArray existingArray && incomingValue is JsonArray incomingArray)
            {
                MergeArray(existingArray, incomingArray);
            }
        }
    }

    private static void MergeArray(JsonArray target, JsonArray incoming)
    {
        var seen = new HashSet<string>(target.Select(GetArrayItemKey), StringComparer.OrdinalIgnoreCase);
        foreach (var incomingValue in incoming)
        {
            var key = GetArrayItemKey(incomingValue);
            if (!seen.Contains(key))
            {
                target.Add(incomingValue?.DeepClone());
                seen.Add(key);
            }
        }
    }

    private static string GetArrayItemKey(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            if (obj["fileUrl"] is JsonValue v1 && v1.TryGetValue<string>(out var fileUrl) && !string.IsNullOrWhiteSpace(fileUrl))
            {
                return $"fileurl:{fileUrl}";
            }

            if (obj["url"] is JsonValue v2 && v2.TryGetValue<string>(out var url) && !string.IsNullOrWhiteSpace(url))
            {
                return $"url:{url}";
            }
        }

        return node?.ToJsonString() ?? string.Empty;
    }

    private static bool IsNullOrEmpty(JsonNode? node)
    {
        if (node == null)
        {
            return true;
        }

        if (node is JsonValue value && value.TryGetValue<string>(out var stringValue))
        {
            return string.IsNullOrWhiteSpace(stringValue);
        }

        if (node is JsonArray array)
        {
            return array.Count == 0;
        }

        if (node is JsonObject obj)
        {
            return obj.Count == 0;
        }

        return false;
    }

    private static bool JsonEquivalent(string? left, string? right)
    {
        var leftNode = ParseJsonObject(left);
        var rightNode = ParseJsonObject(right);
        return string.Equals(leftNode.ToJsonString(), rightNode.ToJsonString(), StringComparison.Ordinal);
    }
}
