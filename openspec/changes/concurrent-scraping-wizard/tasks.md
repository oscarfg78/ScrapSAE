## 1. Core Data Models (ScrapSAE.Core)

- [x] 1.1 Add `ExcelProductRecord` DTO: `Sku`, `CostoProveedor`, `RowIndex`, `OptionalAttributes Dictionary<string,string>`.
- [x] 1.2 Add `TargetSearchConfig` model: `SearchMode` enum (`DomInput` / `QueryParam`), `BaseSearchUrl`, `SearchUrlTemplate`, `SelectorConfig` nested object.
- [x] 1.3 Add `SelectorConfig` model: `SearchInputSelector`, `SearchSubmitSelector`, `FirstResultCardSelector`, `DetailLinkSelector`, `RetailPriceSelector`, `ImageGallerySelector`, `TitleSelector`.
- [x] 1.4 Add `TargetScrapeResult` model: `Status` (`Found` / `NotFound`), `FailureReason` enum, `RetailPrice decimal?`, `ImageUrls List<string>`, `Title string?`, `SourceDetailUrl string?`.
- [x] 1.5 Add `SourcePriorityConfig` model: `PriceSource` enum (`Target1` / `Target2`), `ImageSource` enum.
- [x] 1.6 Add `ConsolidatedProductResult` DTO: `Sku`, `SupplierCost`, `RetailPrice?`, `ImageUrls`, `Title`, `SourceDetailUrls`, `Status` (`Matched` / `NotMatched`), `ScrapedAt`.
- [x] 1.7 Add `ConcurrentWizardSession` model: `SessionId`, `CreatedAt`, `LastSavedAt`, `ExcelFilePath`, `ColumnMapping`, `Targets List<TargetSearchConfig>`, `SourcePriority`, `LastCompletedRowIndex`, `WorkerCount`, `MaxConcurrentPages`.
- [x] 1.8 Add `ScrapingProgressEvent` record: `EventType` enum (`RowStarted` / `RowCompleted` / `RowSkipped` / `ExecutionPaused` / `ExecutionStopped` / `ExecutionFinished`), `RowIndex`, `Sku`, `Result ConsolidatedProductResult?`, `ProcessedCount`, `SuccessCount`, `SkippedCount`, `ElapsedMs`.
- [x] 1.9 Define `IExcelIngestionService` interface: `PreviewAsync(filePath, maxRows)` and `StreamRowsAsync(filePath, mapping, startRowIndex)`.
- [x] 1.10 Define `ISelectorDiscoveryService` interface: `DiscoverSelectorsAsync(targetUrl, token)` → `SelectorConfig`.
- [x] 1.11 Define `IConcurrentScrapingEngine` interface: `IObservable<ScrapingProgressEvent> Progress`, `StartAsync(session, token)`, `PauseAsync()`, `ResumeAsync()`, `StopAsync()`.
- [x] 1.12 Define `IWizardSessionRepository` interface: `SaveAsync(session)`, `SaveTickAsync(session)`, `ListSavedSessionsAsync()`, `LoadAsync(sessionId)`, `DeleteAsync(sessionId)`.

## 2. Excel Ingestion Service (ScrapSAE.Infrastructure)

- [x] 2.1 Add `ExcelDataReader` NuGet package (`ExcelDataReader`, `ExcelDataReader.DataSet`) to `ScrapSAE.Infrastructure.csproj`.
- [x] 2.2 Implement `ExcelIngestionService.PreviewAsync`: open file with `ExcelReaderFactory.CreateReader`, read `DataSet` with `AsDataSet(new ExcelDataSetConfiguration { UseColumnDataType = false, ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = true } })`, return first 10 rows as string arrays.
- [x] 2.3 Implement `ExcelIngestionService.StreamRowsAsync` as `IAsyncEnumerable<ExcelProductRecord>`: iterate via `IDataReader`, yield mapped rows from `startRowIndex`, handle `null`/`DBNull` cells gracefully, skip header row.

## 3. AI Selector Discovery Service (ScrapSAE.Infrastructure)

- [x] 3.1 Implement `AiSelectorDiscoveryService` depending on `IAnalysisService` (existing) and `HttpClient`.
- [x] 3.2 Fetch target page HTML via `HttpClient.GetStringAsync` with 15s timeout; truncate HTML to first 50 KB of `<body>` content.
- [x] 3.3 Invoke `IAnalysisService.AnalyzePageAsync` with a specialized prompt requesting only `SelectorConfig`-structured JSON output; parse result into `SelectorConfig`.
- [x] 3.4 Implement graceful failure: if analysis returns null or unparseable JSON, return a default `SelectorConfig` with all fields empty (caller shows error, does not throw).

## 4. Dual-Mode Search & Detail Extraction (ScrapSAE.Infrastructure)

- [x] 4.1 Implement `DualTargetSearchEngine` with internal `SearchAndExtractAsync(TargetSearchConfig config, string sku, IPage page)` method.
- [x] 4.2 Implement Mode A (DomInput): `GotoAsync` → `WaitForSelectorAsync(SearchInputSelector)` → `FillAsync` → `ClickAsync(SearchSubmitSelector)` → `WaitForSelectorAsync(FirstResultCardSelector)` — each step individually try/caught returning appropriate `FailureReason`.
- [x] 4.3 Implement Mode B (QueryParam): construct URL from `SearchUrlTemplate.Replace("{sku}", Uri.EscapeDataString(sku))` → `GotoAsync(url, DOMContentLoaded)` → `WaitForSelectorAsync(FirstResultCardSelector)`.
- [x] 4.4 Implement detail navigation: `GetAttributeAsync(DetailLinkSelector, "href")` → resolve relative URL → `GotoAsync(detailUrl, NetworkIdle, 20s)`.
- [x] 4.5 Implement detail field extraction: `InnerTextAsync(RetailPriceSelector)` with null safety → `PriceParser.TryParse` (strip `$`, `,`, spaces) → `decimal?`; `QuerySelectorAllAsync(ImageGallerySelector)` → `GetAttributeAsync("src")` for each → `List<string>`.
- [x] 4.6 Add `PriceParser` static utility: handle MXN/USD symbols, thousand-separator commas, decimal commas (European format).

## 5. Concurrent Scraping Engine (ScrapSAE.Infrastructure)

- [x] 5.1 Implement `ConcurrentScrapingEngine` with: `Channel<ExcelProductRecord>` (bounded capacity = workerCount × 2), `Subject<ScrapingProgressEvent>` (or `Channel<ScrapingProgressEvent>`) for progress, `ManualResetEventSlim _pauseGate`, `CancellationTokenSource _cts`.
- [x] 5.2 Implement producer task: reads `IAsyncEnumerable<ExcelProductRecord>` from `IExcelIngestionService.StreamRowsAsync` and writes to channel; completes channel writer on exhaustion or cancellation.
- [x] 5.3 Implement consumer worker task loop: acquire `SemaphoreSlim(maxConcurrentPages)` per target page, call `Task.WhenAll(SearchAndExtractAsync(target1), SearchAndExtractAsync(target2?))`, release semaphore in `finally`, pass results to `ProductDataConsolidator`.
- [x] 5.4 Integrate pause/stop: each worker calls `_pauseGate.Wait(_cts.Token)` at top of loop; `PauseAsync` calls `_pauseGate.Reset()`, `ResumeAsync` calls `_pauseGate.Set()`, `StopAsync` calls `_cts.Cancel()`.
- [x] 5.5 Emit `ScrapingProgressEvent` to progress subject after each `ConsolidatedProductResult` is produced; emit `ExecutionFinished` when all workers complete.

## 6. Product Data Consolidator (ScrapSAE.Infrastructure)

- [x] 6.1 Implement `ProductDataConsolidator.Consolidate(ExcelProductRecord row, TargetScrapeResult r1, TargetScrapeResult? r2, SourcePriorityConfig priority)` → `ConsolidatedProductResult`.
- [x] 6.2 Apply source priority rules: `RetailPrice` from `priority.PriceSource`, `ImageUrls` from `priority.ImageSource`; fall back to the non-null source if the designated source returned `NotFound`.
- [x] 6.3 Always attach `SupplierCost = row.CostoProveedor` regardless of scraping outcome.
- [x] 6.4 Set `Status = Matched` if either source returned `Found`; `NotMatched` if both returned `NotFound` — do NOT discard `NotMatched` records.

## 7. Session Persistence (ScrapSAE.Infrastructure)

- [x] 7.1 Implement `WizardSessionRepository` writing to `%AppData%\ScrapSAE\sessions\{sessionId}.json`.
- [x] 7.2 Implement `SaveTickAsync`: serialize session to `.tmp` file → rename to target path atomically (`File.Move(overwrite: true)`) to prevent partial-write corruption.
- [x] 7.3 Store `ConsolidatedProductResult` records in a companion `{sessionId}.results.json` file, appended in streaming JSON array format to avoid full rewrite on every tick.
- [x] 7.4 Implement `ListSavedSessionsAsync`: scan sessions directory, deserialize session header fields only (skip results file) for display.
- [x] 7.5 Implement `LoadAsync`: load session JSON + results file, reconstruct `ConcurrentWizardSession` with full `ConsolidatedProductResult` list.

## 8. WPF UI — Step ViewModels (ScrapSAE.Desktop)

- [x] 8.1 Create `ConcurrentProviderWizardViewModel` (derives `ViewModelBase`): manages `CurrentStepViewModel`, `ConcurrentWizardSession`, and exposes navigation commands (`NextCommand`, `BackCommand`, `CancelCommand`).
- [x] 8.2 Create `Step1ExcelIngestionViewModel`: `LoadFileCommand` (opens `OpenFileDialog` filtered to `.xlsx/.xls`), `PreviewRows ObservableCollection<string[]>`, `ColumnHeaders string[]`, `SkuColumnIndex int?`, `CostColumnIndex int?`, computed `CanContinue` property.
- [x] 8.3 Create `Step2TargetConfigViewModel`: `Target1Config TargetSearchConfig`, `Target2Config? TargetSearchConfig`, `DetectSearchModeCommand` (async, per-target), `DiscoverWithAiCommand` (async, per-target), `TestSearchCommand` (validates selector against live site with a sample SKU from preview rows).
- [x] 8.4 Create `Step3SourcePriorityViewModel`: `PriceSource` enum property, `ImageSource` enum property, visibility logic hiding this step if only 1 target configured.
- [x] 8.5 Create `Step4ExecutionViewModel`: `StartCommand`, `PauseCommand`, `ResumeCommand`, `StopCommand`, `ExportCommand`; `LiveResults ObservableCollection<ConsolidatedProductCard>`; counters `ProcessedCount`, `SuccessCount`, `SkippedCount`, `ProgressPercent`; subscribes to `IConcurrentScrapingEngine.Progress` via `ObserveOn(DispatcherScheduler.Current)`, throttled at 100ms.

## 9. WPF UI — Views and XAML (ScrapSAE.Desktop)

- [x] 9.1 Create `ConcurrentProviderWizardView.xaml`: shell with step indicator header, `ContentControl` bound to `CurrentStepViewModel`, `DataTemplateSelector` mapping step VM types to view templates.
- [x] 9.2 Create `Step1ExcelIngestionView.xaml`: drag-drop zone, file path display, `DataGrid` preview table (first 10 rows), ComboBox column mappers for SKU, Costo Proveedor, and optional metadata columns.
- [x] 9.3 Create `Step2TargetConfigView.xaml`: two expandable `GroupBox` panels (Target 1, Target 2); each contains URL `TextBox`, mode radio buttons, selector fields, "Detectar modo" button, "Analizar con IA" button, "Probar búsqueda" button with live result feedback.
- [x] 9.4 Create `Step3SourcePriorityView.xaml`: two `RadioButton` groups (Price Source: T1/T2, Image Source: T1/T2) with visual target summary cards showing configured URL and detected selectors.
- [x] 9.5 Create `Step4ExecutionView.xaml`: top summary bar (progress bar, counters), `ScrollViewer` with `WrapPanel` or `ItemsControl` displaying `ConsolidatedProductCard` tiles, bottom toolbar (Pause/Resume/Stop/Export), status text.

## 10. DI Registration and Entry Point (ScrapSAE.Desktop)

- [x] 10.1 Register new services in `App.xaml.cs` (or existing DI container): `IExcelIngestionService` → `ExcelIngestionService`, `ISelectorDiscoveryService` → `AiSelectorDiscoveryService`, `IConcurrentScrapingEngine` → `ConcurrentScrapingEngine`, `IWizardSessionRepository` → `WizardSessionRepository`, `ProductDataConsolidator` (transient).
- [x] 10.2 Add "Nuevo Wizard Concurrente" menu/button entry in the existing `MainWindow.xaml` or `MainViewModel` that opens `ConcurrentProviderWizardView` as a dialog/navigation page; this is the ONLY modification to existing files.
- [x] 10.3 On wizard dialog open, call `WizardSessionRepository.ListSavedSessionsAsync()` and show resume prompt if sessions exist.

## 11. Testing

- [x] 11.1 Unit test `ExcelIngestionService.PreviewAsync` with sample `.xlsx` files: valid file, empty file, missing mandatory columns, corrupt file.
- [x] 11.2 Unit test `ExcelIngestionService.StreamRowsAsync` with `startRowIndex > 0` to verify row-seek on resume.
- [x] 11.3 Unit test `ProductDataConsolidator.Consolidate`: asymmetric price/image priority permutations (T1/T1, T1/T2, T2/T1, T2/T2), both-NotFound → NotMatched with SupplierCost preserved, one-Found fallback.
- [x] 11.4 Unit test `PriceParser.TryParse`: MXN symbol, USD symbol, thousand commas, European decimal comma, whitespace, `null` / empty → `null`.
- [x] 11.5 Unit test `WizardSessionRepository.SaveTickAsync` → verify `.tmp` rename atomicity, verify re-load round-trip equality.
- [x] 11.6 Integration test `ConcurrentScrapingEngine` with mock `DualTargetSearchEngine`: verify all rows emitted, `NotFound` rows produce `NotMatched` results, pause/resume cycle, stop emits `ExecutionStopped` event.
- [x] 11.7 Architecture test (using `NetArchTest` or manual reflection check): verify `ConcurrentScrapingEngine` constructor has no dependency on `ISelectorDiscoveryService`.
