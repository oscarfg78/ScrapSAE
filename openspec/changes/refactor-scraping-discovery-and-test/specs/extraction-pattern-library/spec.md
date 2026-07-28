## ADDED Requirements

### Requirement: Shared Extraction Patterns
Extraction strategies (List, Direct, Family) SHALL utilize a centralized pattern library (`SelectorCombinator`, `ExtractionValidator`) for resolving dual selectors (CSS/XPath) and validating extracted data.

#### Scenario: Code reuse across strategies
- **WHEN** any extraction strategy needs to resolve a selector
- **THEN** it delegates to the pattern library which handles permutations, waits, and validation consistently.
