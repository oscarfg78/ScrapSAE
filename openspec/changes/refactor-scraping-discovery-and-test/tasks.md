## 1. Core Abstractions

- [x] 1.1 Create `SelectorCombinator` class inside `ScrapSAE.Infrastructure.Scraping.Strategies` to handle CSS/XPath generation and iteration logic.
- [x] 1.2 Refactor `GetDualSelector` out of `ListExtractionStrategy` into a shared utility or base class so all strategies can access it consistently.

## 2. Scraping Strategy Integration

- [x] 2.1 Update `ListExtractionStrategy` to use `SelectorCombinator` and iterate over combinations for `ProductContainer`.
- [x] 2.2 Update `DirectExtractionStrategy` to use the shared abstraction for deep item data extraction.
- [x] 2.3 Implement timeouts and logging within the combinator logic using `ScrapeExecutionContext.LogTracker`.

## 3. Testing Engine Validation

- [x] 3.1 Modify the API endpoints / Services involved in the Discovery Test step to require actual extraction validation (checking if at least one extracted product has non-null properties).
- [x] 3.2 Ensure the orchestrator fallback logic is triggered correctly when the primary selector is deemed invalid during the test.

## 4. Final Testing and Clean Up

- [x] 4.1 Kill existing `ScrapSAE.Api` processes (if any are locking DLLs).
- [x] 4.2 Rebuild the backend and start the API.
- [x] 4.3 Verify execution with the desktop Wizard.
