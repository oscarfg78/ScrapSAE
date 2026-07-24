## Why

Currently, when scraping products, some specifications like "source_url" and "supplier name" are being sent to the online store, which is undesirable. Additionally, the "brand" specification is sometimes captured with temporary or incorrect values during scraping, and we need a way to assign the brand based on the supplier the records were obtained from. This change allows setting a specific brand value per supplier and prevents sending internal/undesired specifications to the final store.

## What Changes

- Add capability to configure a "brand" override value for each supplier (proveedor).
- When preparing data to send to the online store (Flashly integration), filter out "source_url" specification so it is not sent.
- Filter out "supplier name" specification so it is not sent.
- When sending data to the online store, if the supplier has a configured brand override, replace the scraped "brand" specification value with the configured one.

## Capabilities

### New Capabilities
- `supplier-specs-mapping`: Allows mapping and filtering of scraped specifications based on supplier settings before sending them to the online store.

### Modified Capabilities

## Impact

- Database schema or entity for Supplier (Proveedor) to include the brand override field.
- The payload generation logic for the Flashly integration will be updated to filter specific properties and override the brand.
