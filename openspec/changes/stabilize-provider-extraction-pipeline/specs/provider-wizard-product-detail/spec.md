## MODIFIED Requirements

### Requirement: Product Detail URL Input
The Provider Wizard UI SHALL include an input field to allow the user to specify a "Product Detail URL" during the provider configuration phase, and MUST pass this to the unified analysis engine.

#### Scenario: User provides a product detail URL
- **WHEN** the user is configuring a new provider in the wizard
- **THEN** the user can input a valid URL pointing to a specific product detail page, which becomes an input for the `provider-onboarding-analysis` capability.

### Requirement: Fallback behavior for Product Detail URL
The "Product Detail URL" field SHALL be optional.

#### Scenario: User omits the product detail URL
- **WHEN** the user is configuring a new provider and leaves the product detail URL blank
- **THEN** the system discovers candidates using the catalog and uses the planner to automatically select an inferred detail URL for analysis.
