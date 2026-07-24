## Context

Currently, the ScrapSAE integration sends scraped products directly to Flashly. Some scraped specifications (like `source_url` and `supplier name`) are for internal or temporary use and should not be published on the online store. Furthermore, the scraped `brand` specification is sometimes captured with incorrect or temporary values. We need a way to override this value based on the supplier settings and prevent internal specifications from being transmitted to Flashly.

## Goals / Non-Goals

**Goals:**
- Add a configuration field to the `Provider` or `Supplier` entity to store a brand override value.
- Intercept the scraped product data before sending it to Flashly.
- Exclude `source_url` and `supplier name` from the product specifications list.
- Replace the `brand` specification value with the supplier's configured brand override value.

**Non-Goals:**
- Modifying how the scraper extracts the data (the scraping logic remains intact).
- Adding complex filtering rules based on regular expressions or multiple conditions (simple omission and replacement).

## Decisions

1. **Entity Update:** We will add a `BrandOverride` string property to the `Provider` entity in `ScrapSAE.Core`. This will require a database migration.
2. **Payload Modification Point:** The data transformation will happen in the integration service layer (likely where we map scraped data to the Flashly API payload). This prevents modifying core scraped data and isolates the logic to the Flashly integration boundary.
3. **Filtering Logic:** We will simply filter out `Specification` entries whose name (case-insensitive) matches "source_url" or "supplier name".
4. **Override Logic:** We will search for a `Specification` named "brand" (case-insensitive). If found, and if the associated `Provider` has a non-null, non-empty `BrandOverride`, we will update the specification's value.

## Risks / Trade-offs

- **Risk:** The names of specifications ("brand", "source_url", "supplier name") might change or have slight variations (e.g., "Supplier Name").
  - **Mitigation:** Use case-insensitive string comparisons. If names are prone to change, we might need a more robust configuration in the future, but hardcoded strings are sufficient for this initial requirement.
- **Risk:** Applying the brand override relies on the provider entity being available during the payload generation.
  - **Mitigation:** Ensure the provider information is fetched or passed along with the scraped product data before building the Flashly payload.
