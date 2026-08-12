## 1. Domain Models and Mapping

- [x] 1.1 Create the `FlashlyProductSyncPayload` and related classes representing the required JSON schema.
- [x] 1.2 Implement a mapper/adapter that converts existing ScrapSAE product entities into the new `FlashlyProductSyncPayload` format.

## 2. Validation Logic Extraction

- [x] 2.1 Extract existing Flashly product validation rules into a reusable service or extension class to avoid duplication.
- [x] 2.2 Verify that validation rules cover all required fields in the new JSON schema (e.g., negative prices, empty SKUs).

## 3. UI and ViewModel Implementation

- [x] 3.1 Create `FlashlySyncWindow.xaml` and its code-behind.
- [x] 3.2 Create `FlashlySyncViewModel.cs` to handle the window's logic.
- [x] 3.3 Implement data binding in the window to display products eligible for sync, their validation status, and sync progress.

## 4. Integration and Sync Logic

- [x] 4.1 Update `Step4ViewModel` or equivalent to provide a command/button to launch `FlashlySyncWindow`.
- [x] 4.2 Implement the API call logic to POST to `/api/v1/products/sync` using the mapped JSON schema.
- [x] 4.3 Update local database (e.g., SQLite via EF Core) to record sync status upon success or failure.
