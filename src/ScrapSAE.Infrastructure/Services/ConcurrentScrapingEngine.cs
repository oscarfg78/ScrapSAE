using System.Diagnostics;
using System.Reactive.Subjects;
using System.Threading.Channels;
using Microsoft.Playwright;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Interfaces;

namespace ScrapSAE.Infrastructure.Services;

/// <summary>
/// Engine principal de scraping concurrente para el wizard Excel-driven.
///
/// Arquitectura del pipeline:
///   [ExcelStreamReader] → Channel{ExcelProductRecord} → [N Workers]
///                                                           ↓
///                                                     Task.WhenAll(Target1, Target2?)
///                                                           ↓
///                                                     ProductDataConsolidator
///                                                           ↓
///                                                     IProgress{ScrapingProgressEvent}
///                                                           ↓
///                                                     WizardSessionRepository.SaveTickAsync
///
/// Concurrencia:
///   - Channel bounded (capacity = workerCount × 2) para back-pressure en la lectura del Excel.
///   - SemaphoreSlim limita páginas Playwright abiertas simultáneamente (default: 8).
///   - Cada worker chequea la pause gate y el cancellation token al inicio de cada item.
///
/// IMPORTANTE: Este engine NO depende de ISelectorDiscoveryService.
/// La IA solo se usa en la configuración (Steps 1-3), nunca en el batch.
/// </summary>
public sealed class ConcurrentScrapingEngine : IConcurrentScrapingEngine
{
    private readonly IExcelIngestionService _excelService;
    private readonly ProductDataConsolidator _consolidator;
    private readonly IWizardSessionRepository _sessionRepository;

    // Progress stream
    private readonly Subject<ScrapingProgressEvent> _progressSubject = new();
    public IObservable<ScrapingProgressEvent> Progress => _progressSubject;

    // Pause/Stop controls
    private readonly ManualResetEventSlim _pauseGate = new(initialState: true);
    private CancellationTokenSource _cts = new();

    // Tick buffer para SaveTickAsync (cada 10 filas)
    private const int SaveTickInterval = 10;

    // Playwright browser (lazy init)
    private IPlaywright? _playwright;
    private IBrowser?    _browser;

    public ConcurrentScrapingEngine(
        IExcelIngestionService excelService,
        ProductDataConsolidator consolidator,
        IWizardSessionRepository sessionRepository)
    {
        _excelService      = excelService;
        _consolidator      = consolidator;
        _sessionRepository = sessionRepository;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Control de ejecución
    // ─────────────────────────────────────────────────────────────────────────

    public void Pause()
    {
        _pauseGate.Reset();
        _progressSubject.OnNext(new ScrapingProgressEvent { EventType = ProgressEventType.ExecutionPaused });
    }

    public void Resume()
    {
        _pauseGate.Set();
        _progressSubject.OnNext(new ScrapingProgressEvent { EventType = ProgressEventType.ExecutionResumed });
    }

    public async Task StopAsync()
    {
        _pauseGate.Set(); // Desbloquear para que los workers puedan recibir el cancel
        await _cts.CancelAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Ejecución principal
    // ─────────────────────────────────────────────────────────────────────────

    public async Task StartAsync(ConcurrentWizardSession session, CancellationToken cancellationToken = default)
    {
        // Reemplazar CTS para soportar reinicio
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pauseGate.Set();

        var token = _cts.Token;
        var sw = Stopwatch.StartNew();

        // Contadores thread-safe
        int processed = 0, success = 0, skipped = 0;
        var totalRows = session.TotalExcelRows - (session.LastCompletedRowIndex + 1);

        // Buffer de resultados para SaveTick
        var tickBuffer = new List<ConsolidatedProductResult>();
        var tickLock   = new SemaphoreSlim(1, 1);

        // Semáforo para limitar páginas Playwright concurrentes
        var pageSemaphore = new SemaphoreSlim(session.MaxConcurrentPages, session.MaxConcurrentPages);

        // Channel bounded para back-pressure
        var channel = Channel.CreateBounded<ExcelProductRecord>(
            new BoundedChannelOptions(session.WorkerCount * 2)
            {
                FullMode  = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = true
            });

        // Inicializar Playwright y browser
        _playwright = await Playwright.CreateAsync();
        _browser    = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = false // No headless para evitar detección anti-bot
        });

        // ── Producer: lee Excel y escribe al channel ──────────────────────
        var producer = Task.Run(async () =>
        {
            try
            {
                var startRow = session.LastCompletedRowIndex + 1;
                await foreach (var row in _excelService.StreamRowsAsync(
                    session.ExcelFilePath, session.ColumnMapping, startRow, token))
                {
                    await channel.Writer.WriteAsync(row, token);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                channel.Writer.Complete();
            }
        }, token);

        // ── Consumers: N workers ──────────────────────────────────────────
        var workers = Enumerable.Range(0, session.WorkerCount).Select(workerIndex => Task.Run(async () =>
        {
            await foreach (var row in channel.Reader.ReadAllAsync(token))
            {
                // Pause gate: bloquea sin consumir CPU hasta Resume
                _pauseGate.Wait(token);
                token.ThrowIfCancellationRequested();

                _progressSubject.OnNext(new ScrapingProgressEvent
                {
                    EventType = ProgressEventType.RowStarted,
                    RowIndex  = row.RowIndex,
                    Sku       = row.Sku,
                    ElapsedMs = sw.ElapsedMilliseconds
                });

                // Ejecutar scraping dual con semáforo para limitar páginas
                ConsolidatedProductResult consolidated;
                try
                {
                    consolidated = await ScrapeRowAsync(
                        row, session, pageSemaphore, token);

                    var cnt = Interlocked.Increment(ref processed);
                    if (consolidated.Status == ConsolidatedStatus.Matched)
                        Interlocked.Increment(ref success);
                    else
                        Interlocked.Increment(ref skipped);

                    // Actualizar LastCompletedRowIndex en sesión
                    session.LastCompletedRowIndex = row.RowIndex;

                    // Tick save cada SaveTickInterval filas
                    await tickLock.WaitAsync(token);
                    try
                    {
                        tickBuffer.Add(consolidated);
                        if (tickBuffer.Count >= SaveTickInterval)
                        {
                            var snapshot = tickBuffer.ToList();
                            tickBuffer.Clear();
                            _ = Task.Run(async () => await _sessionRepository.SaveTickAsync(session, snapshot));
                        }
                    }
                    finally { tickLock.Release(); }

                    _progressSubject.OnNext(new ScrapingProgressEvent
                    {
                        EventType      = ProgressEventType.RowCompleted,
                        RowIndex       = row.RowIndex,
                        Sku            = row.Sku,
                        Result         = consolidated,
                        ProcessedCount = cnt,
                        SuccessCount   = success,
                        SkippedCount   = skipped,
                        TotalRows      = totalRows,
                        ElapsedMs      = sw.ElapsedMilliseconds
                    });
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref processed);
                    Interlocked.Increment(ref skipped);
                    _progressSubject.OnNext(new ScrapingProgressEvent
                    {
                        EventType = ProgressEventType.RowSkipped,
                        RowIndex  = row.RowIndex,
                        Sku       = row.Sku,
                        Message   = ex.Message,
                        ElapsedMs = sw.ElapsedMilliseconds
                    });
                }

                // Delay anti-bot por dominio
                if (session.Target1.RequestDelayMs > 0)
                    await Task.Delay(session.Target1.RequestDelayMs, token);
            }
        }, token)).ToList();

        // ── Esperar finalización ──────────────────────────────────────────
        try
        {
            await producer;
            await Task.WhenAll(workers);
        }
        catch (OperationCanceledException) { }

        // Guardar buffer residual
        if (tickBuffer.Count > 0)
            await _sessionRepository.SaveTickAsync(session, tickBuffer, CancellationToken.None);

        var eventType = _cts.IsCancellationRequested
            ? ProgressEventType.ExecutionStopped
            : ProgressEventType.ExecutionFinished;

        _progressSubject.OnNext(new ScrapingProgressEvent
        {
            EventType      = eventType,
            ProcessedCount = processed,
            SuccessCount   = success,
            SkippedCount   = skipped,
            TotalRows      = totalRows,
            ElapsedMs      = sw.ElapsedMilliseconds
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Scrape de una fila: ejecuta Target1 y Target2 en paralelo
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<ConsolidatedProductResult> ScrapeRowAsync(
        ExcelProductRecord row,
        ConcurrentWizardSession session,
        SemaphoreSlim pageSemaphore,
        CancellationToken token)
    {
        var engine = new DualTargetSearchEngine();

        Task<TargetScrapeResult> t1Task = ScrapeTargetAsync(engine, session.Target1, row.Sku, pageSemaphore, token);
        Task<TargetScrapeResult>? t2Task = session.HasTarget2
            ? ScrapeTargetAsync(engine, session.Target2!, row.Sku, pageSemaphore, token)
            : null;

        var r1 = await t1Task;
        var r2 = t2Task != null ? await t2Task : null;

        return _consolidator.Consolidate(row, r1, r2, session.SourcePriority, session.Target1);
    }

    private async Task<TargetScrapeResult> ScrapeTargetAsync(
        DualTargetSearchEngine engine,
        TargetSearchConfig config,
        string sku,
        SemaphoreSlim pageSemaphore,
        CancellationToken token)
    {
        await pageSemaphore.WaitAsync(token);
        IPage? page = null;
        try
        {
            var context = await _browser!.NewContextAsync();
            page = await context.NewPageAsync();
            return await engine.SearchAndExtractAsync(page, config, sku, token);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return TargetScrapeResult.NotFound(config.Label, sku, SkipReason.UnexpectedException, ex.Message);
        }
        finally
        {
            if (page != null) try { await page.CloseAsync(); } catch { }
            pageSemaphore.Release();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Dispose
    // ─────────────────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        _progressSubject.OnCompleted();
        _pauseGate.Dispose();
        _cts.Dispose();
        if (_browser != null) await _browser.CloseAsync();
        _playwright?.Dispose();
    }
}
