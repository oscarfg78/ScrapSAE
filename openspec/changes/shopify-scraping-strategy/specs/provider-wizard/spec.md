## ADDED Requirements

### Requirement: Discovery of Platform specifics
The Wizard discovery process SHALL adapt its AI analysis strategy by detecting platform-specific traits before doing raw HTML analysis.

#### Scenario: Shopify Provider configuration
- **WHEN** the user provides a Shopify-powered URL to the Wizard
- **THEN** the system SHALL detect it's Shopify and configure the new provider to use the Shopify strategy, retrieving data natively or structuring semantic extraction properly.
