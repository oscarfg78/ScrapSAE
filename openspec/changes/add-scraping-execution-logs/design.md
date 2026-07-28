## Context

When running scraping tests from the Wizard, users currently receive a generic "No products found" or empty list if the extraction fails. We lack deep visibility into which specific selector failed, how many elements were matched at each step, and what the AI (GPT) received or replied when `AIExtractionStrategy` is used. This makes troubleshooting custom configurations very difficult.

## Goals / Non-Goals

**Goals:**
- Implement a `ScrapingLogTracker` to collect detailed steps during `StrategyOrchestrator.ExecuteAsync`.
- Record DOM insights (e.g., "Selector .grid-item found 0 elements").
- Record AI interactions (Prompts sent, JSON responses received, validation errors).
- Return this log payload as part of the test results so the desktop client can display it.

**Non-Goals:**
- Build a persistent, searchable logging dashboard in Supabase for all historical production executions (the focus is on testing and debugging visibility first).
- Modify the database schema of the actual products (only API return types for testing need to change).

## Decisions

- **Decision 1: Create a `ScrapingLogTracker`**
  - *Rationale*: A dedicated tracker object (passed down to strategies or scoped in DI) can collect `ScrapingLogStep` objects sequentially and return them with the final result, decoupled from the standard ASP.NET `ILogger`.

- **Decision 2: Enhance `TestScrapeResult` DTO**
  - *Rationale*: Adding a `List<ScrapingLogStep> ExecutionLogs` property to the result DTO allows the API to serialize the detailed logs back to the Desktop Wizard cleanly.

## Risks / Trade-offs

- **Risk**: Logs can become very large if we log the entire HTML of the page.
  - *Mitigation*: Only log the selectors used, element counts, AI prompts, and AI responses. Do not log full raw HTML unless it's specifically requested as a small snippet.
