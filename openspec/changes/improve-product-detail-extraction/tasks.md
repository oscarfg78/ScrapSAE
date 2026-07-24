## 1. Extraction Engine Updates

- [ ] 1.1 Update `PageAnalysisService` (or equivalent AI/DOM parser) to handle complex HTML blocks for product details (e.g., iterating nested nodes in `tab-content-description`).
- [ ] 1.2 Modify `ScrapingRunner` to apply the improved detail extraction strategy and return a clean text or JSON structured list.

## 2. API Test Endpoint Modification

- [ ] 2.1 Update `TestScrapingConfig` endpoint to optionally perform a detail page fetch for the first N products.
- [ ] 2.2 Ensure the test response includes the extracted product details/characteristics alongside the catalog data.

## 3. Provider Wizard UI Enhancements

- [ ] 3.1 Update `ProviderWizardViewModel.cs` to handle the `Characteristics` field from the API test response.
- [ ] 3.2 Modify `ProviderWizardView.xaml` to display the extracted detail data and its confidence indicator in the Test Results tab.
- [ ] 3.3 Verify UI responsiveness and layout after adding the new indicators.
