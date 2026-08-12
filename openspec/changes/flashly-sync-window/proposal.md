## Why

The current process for sending products to Flashly needs a new dedicated window from Step 4. This new window must strictly follow a specific JSON schema to synchronize products correctly to Flashly, mapping our internal data to the required structure (`/api/v1/products/sync`). This solves the need to have a specific, validated payload format sent to the Flashly integration.

## What Changes

- Creation of a new, independent window for sending information to Flashly from Step 4.
- Mapping of ScrapSAE product fields to the Flashly schema: `source_sku`, `name`, `description`, `purchase_price`, `currency`, `categories`, `product_url`, `image_urls`, `supplier_name`, and `specifications_json`.
- Reuse of existing validations from the current Flashly sync window.
- Saving the mapped information into our own database tables to track the sync state.
- Separation of concerns: the code for this new window will be independent but utilize existing components/validations as a baseline.

## Capabilities

### New Capabilities
- `flashly-product-sync-window`: A new UI window launched from Step 4 that maps, validates, and sends products to Flashly according to the new JSON schema, and records the sent data in the local database.

### Modified Capabilities
- 

## Impact

- **UI**: A new view and viewmodel for the Flashly Sync Window.
- **Integration**: The `/api/v1/products/sync` payload format will be implemented and validated before sending.
- **Database**: Local tables will be updated to store the mapped information sent to Flashly.
