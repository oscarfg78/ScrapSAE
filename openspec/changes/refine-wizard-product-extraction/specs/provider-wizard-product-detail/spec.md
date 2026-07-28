## MODIFIED Requirements

### Requirement: Product Detail URL Input
The Provider Wizard UI SHALL include an input field to allow the user to specify a "Product Detail URL" during the provider configuration phase. When provided, the system SHALL perform exhaustive analysis on this page to extract all product fields and discover candidate list/catalog URLs similar to it.

#### Scenario: User provides a product detail URL
- **WHEN** the user is configuring a new provider in the wizard and inputs a valid product detail URL
- **THEN** the system analyzes the page, extracts all product fields (retaining complete selectors and confidence levels), and discovers similar candidate URLs to populate the catalog discovery pool.
