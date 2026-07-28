## Why

The current extraction system suffers from overlapping responsibilities, hidden side-effects, and unpredictable fallback behaviors. We need to stabilize the architecture by separating control (wizard), execution planning, independent extraction pathways (contributors), and persistence, making sure that failures are never silent and configurations are explicit and testable.

## What Changes

- Introduce a unified Wizard control plane that serves for onboarding, configuration, real demonstration, and approval.
- Isolate execution mechanisms into independent "contributors" (e.g., Direct, List, Families, Shopify, AI fallback) that do not interfere with each other.
- Implement an explicit execution planner supporting `fallback`, `augment`, and `ensemble` policies.
- Defer business data persistence until after explicit reconciliation and quality gates are passed.
- Standardize a canonical Execution Run Report that provides complete observability and provenance per field.
- **BREAKING**: Re-architect how state and results are passed between scraping strategies; remove the implicit substitution of workflows based on discovery outcomes.
- **BREAKING**: Change demo session to run without business persistence (non-destructive) and restrict to a user-defined product limit (defaulting to 10).
- Extend the Wizard configuration to support authentication parameters and visual verification of the extraction report.
- Enable learning and retention of favorably extracted URLs to optimize future runs.
## Capabilities

### New Capabilities
- `provider-onboarding-analysis`: Input, catalog/detail analysis, platform detection and human review.
- `selector-contract`: Versioned selector schema and legacy adapters.
- `extraction-contributors`: Descriptors, states, and common contract for independent pathways.
- `execution-planning`: DAG execution, composition policies, budgeting, and isolation.
- `demo-session`: Real demo execution, non-destructive, with a maximum of 10 products.
- `product-observation-and-reconciliation`: Provenance, identity, merge strategies, and conflict resolution.
- `execution-result-and-preview`: Canonical reporting and comprehensive preview presentation.
- `persistence-boundary`: Late persistence, idempotency, and versioning.
- `pathway-adapters`: Adapters for Generic, Direct, List, Families, Shopify, legacy, and extension mechanisms.
- `parity-observability-and-testing`: Metrics, fixtures, contract tests, and parity validation.

### Modified Capabilities
- `provider-wizard-product-detail`: Updating to use the unified execution report and ensure non-destructive demo integration.
- `provider-discovery`: Adjusting discovery to act purely as a candidate provider (contributor) without altering the planner's overall execution path.

## Impact

- **ScrapSAE.Infrastructure**: Major refactoring of scraping runner, strategies, and orchestrator to adhere to the new planner/contributor model.
- **ScrapSAE.Core (DTOs)**: Contracts for selector schemas, extraction envelopes, observations, and run reports will be overhauled.
- **ScrapSAE.Desktop (Wizard)**: ViewModel, XAML, and flow will be rebuilt to accommodate the new 6-step explicit gate flow (Identity, Analysis, Configuration, Demo, Review, Confirmation).
- **Tests**: Harnesses will need to be rewritten to cover isolated contributor tests and parity between demo and production.
