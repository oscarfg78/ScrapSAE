## 1. Entity and Database Updates

- [x] 1.1 Add `BrandOverride` property to `Provider` (Supplier) entity in `ScrapSAE.Core`.
- [x] 1.2 Create and apply Entity Framework Core database migration for the new `BrandOverride` field (Using SQL script since EF is not used).
- [x] 1.3 Verify database migration applied successfully.

## 2. API and DTO Updates

- [x] 2.1 Update `ProviderDto` or equivalent response models to include `BrandOverride` (Uses `SiteProfile` entity directly).
- [x] 2.2 Update Provider creation/update requests and handlers to accept `BrandOverride`.

## 3. Flashly Integration Update

- [x] 3.1 Locate the payload generation logic for Flashly integration (`FlashlyProductMapper.ToFlashlyDto`).
- [x] 3.2 Ensure the `Provider` entity or its `BrandOverride` configuration is available during mapping (Assigned `Site` to products in `Worker.cs`).
- [x] 3.3 Implement filtering logic to omit any `Specification` named "source_url".
- [x] 3.4 Implement filtering logic to omit any `Specification` named "supplier name".
- [x] 3.5 Implement override logic to find the "brand" specification and replace its value with `BrandOverride` (or add it if missing), also updating the `SupplierName` DTO property.

## 4. Testing and Verification

- [x] 4.1 Test Provider creation/update through the API to ensure `BrandOverride` is saved.
- [x] 4.2 Run a test scraping/integration task and verify that "source_url" and "supplier name" are omitted from the sent payload.
- [x] 4.3 Verify that the "brand" specification is correctly overridden based on the provider's `BrandOverride` value.
