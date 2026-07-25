## ADDED Requirements

### Requirement: DOM Pre-Analysis Heuristic
The `PageAnalysisService` SHALL apply a pre-analysis step using AngleSharp to parse the raw HTML and extract the most relevant DOM hierarchy (e.g., lists, grids, tables) before sending data to the AI model.

#### Scenario: Complex HTML is simplified
- **WHEN** the service receives a large, complex HTML page
- **THEN** it parses the DOM, removes non-structural elements (scripts, styles, hidden nodes), and produces a simplified representation (DOM Skeleton) focused on product containers.

### Requirement: AI Selector Generation with dual locators
The AI model SHALL generate dual locators (`css` and `xpath`) for each required property based on the simplified DOM skeleton, avoiding brittle paths.

#### Scenario: AI generates robust selectors
- **WHEN** the simplified DOM is analyzed
- **THEN** the AI returns a robust CSS selector (using classes/IDs) and a robust relative XPath.
