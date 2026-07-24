## Why

Currently, when scraping products, some websites contain detailed product descriptions (such as features, technical data, or extended descriptions) inside specific HTML sections (like description tabs). This information is either lost or not fully captured by the scraper. Improving the extraction of these details ensures that the final exported product contains comprehensive information that is valuable for the online store.

## What Changes

- Modify the scraping engine (or the AI extraction phase) to correctly capture the product's extended details/description when available on the page.
- Add support for a "description" or "details" selector if needed, or ensure the full relevant DOM content is passed to the AI to extract a robust `description`.
- Ensure the extracted details are mapped into the `description` field or appended to the `specifications` JSON.

## Capabilities

### New Capabilities
- `product-details-extraction`: Improves the capture and extraction of extended product descriptions from product pages and mapping them to the final output.

### Modified Capabilities

## Impact

- `PlaywrightScrapingService` or `ScrapingRunner`: To capture the description element.
- `OpenAIProcessorService`: To properly parse the extended description into the resulting JSON.
- Database/Export models: To ensure the new data correctly flows to Flashly or CSV.
