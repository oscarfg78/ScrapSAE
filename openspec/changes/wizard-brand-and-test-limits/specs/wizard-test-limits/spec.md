## ADDED Requirements

### Requirement: Test Limit Configuration
The system SHALL enforce a maximum of 10 products when running the test extraction phase in the Wizard, while defaulting to 120 products for actual scraping jobs.

#### Scenario: Running test extraction
- **WHEN** the user initiates the test phase in the Wizard
- **THEN** the scraper processes a maximum of 10 products

#### Scenario: Saving supplier profile
- **WHEN** the user successfully completes the Wizard and saves the configuration
- **THEN** the persisted `SiteProfile` sets its processing limit (e.g., `MaxProductsPerJob` or equivalent configuration) to 120 by default.
