## ADDED Requirements

### Requirement: DOM Pruning for AI Analyzer
The system SHALL support pruning of the DOM tree before sending it to OpenAI to minimize context size and improve structured data extraction.

#### Scenario: Removing noise
- **WHEN** the HTML document is downloaded for analysis
- **THEN** the system SHALL remove invisible elements, scripts, styles, and empty structural containers before passing it to the language model.
