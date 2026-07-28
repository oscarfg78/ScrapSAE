## 1. Core Contracts

- [x] 1.1 Implement `SelectorDescriptor` records explicitly separating CSS, XPath, and attributes.
- [x] 1.2 Create `ContributorDescriptor` and `ContributorResult` DTOs with standardized status enums (`NotApplicable`, `NoData`, `Partial`, `Success`, `RecoverableFailure`, `FatalFailure`).
- [x] 1.3 Design `ProductObservation` model to preserve field provenance (value, selector, contributor).
- [x] 1.4 Implement `ReconciledProduct` and the `ExtractionRunReport` structure.

## 2. Planners and Adapters

- [x] 2.1 Implement `ExecutionPlanner` capable of resolving policies (`fallback`, `augment`, `ensemble`).
- [x] 2.2 Create `LegacyAdapterContributor` wrapping the existing Playwright scraping logic to emit observations.
- [x] 2.3 Implement the identity resolution and reconciliation engine based on observation authority.

## 3. UI and Control Flow Updates

- [x] 3.1 Update Wizard ViewModels to use the new `ExtractionRunReport` instead of reading from staging database.
- [x] 3.2 Refactor Demo execution to explicitly set `persistencePolicy=none` and a 10-product limit.
- [x] 3.3 Add the explicit Quality Gate review step before persisting business data.
- [x] 3.4 Move Provider and Product persistence to the end of the production extraction pipeline (idempotent upsert).

## 4. Testing and Parity

- [x] 4.1 Write contract tests for the `ExecutionPlanner` and state transitions.
- [x] 4.2 Create shadow run fixtures comparing demo output with production output for parity validation.
