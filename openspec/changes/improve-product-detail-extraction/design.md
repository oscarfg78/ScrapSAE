## Context

During the provider discovery and configuration process, we added the ability to specify a Product Detail URL. However, the current extraction strategy fails to parse complex HTML blocks representing the product description, such as `tab-content-description` which contains multiple nested tags. We need to improve the extraction logic to correctly gather and format this data, and add proper validation for this specific step in the Provider Wizard's "Test" tab.

## Goals / Non-Goals

**Goals:**
- Upgrade the extraction mechanism in `ScrapingRunner` and/or `PageAnalysisService` to better parse deep, nested product descriptions and output them cleanly (e.g., iterating child nodes to form a JSON list or clean text).
- Update the API test endpoint to test product detail extraction for sample products.
- Enhance the Provider Wizard UI "Test" step to display product detail extraction results and a confidence indicator.

**Non-Goals:**
- Completely rewriting the HTML parser.
- Adding machine learning text summarization (we will rely on DOM structure and basic LLM extraction or rule-based parsing).

## Decisions

- **Decision 1: AI-Assisted DOM parsing for Details**: We will enhance `PageAnalysisService` to specifically instruct the AI to extract structured product characteristics from complex description DOMs (like `tab-content-description`), returning a JSON object or stringified list.
- **Decision 2: Product Detail validation in `/api/providers/test`**: The test endpoint will fetch the detail page (if a detail strategy is configured) for the first few products and validate that the detail extraction works, returning the extracted detail field alongside SKU/Name/Price.
- **Decision 3: Desktop UI Changes**: `ProviderWizardViewModel.cs` and `ProviderWizardView.xaml` will be updated to display the `Characteristics` field with its confidence during the "Test" phase, similar to other catalog fields.

## Risks / Trade-offs

- **Risk:** Fetching detail pages for all tested products might increase the test step duration significantly.
  - **Mitigation:** Limit the detail testing to only the first 2-3 products found in the catalog to keep the test step fast while ensuring the extraction rule works.
