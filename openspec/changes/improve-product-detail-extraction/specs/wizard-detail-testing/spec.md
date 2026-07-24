## ADDED Requirements

### Requirement: Product Detail API Testing
The Provider Wizard API Test endpoint SHALL fetch the detail page for tested products (if a detail strategy is enabled) to validate the detail extraction.

#### Scenario: Testing a catalog with detail URLs
- **WHEN** the user runs the "Test" step and a detail page strategy was found
- **THEN** the test endpoint also fetches and extracts details from the detail page, returning them alongside SKU, Name, and Price.

### Requirement: UI Confidence Indicator for Details
The Desktop Wizard "Test" UI SHALL display the extracted `Characteristics` and calculate a confidence score for it.

#### Scenario: Viewing Test Results
- **WHEN** the user views the result of the analysis
- **THEN** the field `Characteristics` is shown with the extracted sample and its confidence score.
