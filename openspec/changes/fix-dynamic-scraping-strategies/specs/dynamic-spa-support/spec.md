## ADDED Requirements

### Requirement: Wait for Dynamic List Container
The `ListExtractionStrategy` SHALL wait for the `productContainer` or `productCard` to be visible in the DOM before attempting extraction, up to a specified timeout.

#### Scenario: Site loads products asynchronously
- **WHEN** the scraping process navigates to a SPA list page
- **THEN** it waits for the container selector to appear before extracting the products
