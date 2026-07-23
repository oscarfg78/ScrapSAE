## ADDED Requirements

### Requirement: Native Shopify Integration API Support
The system SHALL intercept or provide an integration point specifically for Shopify sites to bypass HTML scraping when possible.

#### Scenario: Fallback to products.json
- **WHEN** the site is identified as Shopify
- **THEN** the system SHALL attempt to query `<url>/products.json` or equivalent pagination endpoint to fetch the JSON schema of products directly, falling back to HTML parsing if restricted (403/429).
