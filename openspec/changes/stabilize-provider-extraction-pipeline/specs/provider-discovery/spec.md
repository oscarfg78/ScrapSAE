## MODIFIED Requirements

### Requirement: Use specified Product Detail URL for analysis
The system SHALL use the explicitly provided "Product Detail URL" (if available) as a candidate for the execution planner, delegating to the appropriate contributor instead of short-circuiting global discovery logic.

#### Scenario: Analyzing details with explicit URL
- **WHEN** a provider discovery is initiated and a Product Detail URL is provided
- **THEN** the execution planner injects this explicit URL as a candidate to the detail contributor, executing it independently of the catalog contributor.

#### Scenario: Analyzing details without explicit URL
- **WHEN** a provider discovery is initiated and no Product Detail URL is provided
- **THEN** the catalog contributor produces candidate URLs, which the execution planner then passes downstream to the detail contributor according to the augment policy.

### Requirement: Favorable URL Learning and Retention
The system SHALL identify and store product URLs that were favorably extracted during discovery and execution, learning these pathways to optimize future extraction tasks for the provider.

#### Scenario: Favorable extraction learning
- **WHEN** the extraction pipeline successfully extracts detail information from a candidate URL with high confidence
- **THEN** the system retains this URL pattern/record in the provider's learned knowledge base to improve future discovery reliability
