# provider-wizard-enhancements Specification

## ADDED Requirements

### Requirement: Baseline Supplier Selection in Provider Wizard
The Provider Wizard SHALL allow the user to select an existing supplier (e.g. Festo) as a baseline template when creating or editing a provider configuration.

#### Scenario: User selects a baseline supplier
- **WHEN** the user is configuring a new provider in the wizard
- **THEN** the user can choose an existing supplier from a dropdown list to load its known selectors as a baseline.

### Requirement: Pre-Testing Selectors from Baseline Supplier
The Provider Wizard SHALL execute a pre-test using the baseline supplier's selectors against the target URL DOM before initiating any AI GPT call. If the baseline selectors extract key fields successfully, the wizard SHALL offer to adopt the selectors without consuming AI tokens.

#### Scenario: Pre-test succeeds with baseline selectors
- **WHEN** the target page has an identical HTML structure to the baseline supplier and baseline selectors successfully extract title, price, or SKU
- **THEN** the wizard populates the selectors directly and informs the user that AI analysis is not required.

#### Scenario: Pre-test partially fails or fails with baseline selectors
- **WHEN** baseline selectors fail to extract all required fields from the target page
- **THEN** the wizard proceeds to hybrid AI analysis sending both the baseline selectors and DOM to GPT.

### Requirement: Hybrid AI Analysis with Baseline Context
When performing AI analysis for a provider configuration, the system SHALL send the existing baseline selectors along with the cleaned DOM to GPT so that GPT infers only missing or modified selectors or XPaths.

#### Scenario: Hybrid AI analysis execution
- **WHEN** the wizard initiates AI analysis with a selected baseline provider
- **THEN** the system constructs a prompt containing the baseline selectors and cleaned target DOM, and GPT returns missing or corrected selectors/XPaths.

### Requirement: Pre-Flight Mitigation for GPT Analysis
The system SHALL validate page accessibility (HTTP 404/connection errors) and sanitize/trim DOM payload size before invoking GPT.

#### Scenario: Page returns 404 or connection error
- **WHEN** the target URL fails pre-flight page fetch with 404 or network error
- **THEN** the system halts the GPT call and alerts the user with a descriptive error message.

#### Scenario: DOM payload exceeds token limit
- **WHEN** the target page HTML is too large
- **THEN** the system strips scripts, styles, SVG tags, and redundant whitespace before sending to GPT.

### Requirement: GPT Response History Retention
The Provider Wizard SHALL retain the raw response and structured output from GPT analysis calls to serve as historical context for subsequent steps or retries.

#### Scenario: GPT analysis completes or retries
- **WHEN** GPT returns a response or a retry is triggered
- **THEN** the system saves the response object in wizard context for use in subsequent analysis or troubleshooting steps.
