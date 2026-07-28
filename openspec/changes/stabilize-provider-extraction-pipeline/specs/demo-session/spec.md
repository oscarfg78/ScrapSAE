## ADDED Requirements

### Requirement: Real non-destructive demo
The Wizard Demo MUST run a real execution through the core pipeline without mutating the provider configuration or saving products to the business database.

#### Scenario: Running the demo
- **WHEN** the user triggers the demo execution
- **THEN** the system executes the plan with a user-defined product limit (defaulting to 10, specified in the Wizard) and `persistencePolicy=none`

### Requirement: Demo resource cleanup
Demo resources MUST be scoped to a `demoSessionId` and cleaned up appropriately.

#### Scenario: Demo session ends
- **WHEN** the demo completes or is cancelled
- **THEN** associated temporary state is marked for cleanup without affecting any existing business products
