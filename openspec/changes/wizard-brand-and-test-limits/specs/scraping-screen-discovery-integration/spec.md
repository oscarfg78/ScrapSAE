## ADDED Requirements

### Requirement: Reusable Discovery Logic in Main Scraping
The system SHALL execute the advanced discovery and testing logic originally built for the Wizard within the main Scraping execution cycle (e.g., when the user clicks "Iniciar" in the main Scraping UI). This logic must complement the standard URL processing.

#### Scenario: Running main scraping job
- **WHEN** a user initiates a scraping job for a supplier
- **THEN** the system invokes the discovery routines to find product URLs dynamically, adding them to the processing queue
- **AND** the standard scraping logic processes both pre-configured URLs and newly discovered URLs without failing on suppliers that don't need discovery.

### Requirement: Scraping UI Progress Feedback
The main Scraping UI SHALL display granular states corresponding to the discovery phases.

#### Scenario: Viewing real-time progress
- **WHEN** the scraping job is running
- **THEN** the UI displays the current phase (e.g., "Explorando paginación", "Extrayendo productos") visually, utilizing a progress bar or timeline indicator, distinct from the raw console output.
