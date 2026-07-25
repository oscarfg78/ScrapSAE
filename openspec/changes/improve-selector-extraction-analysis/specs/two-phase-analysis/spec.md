## ADDED Requirements

### Requirement: Catalog Analysis Phase
The API SHALL perform a dedicated "Catalog Analysis" phase when given a base URL, to extract list containers, product cards, and candidate detail URLs.

#### Scenario: Extracting detail links from catalog
- **WHEN** the user provides a Catalog URL
- **THEN** the API analyzes the DOM to find product links and selects one representative link to be the "Product Detail URL".

### Requirement: Detail Analysis Phase
The API SHALL perform a dedicated "Detail Analysis" phase, using the resolved Product Detail URL to extract SKU, Name, Image, Price, and Characteristics.

#### Scenario: Extracting full properties from detail page
- **WHEN** the Detail Analysis phase runs on the product detail page
- **THEN** the AI correctly identifies the selectors for deep product properties.
