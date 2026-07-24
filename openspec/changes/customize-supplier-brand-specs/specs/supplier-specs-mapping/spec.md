## ADDED Requirements

### Requirement: Supplier brand override
The system SHALL support configuring a specific "brand" string for each supplier.

#### Scenario: Supplier has a brand configured
- **WHEN** configuring a supplier via the ScrapSAE API or database
- **THEN** the system persists the provided brand value associated with that supplier

### Requirement: Exclude specific specifications from online store payload
The system MUST NOT include the "source_url" or "supplier name" specifications in the product data sent to the Flashly integration or online store.

#### Scenario: Formatting scraped product for Flashly
- **WHEN** the integration service maps the scraped product specs to the target payload
- **THEN** the "source_url" specification is omitted from the resulting payload
- **THEN** the "supplier name" specification is omitted from the resulting payload

### Requirement: Apply supplier brand override to scraped product
The system SHALL replace any scraped "brand" specification value with the supplier's configured brand before sending it to the online store.

#### Scenario: Supplier brand override is applied
- **WHEN** the scraped product is mapped for the Flashly integration
- **THEN** if the supplier has a configured brand override, the product's "brand" specification is set to that value
- **THEN** if the supplier does NOT have a configured brand override, the product's "brand" specification retains its original scraped value (or is omitted if that's the default behavior)
