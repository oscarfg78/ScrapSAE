## Context

Currently, the ScrapSAE desktop application sends scraped products to Flashly. We need a new, independent window from "step 4" specifically tailored for syncing products using a strict JSON schema (`/api/v1/products/sync`). This requires mapping existing ScrapSAE data models to the expected `source_sku`, `name`, `description`, `purchase_price`, `currency`, `categories`, `product_url`, `image_urls`, `supplier_name`, and `specifications_json` fields.

## Goals / Non-Goals

**Goals:**
- Implement a standalone WPF Window/ViewModel triggered from Step 4.
- Reuse existing product validations from the current Flashly sending logic to avoid duplicating rules.
- Transform internal data into the strict JSON schema required by Flashly's sync endpoint.
- Persist the synchronization state (e.g., success, error, last synced) back into the local database (likely `FlashlyProductInfo` or similar).

**Non-Goals:**
- Completely rewriting the step 4 wizard.
- Changing how products are initially scraped or stored.
- Removing the old sending method immediately (this new window will be parallel for now or explicitly launched).

## Decisions

- **Independent UI/ViewModel:** We will create `FlashlySyncWindow` and `FlashlySyncViewModel` to maintain separation of concerns, even though we will reuse validation logic.
- **Validation Reuse:** We will extract or reference the existing validation methods (e.g., ensuring price > 0, required fields are present) so both the old and new processes stay aligned.
- **Payload Mapping:** A new DTO or mapper class will be created to ensure the payload exactly matches the JSON Schema provided by the user.

## Risks / Trade-offs

- **Risk:** Duplication of validation logic if not properly abstracted.
  - **Mitigation:** Carefully extract existing validation into a shared service or utility class before implementing the new window.
- **Risk:** Database schema mismatch if we need to store new fields for the sync state.
  - **Mitigation:** Ensure we only add necessary columns (like `FlashlySyncStatus`) and use Entity Framework migrations if needed.
