## Why

The wizard testing and actual scraping executions sometimes fail to retrieve data (e.g. returning 0 products) even when selectors appear correct. Currently, there is limited visibility into what the scraper actually evaluated in the DOM, why specific selectors failed, or how AI models interpreted the inputs. A detailed operation log that captures inputs, parsed DOM state, evaluation errors, and AI (GPT) communications is needed to troubleshoot configuration mismatches, understand failures, and refine our scraping methodology.

## What Changes

- Introduce a detailed `ScrapingExecutionLog` (or similar entity) to capture the step-by-step process of the `StrategyOrchestrator` and specific strategies (List, Direct, Families).
- Log exactly what selectors were applied, how many elements were found, and why a product might have been discarded (e.g. missing title).
- Log the prompt sent to GPT (when using AI extraction) and the exact response received, to allow debugging of the LLM's output.
- Store these logs either in Supabase or a local SQLite/file structure so they can be reviewed from the desktop application after a test execution.

## Capabilities

### New Capabilities
- `execution-logging`: Capability to log detailed steps of a scraping execution, including DOM analysis, applied selectors, and AI interaction.

### Modified Capabilities

## Impact

- `ScrapSAE.Infrastructure.Scraping.Strategies.*` will be updated to emit detailed execution logs instead of just `ILogger` traces.
- `ScrapSAE.Infrastructure.Scraping.StrategyOrchestrator` will aggregate these logs.
- The Desktop Wizard or API will need to expose these logs to the user after a test execution.
