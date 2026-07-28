## Why

The current scraping process, particularly in discovery and testing phases, suffers from reliability issues. Previous attempts have led to brittle selector extraction where discovery succeeds (finding `.grid-item` or similar) but testing and subsequent execution fail to extract the products. We need to overhaul these steps to build a more robust, resilient engine that leverages logs, creates structured class design patterns for reuse, and aggressively iterates over combinations of CSS and XPath patterns to find the method that truly works.

## What Changes

- Redesign of the discovery and testing pipeline to execute true end-to-end extraction verification.
- Implementation of a resilient strategy pattern allowing dynamic fallback and combinations of extraction logic.
- Establishment of new scanning patterns focusing on robust CSS and XPath combinations.
- Better consumption and structured use of the Scraping Execution Logs to inform which patterns failed and why, guiding the orchestration logic dynamically.
- Refactoring `ListExtractionStrategy` and `DirectExtractionStrategy` to leverage shared, reusable code blocks (e.g. `SelectorCombinator`, `ExtractionValidator`).

## Capabilities

### New Capabilities
- `dynamic-selector-combinator`: A new engine capability that tests multiple permutations and combinations of CSS/XPath to discover the best match during testing.
- `extraction-pattern-library`: A standardized library of design patterns/classes for extraction logic reuse.

### Modified Capabilities
- `provider-discovery`: Overhauling the scraping test execution to mandate validation of discovered selectors before saving the provider config.

## Impact

- `ScrapSAE.Infrastructure.Scraping.Strategies` (All Strategies)
- `ScrapSAE.Api` Controllers and Scrape Runners involved in the Discovery and Test steps.
- `ScrapSAE.Desktop` Wizard (testing feedback loops).
