## ADDED Requirements

### Requirement: Mandatory Scraping Test Validation
The provider discovery test step SHALL require that at least one product with meaningful data (e.g. Title, SKU, or Price) is extracted before considering the discovery configuration valid.

#### Scenario: Successful validation during discovery test
- **WHEN** the scraping test step is executed with the inferred selectors
- **THEN** the extraction engine must return at least one product with non-null critical properties (Title, SKU) to pass the test.

#### Scenario: Failing validation during discovery test
- **WHEN** the scraping test step yields zero products or products with only null fields
- **THEN** the discovery is marked as failed, triggering the dynamic selector combinator to self-heal.
