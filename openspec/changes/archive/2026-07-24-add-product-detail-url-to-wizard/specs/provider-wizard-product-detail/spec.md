## ADDED Requirements

### Requirement: Product Detail URL Input
The Provider Wizard UI SHALL include an input field to allow the user to specify a "Product Detail URL" during the provider configuration phase.

#### Scenario: User provides a product detail URL
- **WHEN** the user is configuring a new provider in the wizard
- **THEN** the user can input a valid URL pointing to a specific product detail page.

### Requirement: Fallback behavior for Product Detail URL
The "Product Detail URL" field SHALL be optional.

#### Scenario: User omits the product detail URL
- **WHEN** the user is configuring a new provider and leaves the product detail URL blank
- **THEN** the system proceeds with the discovery using only the catalog URL, relying on the first product found for detail analysis.
