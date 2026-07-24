## 1. UI Modificactions (Wizard)

- [x] 1.1 Add a text input field for "Marca" (Brand) in the `SiteUrlStepView` or the first step of the configuration Wizard in `ScrapSAE.Desktop`.
- [x] 1.2 Bind the new input field to a property in the corresponding ViewModel (e.g. `SiteProfile.SupplierBrand` or a property mapped to `SecondarySelectors["brand"]`).

## 2. Scraping Limits Configuration

- [x] 2.1 Locate the execution logic for the test step in the Wizard (e.g., `TestStepViewModel` or `IScrapingService` invocation).
- [x] 2.2 Temporarily set or pass a limit parameter of 10 for the `MaxProductsPerJob` (or equivalent test configuration) during the Wizard's test run.
- [x] 2.3 Locate the final step of the Wizard where the `SiteProfile` is saved.
- [x] 2.4 Force the `MaxProductsPerJob` (or equivalent saved limit) to 120 just before serializing and saving the configuration to ensure the background/manual scrapes start with that limit.

## 3. Discovery Integration in Scraping Runner

- [x] 3.1 Extract the core discovery logic from the Wizard (e.g. `ExtractProductsFromFamilyPageAsync`, `ExplorePaginationAsync`) into reusable methods in `PlaywrightScrapingService` if they are not already accessible.
- [x] 3.2 In `ScrapingRunner` or the entry point for the main scraping process, invoke this discovery step before or concurrently with the static URL processing.
- [x] 3.3 Ensure the discovered URLs are deduplicated and merged into the target processing list.
- [x] 3.4 Verify that for existing suppliers without complex pagination logic, this step either skips safely or completes without errors (retrocompatibility).

## 4. UI/UX Refactor for Scraping Screen

- [x] 4.1 Update `ScrapSAE.Desktop`'s main Scraping view (e.g., `ScrapingViewModel` / `ScrapingView.xaml`).
- [x] 4.2 Add a new UI control (like a Progress Bar, Stepper, or dynamic Status text block) below "Estadísticas de Ejecución" to report granular phases: "Descubrimiento de Catálogo", "Resolución de Paginación", "Extracción".
- [x] 4.3 Enhance the "Estado" badge to use color-coding (e.g., Green for Exploring, Blue for Extracting).
- [x] 4.4 Provide a filtering toggle on the real-time Console (e.g., "Ver solo Errores") to reduce verbosity.

## 5. Verification

- [x] 5.1 Run the Wizard from the Desktop application.
- [x] 5.2 Verify the "Marca" field is present, enter a test value, and confirm it's present in the saved `SiteProfile`.
- [x] 5.3 Ensure the test execution stops after processing 10 products instead of the full 120.
- [x] 5.4 Open the saved configuration file and verify that the product processing limit is indeed 120 for subsequent jobs.
- [x] 5.5 Start a normal Scraping job from the main UI and verify that the Discovery phase runs, updates the new granular UI indicators, and correctly extracts products.

