## ADDED Requirements

### Requirement: Integration of Details Testing in Wizard
The Provider Wizard's Test step SHALL execute a detailed extraction test when a Product Detail URL was provided or successfully discovered as fallback.

#### Scenario: Test Step runs with Detail URL
- **WHEN** the user navigates to the "Test" step in the wizard
- **THEN** the wizard initiates a test that spans both catalog and detail extraction phases.
