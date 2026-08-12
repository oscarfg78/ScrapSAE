# scroll-pagination-scraping Specification

## ADDED Requirements

### Requirement: Targeted Scroll to Last Product and Footer
The system SHALL perform targeted scrolling to the last rendered product item element in the DOM followed by a scroll towards the page footer to trigger lazy-loading and dynamic AJAX pagination events.

#### Scenario: Scrolling to trigger dynamic loading
- **WHEN** the scraping engine processes a catalog page with dynamic product loading
- **THEN** it scrolls to the last visible product element and footer, triggering the browser's scroll and intersection events.

### Requirement: Incremental Product Hydration Waiting
The system SHALL wait for new product DOM elements to hydrate after each scroll action and compare the new product card count against the previous count.

#### Scenario: New products loaded after scroll
- **WHEN** the scroll action triggers new products via AJAX
- **THEN** the system detects the increase in product card count and extracts the newly loaded product records.

### Requirement: Iterative Scroll Extraction Loop and Termination
The system SHALL repeat the scroll-and-extract process in a loop until no new products appear after threshold retries or until the configured max products limit is reached.

#### Scenario: End of catalog reached
- **WHEN** consecutive scroll attempts produce no additional product cards in the DOM
- **THEN** the system terminates the scroll loop and completes the page extraction gracefully.

#### Scenario: Max products limit reached
- **WHEN** the total extracted product count reaches the site's configured `MaxProductsPerScrape` limit
- **THEN** the system immediately stops scrolling and finishes processing.
