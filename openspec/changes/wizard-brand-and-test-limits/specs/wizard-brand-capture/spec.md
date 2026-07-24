## ADDED Requirements

### Requirement: Capture Brand in Wizard
The system SHALL provide a mechanism in the first step of the configuration Wizard to capture the "Brand" (Marca) name associated with the supplier's products.

#### Scenario: User inputs brand name
- **WHEN** the user is configuring a new supplier site in the Wizard's initial step
- **THEN** they see an input field labeled "Marca (Brand)"
- **AND** the captured value is stored in the Wizard's state and subsequently assigned to the `SiteProfile` configuration under a mechanism that allows product mapping to retrieve it.
