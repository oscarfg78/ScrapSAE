## Why

The current provider wizard is single-threaded, coupled to sequential Aspel SAE workflows, and lacks multi-source data consolidation from raw Excel files. Users need a high-performance, resilient wizard that can ingest arbitrary supplier Excel files, resolve SKUs concurrently across up to two target search sources, prioritize extracted attributes asymmetricly (e.g. price from source A, images from source B), tolerate missing products silently, and persist wizard state for deferred execution without modifying the original wizard.

## What Changes

- **New Concurrent Scraping Wizard Architecture**: Create a distinct, non-destructive concurrent wizard WPF UI (`ConcurrentProviderWizardView` / `ConcurrentProviderWizardViewModel`) alongside the existing wizard.
- **Excel File Ingestion & Column Mapping**: Ingest user-uploaded Excel files (`.xlsx`/`.xls`), render early row previews, and mandate selection of SKU and Supplier Cost ("Costo del Proveedor"), with optional product metadata columns.
- **Multi-Source Concurrent Search Configuration**: Allow setting up to two (2) target search URLs operating simultaneously per SKU, supporting both DOM search input box interaction and direct URL query parameter parameterization (`?q={sku}`).
- **Asymmetric Extraction & Data Source Prioritization**: UI configuration to explicitly select which source provides the public retail price ("precio de venta") and which source provides product images, supporting hybrid source combinations.
- **Silent Fault Tolerance**: Automatic skipping of missing records or unresolved detail pages without failing the batch job or halting execution.
- **Data Consolidation**: Merging scraped DOM fields (Public Price, Images, Detail Attributes) with original row Supplier Cost from Excel.
- **Real-Time Client Execution & Control**: Display real-time progress, live scraped items table/preview, and support explicit pause/stop actions.
- **Wizard State Persistence & Resume**: Save draft state and execution progress from early steps to database/file storage to allow resuming wizard sessions seamlessly.
- **Hybrid AI Selector Identification**: One-time AI assistance during initial configuration steps to infer CSS/XPath selectors, switching strictly to deterministic execution during scraping batch runs.

## Capabilities

### New Capabilities
- `concurrent-scraping-wizard`: Ingest structured Excel files, configure dual-source concurrent search strategies (DOM or query param), extract asymmetric attributes (retail price vs images), execute fault-tolerant batch scraping with real-time UI monitoring/pause, support state persistence/resume, and leverage initial AI selector discovery.

### Modified Capabilities
*(None - existing capabilities and original wizard remain completely untouched)*

## Impact

- **Desktop UI (`ScrapSAE.Desktop`)**: New WPF Views, ViewModels, and Navigation options for the Concurrent Wizard.
- **Worker & Core Services (`ScrapSAE.Worker`, `ScrapSAE.Core`, `ScrapSAE.Infrastructure`)**:
  - Excel ingestion parser service (using ClosedXML / EPPlus or NPOI).
  - Parallel multi-source Web Scraping engine (Playwright / Puppeteer Sharp or HttpClient parallel execution).
  - Wizard draft state persistence repository.
  - Initial AI Selector discovery service (LLM service integration).
- **Data Models**: Consolidated scraped product DTOs containing mapped supplier costs and asymmetric public prices/media.
