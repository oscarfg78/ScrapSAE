## ADDED Requirements

---

### Requirement: Excel File Ingestion with Streaming Column Preview
The Concurrent Provider Wizard SHALL accept an Excel file (`.xlsx` / `.xls`) via drag-and-drop or file dialog. It SHALL parse the file using streaming row-by-row reading (not loading into memory wholesale), extract column headers from the first row, render a preview table showing the first 10 data rows, and block navigation to Step 2 until the user explicitly maps at minimum the SKU column and the "Costo del Proveedor" column.

#### Scenario: Mandatory column mapping enforced before continuing
- **WHEN** the user loads a valid Excel file and the preview renders correctly
- **THEN** the "Continuar" button SHALL remain disabled until both the SKU column and the Costo del Proveedor column selectors are set to non-empty distinct values.

#### Scenario: Optional metadata columns are offered for selection
- **WHEN** the preview table renders
- **THEN** the UI SHALL display optional column-mapping dropdowns for: Detail URL, Product Size/Dimensions, Product Images, and any remaining unmapped columns labeled as "Atributos Adicionales".

#### Scenario: Excel file cannot be parsed
- **WHEN** the uploaded file is corrupt, password-protected, or not a valid Excel format
- **THEN** the system SHALL display a clear inline error message, NOT navigate forward, and allow the user to retry with a different file.

---

### Requirement: Dual Target Search URL Configuration with Mode Detection
The Concurrent Provider Wizard SHALL allow configuring up to two (2) target search source URLs. For each target, the wizard SHALL offer two search execution modes: (A) **DOM Input Mode** — targeting an identified search input field in the page DOM to type the SKU and submit a search, and (B) **Query Parameter Mode** — constructing the search URL using a template containing a `{sku}` placeholder (e.g. `https://supplier.com/catalog/search?q={sku}`).

The wizard SHALL attempt auto-detection of the search mode by performing an initial HEAD/GET request to the target URL and inspecting for `<input type="search">` or `<form>` elements.

#### Scenario: Wizard detects search input automatically
- **WHEN** the user enters a target URL and clicks "Detectar modo de búsqueda"
- **THEN** the system SHALL make a lightweight DOM fetch, scan for `<input[type=search]>`, `<input[name*=search]>` or `<form>` elements, and pre-select DOM Input Mode with a suggested CSS selector if found; otherwise pre-select Query Parameter Mode.

#### Scenario: User manually overrides detected search mode
- **WHEN** the system auto-detects a mode but the user selects a different mode or modifies the auto-suggested selector
- **THEN** the wizard SHALL save the user's explicit override and use that configuration exclusively during batch execution.

#### Scenario: Single target configuration is valid
- **WHEN** the user configures only Target Source 1 and leaves Target Source 2 blank
- **THEN** the wizard SHALL proceed with single-source scraping and auto-assign Target Source 1 as the exclusive provider for both retail price and images.

---

### Requirement: AI-Assisted Selector Discovery (Setup Only)
During Step 2 (Target Configuration), the wizard SHALL provide an "Analizar con IA" button for each configured target URL. When triggered, the system SHALL fetch the live DOM of the search page, send a sanitized HTML excerpt (max 50 KB) to the configured AI service (via the existing `IAnalysisService` interface), and receive back a structured `SelectorDiscoveryResult` containing: `SearchInputSelector`, `SearchSubmitSelector`, `FirstResultCardSelector`, `DetailLinkSelector`, `RetailPriceSelector`, and `ImageGallerySelector`. These selectors SHALL be persisted in the session and used deterministically during all subsequent batch runs without any AI API calls.

#### Scenario: AI discovers selectors and populates configuration fields
- **WHEN** the user clicks "Analizar con IA" for a target URL
- **THEN** the system fetches the DOM, invokes the AI service, and populates the corresponding selector fields in the UI; the user can accept or manually edit any discovered selector before saving.

#### Scenario: AI call fails gracefully
- **WHEN** the AI service is unavailable or returns an unstructured response
- **THEN** the system displays an inline error message, leaves existing selector fields unchanged, and allows the user to enter selectors manually.

#### Scenario: AI is never invoked during batch execution
- **WHEN** the user confirms selector configuration and initiates batch scraping
- **THEN** the engine SHALL execute all search interactions and detail extractions exclusively using the stored `SelectorConfig` without making any calls to AI services.

---

### Requirement: Asymmetric Data Source Priority Assignment
When two target sources are configured, the Concurrent Provider Wizard UI SHALL require the user to explicitly designate: (1) which source (`Target1` | `Target2`) provides the authoritative **retail price ("precio de venta")**, and (2) which source (`Target1` | `Target2`) provides the authoritative **product images**. Both attributes MAY point to the same source. This configuration SHALL be mandatory before batch execution begins.

#### Scenario: User assigns different sources for price and images
- **WHEN** the user selects Target 1 as the price source and Target 2 as the image source
- **THEN** the `ProductDataConsolidator` SHALL extract `RetailPrice` from Target 1's scrape result and `ImageUrls` from Target 2's scrape result for every consolidated product record.

#### Scenario: User assigns the same source for both price and images
- **WHEN** the user selects Target 1 as both price source and image source
- **THEN** the consolidator SHALL read both retail price and images from Target 1's scrape result exclusively, ignoring Target 2's price and image data.

#### Scenario: Source priority configuration is blocked if sources unconfigured
- **WHEN** only one target URL is configured
- **THEN** the source priority step SHALL be skipped and the single configured target SHALL be used automatically for all data.

---

### Requirement: Concurrent Multi-Source Scraping via Channel Pipeline
The `ConcurrentScrapingEngine` SHALL use a `System.Threading.Channels.Channel<ExcelProductRecord>` producer-consumer pipeline where the Excel reader writes rows into the channel and up to N worker tasks (configurable, default 4) consume rows concurrently. For each consumed row, the engine SHALL launch search and detail extraction for Target 1 and Target 2 simultaneously using `Task.WhenAll`, bounded by a configurable `SemaphoreSlim` to cap total concurrent Playwright browser page instances (default max: 8).

#### Scenario: Parallel dual-source execution per SKU row
- **WHEN** a worker task dequeues an `ExcelProductRecord` row
- **THEN** it SHALL call `SearchAndExtractAsync(targetConfig1, sku)` and `SearchAndExtractAsync(targetConfig2, sku)` concurrently, wait for both results with `Task.WhenAll`, then pass them to `ProductDataConsolidator`.

#### Scenario: Semaphore limits total concurrent browser pages
- **WHEN** 10 rows are being processed simultaneously with dual sources (20 potential page instances)
- **THEN** the semaphore SHALL limit concurrent open Playwright pages to the configured maximum (default 8), queuing additional tasks until a slot is available.

---

### Requirement: Silent Fault Tolerance at Search, Navigation, and Extraction Levels
The scraping engine SHALL wrap each of the following operations in isolated `try/catch` blocks that log a warning via the existing `IScrapingLogsRepository` and return a `TargetScrapeResult` with `Status = ScrapingResultStatus.NotFound` WITHOUT surfacing exceptions to the caller: (1) Loading the target search page, (2) Locating and interacting with the search input or constructing the query URL, (3) Parsing search result cards, (4) Navigating to the product detail page, (5) Extracting retail price element, (6) Extracting image gallery elements.

#### Scenario: No search results found for a SKU
- **WHEN** the DOM selector for search result cards yields zero elements after the page loads
- **THEN** the engine SHALL log `[SKIP] SKU={sku} Source=Target1 Reason=NoSearchResults`, mark `TargetScrapeResult.Status = NotFound`, and continue to the next Excel row.

#### Scenario: Detail page navigation fails with exception
- **WHEN** clicking the first product card link throws a Playwright `TimeoutException` or `NavigationException`
- **THEN** the engine SHALL log `[SKIP] SKU={sku} Source=Target1 Reason=DetailNavigationFailed Exception={message}`, return `NotFound`, and proceed without rethrowing.

#### Scenario: Price selector yields empty result
- **WHEN** the retail price CSS selector returns null or empty text on a successfully loaded detail page
- **THEN** the engine SHALL extract all other available fields and record `RetailPrice = null` in the result (partial extraction; record is NOT skipped unless no fields at all are extractable).

---

### Requirement: Data Consolidation with Mandatory Supplier Cost Attachment
The `ProductDataConsolidator` SHALL merge the `TargetScrapeResult` pair into a `ConsolidatedProductResult` record. The `SupplierCost` field taken from the original `ExcelProductRecord.CostoProveedor` SHALL be attached to every `ConsolidatedProductResult` regardless of scraping outcome. Records where all sources returned `NotFound` SHALL be included in the output with `Status = NotMatched` and all scraped fields null, preserving the original Excel row data for export and auditing.

#### Scenario: Full consolidation with asymmetric source data
- **WHEN** Target 1 returns price data and Target 2 returns image data for the same SKU
- **THEN** the `ConsolidatedProductResult` SHALL contain: `Sku` (from Excel), `SupplierCost` (from Excel), `RetailPrice` (from Target 1), `ImageUrls` (from Target 2), and all other extracted metadata from both targets merged by field precedence order.

#### Scenario: Not-found records preserved in output
- **WHEN** both Target 1 and Target 2 return `NotFound` for a given SKU
- **THEN** the `ConsolidatedProductResult` for that row SHALL still be emitted with `Sku = excelRow.Sku`, `SupplierCost = excelRow.CostoProveedor`, `Status = NotMatched`, and all other fields null, ensuring 100% row coverage in exports.

---

### Requirement: Real-Time Execution Progress Streaming to WPF Client
The WPF desktop UI SHALL display execution progress without polling. The `ConcurrentScrapingEngine` SHALL expose an `IObservable<ScrapingProgressEvent>` (or `IProgress<T>`) stream that the ViewModel subscribes to via `ObserveOn(Dispatcher)` to safely update UI-bound collections. The progress stream SHALL emit events of types: `RowStarted`, `RowCompleted`, `RowSkipped`, `ExecutionPaused`, `ExecutionStopped`, `ExecutionFinished`.

#### Scenario: Live product card appears as each row completes
- **WHEN** the engine emits a `RowCompleted` event for a SKU
- **THEN** the ViewModel appends a new `ConsolidatedProductCard` item to `LiveResults` ObservableCollection within 200ms, visible to the user without any refresh action.

#### Scenario: Progress counters update in real-time
- **WHEN** the engine emits any progress event
- **THEN** the following bound properties SHALL update: `ProcessedCount`, `SuccessCount`, `SkippedCount`, `ErrorCount`, and `ProgressPercent = ProcessedCount / TotalRows * 100`.

---

### Requirement: Pause and Stop Execution Controls
The WPF execution UI SHALL provide "Pausar", "Reanudar", and "Detener" buttons backed by a shared `CancellationTokenSource` for stop and a `ManualResetEventSlim` gate for pause. "Detener" SHALL signal cancellation, allow currently-executing `Task.WhenAll` operations to complete their current item naturally, and then halt the channel consumer loop cleanly. Partial results collected up to the stop point SHALL be preserved and exportable.

#### Scenario: Pause halts new dequeuing but finishes active items
- **WHEN** the user clicks "Pausar"
- **THEN** the channel consumer tasks SHALL finish processing their current in-flight row, then block on the pause gate until "Reanudar" is clicked; no new rows SHALL be dequeued while paused.

#### Scenario: Stop emits final execution summary
- **WHEN** the user clicks "Detener" and all in-flight tasks have settled
- **THEN** the UI SHALL display a final summary: total rows processed, successful matches, skipped rows, and elapsed time; a "Exportar resultados parciales" button SHALL be enabled.

---

### Requirement: Wizard Session Persistence and Incremental Resume
The wizard SHALL serialize and persist the complete session state to a local file (`%AppData%\ScrapSAE\sessions\{sessionId}.json`) at every step transition AND after every batch tick (every 10 completed rows). Persisted state SHALL include: Excel file path and column mapping, all target URL configurations and selector configs, source priority assignment, and the `ConsolidatedProductResult` list accumulated so far. On wizard launch, the system SHALL query for saved sessions and offer resume if found.

#### Scenario: Session auto-saved after every 10 completed rows
- **WHEN** the engine completes the 10th, 20th, 30th... rows in a batch
- **THEN** the `WizardSessionRepository` asynchronously serializes the current state to the session JSON file without blocking the scraping pipeline.

#### Scenario: Resuming a previously interrupted session restores complete state
- **WHEN** the user opens the wizard and selects "Reanudar sesión" from a list of saved sessions
- **THEN** the wizard navigates directly to Step 4 (Execution Monitoring), restores all configuration fields from the saved state, pre-populates `LiveResults` with previously scraped `ConsolidatedProductResult` items, and offers to continue from the last completed row index.

#### Scenario: Already-processed rows are skipped on resume
- **WHEN** resuming a session where rows 1–150 were already processed
- **THEN** the Excel reader seeks directly to row 151 and the channel pipeline begins from that row, preventing duplicate processing.
