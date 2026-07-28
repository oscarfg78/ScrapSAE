## ADDED Requirements

### Requirement: Independent Extraction Contributors
Every extraction capability MUST implement a `ContributorDescriptor` and operate independently without mutating global state or cancelling other contributors prematurely.

#### Scenario: Multiple contributors enabled
- **WHEN** multiple contributors are enabled (e.g., List, Shopify API)
- **THEN** each executes independently and returns a `ContributorResult` with observations, without short-circuiting the entire run based on one result

### Requirement: Standardized Contributor Statuses
A contributor MUST return a uniform state (`NotApplicable`, `NoData`, `Partial`, `Success`, `RecoverableFailure`, `FatalFailure`).

#### Scenario: Contributor fails gracefully
- **WHEN** a contributor hits a timeout
- **THEN** it returns `RecoverableFailure` with diagnostics, and the system continues execution according to the planner's policy
