## ADDED Requirements

### Requirement: Description Extraction
The system SHALL extract extended product descriptions from product pages, including HTML sections specifying descriptions or highlighted specifications (e.g. `product-description` or `tab-content-description`).

#### Scenario: Description section exists on product page
- **WHEN** a product page is scraped and it contains an extended description HTML block
- **THEN** the scraper SHALL extract the HTML content of the description block and include it in the data sent for AI processing or directly parse it into the product's Description field.

### Requirement: JSON Specification Output
The extracted description information SHALL be included in the final exported payload either in the `Description` field or embedded within the `Specifications` JSON as "Extended Description" or "Detalles", ensuring the information reaches the online store.

#### Scenario: Data processing produces final payload
- **WHEN** the system generates the product data payload for the online store or CSV export
- **THEN** the payload SHALL contain the extracted detailed description.
