## ADDED Requirements

### Requirement: Raw observations retention
The system MUST retain raw `ProductObservation` objects before reconciling them into a final product representation.

#### Scenario: Contributor yields a candidate
- **WHEN** a contributor extracts field data
- **THEN** the system stores the raw value, the normalizer used, and the provenance (selector/contributor) in an observation

### Requirement: Explicit reconciliation rules
The system MUST reconcile observations into a `ReconciledProduct` using deterministic authority and confidence rules.

#### Scenario: Conflicting values for a field
- **WHEN** two contributors return different valid values for the same field
- **THEN** the system selects the value according to predefined contributor authority and flags a conflict if authority is equal
