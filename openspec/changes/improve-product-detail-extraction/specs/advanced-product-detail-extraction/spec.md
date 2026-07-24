## ADDED Requirements

### Requirement: Complex Detail Extraction
The extraction strategy SHALL successfully parse complex nested HTML descriptions, such as `tab-content-description`, into a readable text format or a structured list (JSON string).

#### Scenario: Product with nested HTML in description
- **WHEN** the product description is inside deep nested elements
- **THEN** the system iterates through the child nodes and extracts relevant details, avoiding truncated text.
