## Context

The scraping engine recently adopted `StrategyOrchestrator` allowing dynamic strategy assignment (Direct, List, Families) configured via a wizard. However, legacy logic successfully handled dynamic SPA sites like IDSupply (Searchanise) using explicit wait logic and navigating to detail pages to enrich data (like description and high-res images). The new list extraction strategy doesn't support asynchronous element loading (it executes `QuerySelectorResilientAsync` right away) nor does it perform detail enrichment, causing it to return empty lists or products missing descriptions.

## Goals / Non-Goals

**Goals:**
- Enable `ListExtractionStrategy` to wait for dynamic elements (e.g. `productContainer` or `productCard`).
- Add a detail enrichment mechanism that visits the product `SourceUrl` extracted from the list card to obtain `Description` and other properties.
- Retain backwards compatibility for non-dynamic sites.

**Non-Goals:**
- Completely rewrite the orchestrator.
- Change the Database Schema or API endpoints.

## Decisions

- **Decision 1: Dynamic Waiting in List Strategy**
  - *Rationale*: We will use Playwright's `Locator.WaitForAsync()` or `Page.WaitForSelectorAsync()` before attempting extraction. This is standard for SPAs.

- **Decision 2: Detail Enrichment**
  - *Rationale*: We will implement an enrichment step after the list extraction phase. If the product lacks a description and has a valid `SourceUrl`, the scraper will visit the detail page to extract it using `DirectExtractionStrategy` logic or a specific enrichment method.

## Risks / Trade-offs

- **Risk**: Waiting for elements can slow down scraping on static sites if selectors are misconfigured.
  - *Mitigation*: Use a short, configurable timeout (e.g., 5-10s).
- **Risk**: Navigating to detail pages for every product in a list significantly increases scraping time.
  - *Mitigation*: Enrichment should only be triggered if explicitly configured or if critical data (like Description) is missing and a detail selector is provided.
