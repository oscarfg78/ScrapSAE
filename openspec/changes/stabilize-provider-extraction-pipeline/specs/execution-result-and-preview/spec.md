## ADDED Requirements

### Requirement: Canonical run report
The execution pipeline MUST produce a comprehensive `ExtractionRunReport` containing the request, plan, execution diagnostics, and reconciled products.

#### Scenario: Execution completes
- **WHEN** the extraction pipeline finishes processing
- **THEN** it generates a canonical report object independent of the persistence layer

### Requirement: Comprehensive UI preview
The Wizard MUST use the `ExtractionRunReport` to display a detailed preview, exposing missing fields, confidence scores, and errors.

#### Scenario: Rendering the wizard preview
- **WHEN** the Wizard receives the report
- **THEN** it renders all products, flags data gaps, and provides diagnostic logs for any failed contributors, serving as proof of task completion and allowing for ocular verification by the user
