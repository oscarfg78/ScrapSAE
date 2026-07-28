## ADDED Requirements

### Requirement: Production and Demo Parity
The core extraction logic MUST execute exactly the same code path for both Demo and Production runs, differing only by budget limit and persistence policy.

#### Scenario: Validating parity
- **WHEN** an extraction is run in Demo mode and Production mode against the exact same snapshot
- **THEN** the generated `ExtractionRunReport` yields identical reconciled products and diagnostic events, except for timestamps and limits

### Requirement: Metrics and Observability
Every contributor MUST log execution metrics, candidates found, timeouts, and state transitions.

#### Scenario: Contributor times out
- **WHEN** a contributor exceeds its allotted budget
- **THEN** it logs a metric for the timeout, includes it in the `ExtractionRunReport`, and yields a `RecoverableFailure`
