## 1. Dynamic Waiting

- [x] 1.1 Update `ListExtractionStrategy.ExecuteAsync` to wait for the list container before attempting extraction, using `page.WaitForSelectorAsync` with a configurable or default timeout (e.g. 10s).
- [x] 1.2 Update `QuerySelectorAllResilientAsync` or similar inner logic to handle delays or retries if elements appear dynamically.

## 2. Enrichment Step

- [x] 2.1 Create an enrichment phase at the end of `ListExtractionStrategy.ExecuteAsync`. Iterate over extracted products and if `Description` is null or empty, and a `SourceUrl` exists, navigate to it using `page.GotoAsync`.
- [x] 2.2 Extract `Description` from the detail page using the provided selectors.
- [x] 2.3 Extract missing `SkuSource` or high-res images if required, similar to legacy `EnrichIdSupplyProductFromDetailPageAsync`.
