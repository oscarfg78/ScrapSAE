## Why

The `StrategyOrchestrator` currently uses `ListExtractionStrategy` which evaluates the DOM immediately using `QuerySelectorResilientAsync`. For dynamic SPAs or search engines like Searchanise (e.g. IDSupply, some Festo distributors), elements are loaded asynchronously after `DOMContentLoaded`. The legacy methods waited specifically for these elements and also navigated to the detail page to extract missing fields like `Description`. The lack of synchronization and detail-enrichment in the new orchestrator causes it to fail or retrieve partial data.

## What Changes

- Update `ListExtractionStrategy` to include `WaitForSelectorAsync` before attempting extraction, allowing time for dynamic components to render.
- Implement an Enrichment phase (e.g., `DetailEnrichmentStrategy` or integrated inside `ListExtractionStrategy`) to visit the extracted `SourceUrl` and retrieve missing data, specifically the `Description` and `Sku`, mirroring the legacy `EnrichIdSupplyProductFromDetailPageAsync` behavior.
- Support pagination or "Load More" logic generically if required by the site configuration.

## Capabilities

### New Capabilities
- `dynamic-spa-support`: Allow list extraction to gracefully wait for asynchronous dynamic elements using configured selectors.
- `detail-page-enrichment`: Optional enrichment step that navigates from a list card to the detail page to extract additional information (e.g., Description, Images) using `DirectExtractionStrategy` or similar.

### Modified Capabilities

## Impact

- `ScrapSAE.Infrastructure.Scraping.Strategies.ListExtractionStrategy` will be updated.
- `ScrapSAE.Infrastructure.Scraping.StrategyOrchestrator` may be updated to chain List and Direct strategies if enrichment is enabled.
- `SiteProfile` / `ScrapingStrategyDefinition` might need new properties to enable enrichment.
