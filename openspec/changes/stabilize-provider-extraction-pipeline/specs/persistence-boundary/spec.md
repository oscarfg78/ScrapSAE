## ADDED Requirements

### Requirement: Late persistence
The system MUST NOT persist `Products` or `Providers` in the business database until the entire extraction pipeline finishes reconciliation and passes the quality gate.

#### Scenario: Extraction in production
- **WHEN** a production run is executed and successfully generates an `ExtractionRunReport`
- **THEN** the system commits the reconciled products to the database in an idempotent manner

### Requirement: Idempotent upsert
The system MUST perform upserts based on a deterministic identity cascade (not just an optionally empty SKU) to avoid collisions.

#### Scenario: Running extraction twice on the same catalog
- **WHEN** a catalog is scraped twice without changes
- **THEN** the system updates the existing records idempotently based on the stable identity without creating duplicates
