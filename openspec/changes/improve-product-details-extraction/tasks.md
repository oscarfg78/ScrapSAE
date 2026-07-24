## 1. Core Implementation

- [x] 1.1 Update `PlaywrightScrapingService` (or relevant scraping logic) to look up a secondary selector with the key `"description"`.
- [x] 1.2 If the `"description"` selector matches an element on the page, extract its `innerText` (or `innerHTML` stripped of dangerous tags).
- [x] 1.3 Map the extracted description text to the `ScrapedProduct` structure or pass it explicitly to the AI processor context, so it is either directly set in the `Description` property or merged into `Specifications`.

## 2. API and JSON Mapping

- [x] 2.1 Update `OpenAIProcessorService` to receive the extended description explicitly and instruct the AI to use it for the final JSON's `description` field.
- [x] 2.2 Alternatively (or additionally), in `ScrapingRunner.cs` or `RescrapeJobService.cs`, fallback to directly mapping the extracted description to the `StagingProduct` if the AI leaves the `description` field empty.
- [x] 2.3 Verify that the mapping generates the correct Flashly DTO via `FlashlyProductMapper`.

## 3. Testing and Verification

- [x] 3.1 Run a scraping job against a product URL with a known description tab/element using a properly configured `SiteProfile` (with `SecondarySelectors["description"]` set).
- [x] 3.2 Verify that the `StagingProduct` created contains the full extracted description.
- [x] 3.3 Ensure the information is propagated correctly to the final export payload.
