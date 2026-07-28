## ADDED Requirements

### Requirement: Legacy execution adapter
The system MUST encapsulate the existing Playwright scraping logic within a `LegacyAdapterContributor` that honors the new `ContributorDescriptor` and `ContributorResult` contracts.

#### Scenario: Falling back to legacy Playwright
- **WHEN** native contributors fail and the fallback policy specifies the legacy adapter
- **THEN** the planner invokes the legacy adapter, which runs the old logic internally and translates the output into `ProductObservation` items

### Requirement: Generic pathway adapters
The system MUST provide dedicated adapters for standard capabilities (Generic, Direct, List, Families, Shopify, Extension) to operate as independent contributors.

#### Scenario: Executing the List contributor
- **WHEN** the List contributor is scheduled by the planner
- **THEN** it executes only the list-gathering logic and returns candidate URLs or partial observations without attempting to extract detail fields globally
