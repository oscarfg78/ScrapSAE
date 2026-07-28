## ADDED Requirements

### Requirement: Execution Logs Collection
The scraping system SHALL collect step-by-step execution logs during the evaluation of any strategy.

#### Scenario: Selector fails to find elements
- **WHEN** a strategy attempts to find elements using a specific CSS/XPath selector and finds 0
- **THEN** it records a log step detailing the selector used and the resulting count (0).

### Requirement: AI Interaction Logging
The system SHALL record exact prompts and responses when using AI for extraction.

#### Scenario: AI extracts a product
- **WHEN** the `AIExtractionStrategy` or similar AI method is invoked
- **THEN** it records the prompt sent to the LLM and the raw JSON response received.

### Requirement: API Exposure
The API SHALL include the collected execution logs in the testing response.

#### Scenario: Wizard requests a test scrape
- **WHEN** the user runs a test scrape from the Wizard
- **THEN** the API response includes a populated `ExecutionLogs` array detailing the steps taken.
