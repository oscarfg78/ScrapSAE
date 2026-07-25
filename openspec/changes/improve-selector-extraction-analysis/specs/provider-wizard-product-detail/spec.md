## MODIFIED Requirements

### Requirement: Fallback behavior for Product Detail URL
The "Product Detail URL" field SHALL be optional, but the system MUST actively try to discover one if it is omitted.

#### Scenario: User omits the product detail URL
- **WHEN** the user is configuring a new provider and leaves the product detail URL blank
- **THEN** the system performs Phase 1 analysis on the catalog URL to extract a valid product detail URL, and uses it automatically for Phase 2 analysis.
- **AND THEN** the wizard UI displays the automatically discovered detail URL for confirmation.
