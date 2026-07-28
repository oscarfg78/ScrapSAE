## 1. Core Definitions

- [x] 1.1 Create `ScrapingLogStep` class to represent a single log entry (Timestamp, Action, Selector, ElementCount, Details, Error, JsonPayload).
- [x] 1.2 Create `ScrapingLogTracker` class to aggregate `ScrapingLogStep` instances during a scraping run.
- [x] 1.3 Update `TestScrapeResult` (or equivalent DTO returned to the Wizard) to include `List<ScrapingLogStep> ExecutionLogs`.

## 2. Strategy Tracking Integration

- [x] 2.1 Update `StrategyOrchestrator` to use `ScrapingLogTracker` and pass it down.
- [x] 2.2 Update `ListExtractionStrategy` to log selectors used and element counts.
- [x] 2.3 Update `DirectExtractionStrategy` (and others like Families) to log their progress and extraction results.
- [x] 2.4 Update the AI interaction layer (`AIExtractionStrategy` or GPT client) to log the exact prompt string sent to the LLM and the raw JSON string received.

## 3. Verification & API Payload

- [x] 3.1 Serialize and return logs in the API response `TestScrapeResult` or scraping runner result to pass to the Wizard.
