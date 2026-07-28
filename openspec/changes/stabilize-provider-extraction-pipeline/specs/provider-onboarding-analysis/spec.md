## ADDED Requirements

### Requirement: Unified Onboarding Analysis
The Wizard MUST analyze the provider configuration without persisting any business data until the user explicitly confirms the setup.

#### Scenario: User provides a new store URL
- **WHEN** the user inputs a catalog or product detail URL in the Wizard
- **THEN** the system analyzes the target, identifies capabilities, and presents a proposed configuration without saving a permanent Provider record

### Requirement: Preview Quality Gate
The Wizard MUST present a final quality gate (Pass, PassWithWarnings, Fail) based on the analysis of the Demo session run before allowing the user to save.

#### Scenario: Successful Demo Run
- **WHEN** the demo run completes
- **THEN** the wizard shows a report view with the reconciled products, highlights missing fields, and requires explicit user confirmation to persist the provider configuration

### Requirement: Authentication Parameters Configuration
The Wizard MUST be capable of receiving and configuring additional parameters, such as authentication credentials or session tokens, necessary for accessing protected provider catalogs.

#### Scenario: User configures a provider requiring authentication
- **WHEN** the user inputs a URL for a provider that requires login
- **THEN** the wizard provides inputs for authentication parameters and includes them in the onboarding analysis and subsequent demo execution
