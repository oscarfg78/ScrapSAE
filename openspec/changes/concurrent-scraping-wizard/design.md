## Context

ScrapSAE's current `ProviderWizardViewModel` is designed for single-source catalog extraction tied to existing `SiteProfile` entities persisted via `ApiClient`. It uses a linear 5-step flow: URL input → AI analysis → config review → test scrape → save. It does not support bulk input from external files, multi-source concurrency, asymmetric source prioritization, or resumable sessions.

This design creates a fully parallel, fault-tolerant wizard pipeline as a new, isolated module within `ScrapSAE.Desktop`, with new services in `ScrapSAE.Core` and `ScrapSAE.Infrastructure`. The existing wizard is preserved unchanged. The new wizard follows the same MVVM patterns (`ViewModelBase`, `AsyncCommand`, `ObservableCollection`) as the existing codebase.

## Goals / Non-Goals

**Goals:**
- New WPF wizard module with step-based view routing, decoupled from existing `ProviderWizardViewModel`.
- Excel file ingestion via streaming (`ExcelDataReader`) with column mapping enforcement.
- Flexible dual-source search configuration: DOM input typing and URL query parameter modes.
- AI-only-in-setup selector discovery via `IAnalysisService`, strictly forbidden during batch execution.
- Concurrent Channel-based scraping pipeline with configurable worker concurrency and Playwright page semaphore.
- Silent fault tolerance at every granular failure point, using `IScrapingLogsRepository` for structured skip logging.
- Asymmetric data source priority: independent selection of price source and image source.
- Data consolidation: every Excel row produces a `ConsolidatedProductResult` (matched or `NotMatched`).
- Progress stream via `IObservable<ScrapingProgressEvent>` bound to UI with dispatcher marshaling.
- Pause/resume gate via `ManualResetEventSlim` + stop via `CancellationToken`.
- Incremental session persistence: JSON file auto-saved after every 10 rows + step transitions.
- Resume: row-seek to last completed index, restore UI state, re-populate live results grid.

**Non-Goals:**
- Modifying `ProviderWizardViewModel`, `MainWindow.xaml` navigation, or any existing `SiteProfile` / `ApiClient` behavior.
- More than 2 concurrent target search sources in v1.
- AI-driven field enrichment or post-processing (this wizard is extraction-only).
- Cloud sync of session state.

## Decisions

---

### 1. Project Layer Distribution

All new types are added to the following projects without modifying existing files unless adding a DI registration:

| Layer | New Types |
|---|---|
| `ScrapSAE.Core` | `ExcelProductRecord`, `TargetSearchConfig`, `SelectorConfig`, `TargetScrapeResult`, `ScrapingResultStatus`, `ConsolidatedProductResult`, `ConcurrentWizardSession`, `SourcePriorityConfig`, `ScrapingProgressEvent`, `ISelectorDiscoveryService`, `IExcelIngestionService`, `IConcurrentScrapingEngine`, `IWizardSessionRepository` |
| `ScrapSAE.Infrastructure` | `ExcelIngestionService` (uses `ExcelDataReader`), `AiSelectorDiscoveryService` (adapts `IAnalysisService`), `ConcurrentScrapingEngine`, `ProductDataConsolidator`, `WizardSessionRepository` |
| `ScrapSAE.Desktop` | `ConcurrentProviderWizardViewModel`, step sub-ViewModels, XAML Views, `ConcurrentWizardNavigator` |

**Rationale**: Keeps Core clean of Playwright/WPF dependencies. Infrastructure owns heavy I/O. Desktop owns MVVM bindings.

---

### 2. Excel Ingestion: Streaming with ExcelDataReader

Use `ExcelDataReader` + `DataSet` batch reading (not `ClosedXML`) because the existing project avoids heavy XML-based parsers for performance. Streaming approach:

```csharp
// IExcelIngestionService
Task<ExcelPreviewResult> PreviewAsync(string filePath, int maxPreviewRows = 10);
IAsyncEnumerable<ExcelProductRecord> StreamRowsAsync(string filePath, ExcelColumnMapping mapping, int startRowIndex = 0);
```

`ExcelPreviewResult` contains: `string[] ColumnHeaders`, `List<string[]> PreviewRows`. Column mapping is saved to `ConcurrentWizardSession` before streaming begins. `StreamRowsAsync` accepts `startRowIndex` to support mid-stream resume.

**Why not ClosedXML?** ClosedXML loads the entire workbook into memory. `ExcelDataReader` streams via `IDataReader`, critical for files with 5,000–50,000 rows.

---

### 3. Channel-Based Producer-Consumer Pipeline

```
[ExcelStreamReader] ──► Channel<ExcelProductRecord> ──► [N Worker Tasks]
                                                             │
                                                    Task.WhenAll(
                                                      SearchAndExtract(target1, sku),
                                                      SearchAndExtract(target2, sku)  ← optional
                                                    )
                                                             │
                                                    ProductDataConsolidator.Consolidate()
                                                             │
                                                    IProgress<ScrapingProgressEvent>.Report()
                                                             │
                                                    WizardSessionRepository.SaveTickAsync()
```

- `Channel.CreateBounded<ExcelProductRecord>(capacity: workerCount * 2)` — prevents over-reading Excel into memory.
- Default worker count: 4, configurable via `ConcurrentWizardSession.WorkerCount`.
- `SemaphoreSlim(initialCount: maxConcurrentPages, maxCount: maxConcurrentPages)` where `maxConcurrentPages` defaults to 8. Each individual `SearchAndExtract` call acquires the semaphore before opening a Playwright page and releases it in `finally`.

**Why Channel over Parallel.ForEach?** Channels enable true async producer-consumer with back-pressure, compatible with `async/await` inside workers. `Parallel.ForEach` is synchronous and blocks thread pool threads.

---

### 4. Search Execution Strategy — Dual Mode

`DualTargetSearchEngine` implements two strategies selected per `TargetSearchConfig.SearchMode`:

**Mode A — DOM Input (`SearchMode.DomInput`)**:
```
1. page.GotoAsync(targetConfig.BaseSearchUrl, waitUntil: NetworkIdle, timeout: 15s)
2. page.WaitForSelectorAsync(selectorConfig.SearchInputSelector, timeout: 10s)
3. page.FillAsync(selectorConfig.SearchInputSelector, sku)
4. page.ClickAsync(selectorConfig.SearchSubmitSelector)   // OR page.Keyboard.PressAsync("Enter")
5. page.WaitForSelectorAsync(selectorConfig.FirstResultCardSelector, timeout: 15s)
```

**Mode B — Query Parameter (`SearchMode.QueryParam`)**:
```
1. url = targetConfig.SearchUrlTemplate.Replace("{sku}", Uri.EscapeDataString(sku))
2. page.GotoAsync(url, waitUntil: DOMContentLoaded, timeout: 15s)
3. page.WaitForSelectorAsync(selectorConfig.FirstResultCardSelector, timeout: 12s)
```

Each step is individually wrapped in `try/catch`. Failure at any step returns `TargetScrapeResult { Status = NotFound, FailureReason = <enum> }`.

---

### 5. Detail Extraction

After locating `FirstResultCardSelector`:
```
1. href = page.GetAttributeAsync(selectorConfig.DetailLinkSelector, "href")
2. page.GotoAsync(href, waitUntil: NetworkIdle, timeout: 20s)
3. priceText = page.InnerTextAsync(selectorConfig.RetailPriceSelector)   // nullable
4. imgSrcs = page.QuerySelectorAllAsync(selectorConfig.ImageGallerySelector) → each.GetAttributeAsync("src")
5. title = page.InnerTextAsync(selectorConfig.TitleSelector)              // nullable
```

Result: `TargetScrapeResult { Status = Found, RetailPrice = decimal?, ImageUrls = List<string>, Title = string?, SourceDetailUrl = string }`.

Numeric price parsing: `PriceParser.TryParse(rawText)` — strips currency symbols, thousand separators, and converts commas to dots.

---

### 6. Asymmetric Consolidation

`ProductDataConsolidator` receives `(ExcelProductRecord row, TargetScrapeResult r1, TargetScrapeResult? r2, SourcePriorityConfig priority)`:

```
RetailPrice  = priority.PriceSource == Source.Target1 ? r1.RetailPrice  : r2?.RetailPrice
ImageUrls    = priority.ImageSource  == Source.Target1 ? r1.ImageUrls    : r2?.ImageUrls ?? []
Title        = r1.Title ?? r2?.Title
SourceUrls   = [r1.SourceDetailUrl, r2?.SourceDetailUrl].Where(x => x != null)
SupplierCost = row.CostoProveedor   ← ALWAYS attached from Excel
Status       = (r1.Status == Found || r2?.Status == Found) ? Matched : NotMatched
```

`NotMatched` records are NOT silently dropped — they are emitted into the results stream with full Excel row data, enabling 100% export coverage for audit purposes.

---

### 7. Progress Streaming Without Polling

`ConcurrentScrapingEngine` exposes:
```csharp
IObservable<ScrapingProgressEvent> Progress { get; }
```

Internally backed by `Subject<ScrapingProgressEvent>` (System.Reactive) or equivalently `Channel<ScrapingProgressEvent>` read as `IAsyncEnumerable` on the UI side.

In `ConcurrentProviderWizardViewModel`:
```csharp
_engine.Progress
    .ObserveOn(DispatcherScheduler.Current)
    .Subscribe(evt => HandleProgressEvent(evt));
```

This eliminates polling and guarantees UI thread-safe updates to `ObservableCollection<ConsolidatedProductCard>`.

---

### 8. Pause / Stop Mechanism

```csharp
private readonly ManualResetEventSlim _pauseGate = new(initialState: true);
private CancellationTokenSource _cts = new();
```

Each worker task checks:
```csharp
_pauseGate.Wait(cancellationToken: _cts.Token);  // blocks on pause, throws on cancel
cancellationToken.ThrowIfCancellationRequested();
```

**Pause**: `_pauseGate.Reset()` — workers finish current item, then block.
**Resume**: `_pauseGate.Set()` — workers unblock and continue dequeuing.
**Stop**: `_cts.Cancel()` — `_pauseGate.Wait()` throws `OperationCanceledException`, caught at worker loop level. Channel writer is completed, remaining rows discarded. Partial results preserved.

---

### 9. Session Persistence Format

```json
{
  "sessionId": "uuid",
  "createdAt": "ISO8601",
  "lastSavedAt": "ISO8601",
  "excelFilePath": "C:\\...",
  "columnMapping": { "sku": 2, "costoProveedor": 5, "detailUrl": 7 },
  "targets": [
    { "baseUrl": "...", "searchMode": "QueryParam", "searchUrlTemplate": "...?q={sku}", "selectors": {...} },
    { "baseUrl": "...", "searchMode": "DomInput", "selectors": {...} }
  ],
  "sourcePriority": { "priceSource": "Target1", "imageSource": "Target2" },
  "lastCompletedRowIndex": 147,
  "results": [ { "sku": "...", "supplierCost": 99.5, ... } ]
}
```

`WizardSessionRepository.SaveTickAsync()` is called every 10 completed rows using a background `Task.Run` to avoid blocking the scraping pipeline. File write uses `File.WriteAllTextAsync` with a `.tmp` → atomic rename pattern to prevent corruption on crash.

---

### 10. AI Selector Discovery Integration

`AiSelectorDiscoveryService` adapts the existing `IAnalysisService.AnalyzePageAsync()` contract:
1. Fetches raw page HTML via `HttpClient` (max 200 KB, then truncated to 50 KB of significant DOM).
2. Sends to `IAnalysisService` with a specialized system prompt requesting only selector output.
3. Parses the JSON response into `SelectorConfig`.
4. Saves `SelectorConfig` to `TargetSearchConfig.Selectors` in session.

**Hard constraint**: `AiSelectorDiscoveryService` is only injected into Step 2 ViewModel. `ConcurrentScrapingEngine` does NOT take `ISelectorDiscoveryService` as a dependency — enforced at constructor level, verified by architecture tests.

---

### 11. WPF Navigation Model

The wizard uses a single `ContentControl` in `ConcurrentProviderWizardView` bound to `CurrentStepViewModel` (type `ViewModelBase`). `ConcurrentWizardNavigator` manages step transitions and calls `WizardSessionRepository.SaveStepAsync()` on each transition:

| Step | ViewModel | View |
|---|---|---|
| 1 | `Step1ExcelIngestionViewModel` | `Step1ExcelIngestionView.xaml` |
| 2 | `Step2TargetConfigViewModel` | `Step2TargetConfigView.xaml` |
| 3 | `Step3SourcePriorityViewModel` | `Step3SourcePriorityView.xaml` |
| 4 | `Step4ExecutionViewModel` | `Step4ExecutionView.xaml` |

---

## Risks / Trade-offs

- **[Anti-bot throttling on concurrent requests]** → Target sites may block rapid sequential requests from same IP.
  - *Mitigation*: Configurable per-domain `RequestDelayMs` (default 1500ms) applied between items in each worker. Optional random jitter ±500ms. Playwright uses non-headless mode by default to appear as real browser traffic.

- **[Playwright page leak on abrupt cancellation]** → If `CancellationToken` fires mid-navigation, pages may remain open.
  - *Mitigation*: Worker tasks use `try/finally` to call `await page.CloseAsync()` unconditionally. Browser context is owned by `ConcurrentScrapingEngine` and disposed via `IAsyncDisposable`.

- **[Session JSON file growth with large Excel files]** → 10,000 results serialized every 10 rows = frequent large writes.
  - *Mitigation*: Results list in session JSON written in append mode using `JsonSerializer` with streaming write. Alternatively, results are persisted separately in a SQLite table keyed by `sessionId + rowIndex`.

- **[Excel file moved or deleted before resume]** → Session references a file path that no longer exists.
  - *Mitigation*: On session resume, validate file path exists before proceeding. If missing, prompt user to re-select file; ColumnMapping is preserved so they only need to point to the new file location.

- **[WPF dispatcher overhead with high-frequency progress events]** → Firing 1 UI event per row at high concurrency (4 workers) may queue hundreds of dispatcher callbacks.
  - *Mitigation*: Progress events are throttled using `Observable.Sample(TimeSpan.FromMilliseconds(100))` before reaching the UI subscription. UI updates are batched, not per-event.
