## ADDED Requirements

### Requirement: Explicit Selector Schema
The system MUST use a typed `SelectorDescriptor` contract to represent DOM targets, distinguishing CSS, XPath, and attributes explicitly.

#### Scenario: Processing a configured selector
- **WHEN** a contributor attempts to locate an element using a configured selector
- **THEN** the system uses the exact type (CSS/XPath/Attribute) defined in the `SelectorDescriptor` without guessing or implicit string parsing

### Requirement: Selector Provenance
Every extracted field MUST record the `SelectorDescriptor` that produced the value.

#### Scenario: Merging extracted fields
- **WHEN** a field is extracted
- **THEN** its `ProductObservation` must include the selector and contributor that generated it, enabling tracing from the final report
