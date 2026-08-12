# scraping-resource-optimization Specification

## ADDED Requirements

### Requirement: AI Scraping Toggle Option
The system SHALL provide a user-configurable option "Utilizar IA" prior to starting and during a scraping process.

#### Scenario: User enables or disables AI before scraping
- **WHEN** the user configures the scraping execution in the UI
- **THEN** the user can toggle the "Utilizar IA" option on or off.

### Requirement: AI Scraping Efficiency Monitoring and Alert
The system SHALL continuously evaluate if AI calls during scraping produce effective extracted data fields beyond baseline selectors. If AI extraction yields no additional fields over a specified threshold of consecutive items, the system SHALL display an alert dialog "No es necesario que se siga usando IA" to allow the user to disable AI or continue.

#### Scenario: AI extraction yields no benefits across consecutive items
- **WHEN** AI is enabled and fails to extract any new fields for 3 consecutive products
- **THEN** the system triggers a dialog informing the user "No es necesario que se siga usando IA" with options to disable AI or keep it active.

#### Scenario: User chooses to disable AI from alert dialog
- **WHEN** the user selects to disable AI in the alert dialog
- **THEN** the system updates the running scraping process configuration to disable AI immediately without stopping the scraping process.

### Requirement: Immediate Per-Product Record Persistence
The system SHALL save each extracted product record into the local persistence layer immediately after its extraction completes, rather than waiting for the entire scraping job to finish.

#### Scenario: Product extraction completes
- **WHEN** a single product record extraction process finishes
- **THEN** the system immediately persists the product to the local database and updates the UI progress.

### Requirement: Exclusion of Source URL and Supplier Name on Store Export
The system SHALL remove `source_url` and `supplier name` attributes from product payload metadata when exporting products to the online store.

#### Scenario: Exporting product to store
- **WHEN** the user triggers an export of product records to the online store (Flashly or CSV)
- **THEN** the system strips `source_url` and `supplier name` keys from the exported product payload.
