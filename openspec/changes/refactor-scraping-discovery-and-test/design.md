## Context

The discovery engine successfully identifies a set of likely selectors (e.g., `.grid-item` for product containers) but during the test scraping step, the engine fails to extract data. The primary issue is that the extraction strategies (`ListExtractionStrategy`, `DirectExtractionStrategy`) rigidly apply the selector string passed to them. If it fails due to context or page variation (e.g. dynamic DOM loading, subtle structure changes, or single CSS/XPath logic mismatch), the test returns zero products. 

## Goals / Non-Goals

**Goals:**
- Implement a reusable, resilient `SelectorCombinator` or `DynamicSelectorEvaluator` class.
- Provide a mechanism to test various combinations of CSS and XPath patterns intelligently during the testing phase.
- Use execution logs (`ScrapeExecutionContext.LogTracker`) to trace failing patterns and guide subsequent combinations.
- Enforce validation: during testing, extraction must actually return properties (like Name, Price) before declaring success.

**Non-Goals:**
- Completely rewriting Playwright infrastructure.
- Building a new frontend. We only need to fix the backend extraction logic so the frontend test step actually succeeds.

## Decisions

**1. Create a `SelectorCombinator` Utility:**
Instead of `ListExtractionStrategy` doing manual fallback logic inside `GetDualSelector`, we will introduce a `SelectorCombinator` class. It will generate a matrix of `[CSS, XPath, Reverse XPath, CSS + child elements]` permutations to attempt robust extraction.

**2. Standardize `GetDualSelector` into a Base Pattern:**
Extract the `GetDualSelector` and `QuerySelectorResilientAsync` methods into a reusable abstract base class `BaseExtractionStrategy` or a shared utility to dry up `ListExtractionStrategy` and `DirectExtractionStrategy`.

**3. True Validation in Testing:**
We will update `ScrapingOrchestrator` or the `test` endpoint logic so that a test scrape only succeeds if at least one product has a non-null `Title` or `SkuSource`. If it fails, the orchestrator invokes the `SelectorCombinator` to self-heal and find a working combination, leveraging `AddLog` to record each attempt.

## Risks / Trade-offs

- **Performance Trade-off**: Permuting multiple selectors takes longer if we wait for network/DOM idle each time. Mitigation: We will limit permutations to a smart subset and use short timeouts (`500ms` or `1s` instead of `10s`) during permutation attempts.
- **Complexity**: Strategies become more abstracted. Mitigation: Clearly document the `SelectorCombinator` and ensure logs are detailed (INFO/DEBUG) so we know exactly which selector worked.
