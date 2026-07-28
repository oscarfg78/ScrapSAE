## ADDED Requirements

### Requirement: Dynamic Selector Combination Testing
The testing engine SHALL iterate through multiple permutations of CSS and XPath patterns extracted from the discovered selectors.

#### Scenario: Fallback combinations during test
- **WHEN** the primary CSS selector fails to extract product data
- **THEN** the combinator injects a fallback XPath or hybrid selector automatically to retry the extraction.

### Requirement: Scrape Log Tracing
The combinator SHALL log each permutation attempted along with its extraction result using the `ScrapeExecutionContext.LogTracker`.

#### Scenario: Successful permutation logging
- **WHEN** a permutation succeeds
- **THEN** it is logged as the final selected selector and used to update the configuration.
