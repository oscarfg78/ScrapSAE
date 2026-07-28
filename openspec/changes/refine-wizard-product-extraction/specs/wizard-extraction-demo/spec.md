## ADDED Requirements

### Requirement: Demo Mode Extraction Execution
The system SHALL execute the extraction test in the wizard in a non-destructive Demo mode, without persisting the results in the business state (staging or profile saving) unless explicitly requested during a subsequent non-demo save.

#### Scenario: User runs a test extraction
- **WHEN** the user initiates the test extraction in the wizard
- **THEN** the system executes the extraction using the core extraction logic with the `Demo` mode flag and returns a self-contained report of the products found, skipping database insertion.

### Requirement: Test Extraction Budget Configuration
The Provider Wizard UI SHALL include an input to allow the user to explicitly define the maximum number of products to extract during the test (budget).

#### Scenario: User sets maximum products for test
- **WHEN** the user configures the Test step in the wizard
- **THEN** they can input a number (e.g., 5, 10) which limits the extraction run to that exact maximum amount of products.
