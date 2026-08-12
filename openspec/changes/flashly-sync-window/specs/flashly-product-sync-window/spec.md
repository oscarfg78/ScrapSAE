## ADDED Requirements

### Requirement: Independent Flashly Sync Window
The system SHALL provide a dedicated window to sync scraped products to Flashly, accessible from Step 4.

#### Scenario: Open Flashly Sync Window
- **WHEN** the user selects the option to sync to Flashly from Step 4
- **THEN** a new, independent window opens displaying the products available for sync

### Requirement: JSON Schema Mapping
The system SHALL map the product data to match the Flashly `/api/v1/products/sync` JSON Schema precisely.

#### Scenario: Verify payload mapping
- **WHEN** the sync payload is generated
- **THEN** it must contain `source_sku`, `name`, `description`, `purchase_price` (numeric, >= 0), `currency` (3 chars), `categories` (array), `product_url` (uri/null), `image_urls` (array of uris), `supplier_name` (string/null), and `specifications_json` (string/null)

### Requirement: Sync Data Validation
The system SHALL validate the product data using the existing validation rules before allowing sync.

#### Scenario: Validation failure prevents sync
- **WHEN** a product is missing a required field or has invalid data (e.g., negative price)
- **THEN** the system prevents syncing that product and displays an error message

### Requirement: Local Sync State Persistence
The system SHALL record the outcome of the Flashly sync operation in the local database.

#### Scenario: Successful sync updates database
- **WHEN** a product is successfully sent and acknowledged by the Flashly API
- **THEN** the system updates the local database to reflect the successful sync status for that product
