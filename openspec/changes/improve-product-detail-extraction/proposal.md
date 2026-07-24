## Why

The current product detail detection isn't working robustly on complex product detail pages. For example, when there are nested elements like `tab-content-description` containing multiple specification details, the extractor fails to obtain the complete description or parse it cleanly into a structured format. Improving this extraction strategy and validating it thoroughly in the wizard's "Test" step is essential for high-quality data ingestion.

## What Changes

- Update the extraction logic to handle complex HTML structures for product details, aggregating inner texts or formatting them into a structured JSON list for subsequent parsing.
- Modify the wizard's "Test" step so that it actually fetches the product detail page for tested products and validates the product detail extraction.
- Display a confidence indicator and the extracted details for the detail-level analysis in the wizard's "Test" step.

## Capabilities

### New Capabilities
- `advanced-product-detail-extraction`: Adds capabilities to parse and clean up complex description structures into structured JSON or cohesive descriptions during extraction.
- `wizard-detail-testing`: Incorporates the detail page extraction test inside the "Test" step, providing a confidence indicator for product detail discovery.

### Modified Capabilities
- `provider-wizard-product-detail`: Update the requirement to include testing the extracted product details in the wizard's testing phase.

## Impact

- Wizard UI (Test step will show detail page analysis results)
- Scraping Engine / Analysis Services (Strategy logic to parse nested HTML elements like `tab-content-description` into JSON lists or clean text)
- API endpoint for product testing to include detail-level extraction.
