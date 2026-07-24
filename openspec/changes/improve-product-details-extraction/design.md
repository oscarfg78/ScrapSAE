## Context

Currently, the scraping process extracts text and specific attributes from product pages. However, rich descriptions located in tabs or specific containers (such as `#tab-content-description` or `.product-description`) may not be captured properly, either because they require specific selectors or because they are lost when converting the entire page to plain text for AI processing.

## Goals / Non-Goals

**Goals:**
- Provide a reliable mechanism to target and extract extended product descriptions.
- Pass the extracted description content accurately to the resulting product payload (either directly or via the AI processing step).
- Allow configuration per-supplier (SiteProfile) using `SecondarySelectors` or similar mechanism to target the description element.

**Non-Goals:**
- Completely rewriting the scraping engine.
- Automatically guessing the description container without any configuration.

## Decisions

- **Decision 1: Configuration via SecondarySelectors**:
  We will use the existing `SecondarySelectors` dictionary on the `SiteProfile` (e.g., key `"description"`) to allow users to specify the CSS selector for the description block.
- **Decision 2: Extraction logic in PlaywrightScrapingService**:
  The `PlaywrightScrapingService` will check for the `"description"` key in `SecondarySelectors`. If found, it will extract the `innerHTML` or `innerText` of that element.
- **Decision 3: Mapping via AIProcessor or Direct Assignment**:
  If the AI is used to process the product, we can append the extracted description text to the AI context to ensure it incorporates it into the final JSON. Alternatively, we can inject it directly into the `Specifications` JSON as "Description" or map it to the `Description` property of `StagingProduct` if the AI leaves it empty. We will aim to map it to the `Description` property or `Specifications` dictionary.

## Risks / Trade-offs

- **Risk**: Description HTML might be too large for the AI context limit.
  - **Mitigation**: We will extract `innerText` instead of raw HTML, or truncate it if it exceeds a certain threshold.
- **Risk**: Selectors might change on the provider's website.
  - **Mitigation**: Using `SecondarySelectors` allows the user to update the selector dynamically from the UI without code changes.
