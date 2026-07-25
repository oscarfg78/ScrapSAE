## MODIFIED Requirements

### Requirement: Product Detail URL Input
The Provider Wizard UI SHALL include an input field to allow the user to specify a "Product Detail URL" during the provider configuration phase.

#### Scenario: User provides a product detail URL
- **WHEN** the user is configuring a new provider in the wizard
- **THEN** the user can input a valid URL pointing to a specific product detail page.

### Requirement: Fallback behavior for Product Detail URL
The "Product Detail URL" field SHALL be optional.

#### Scenario: User omits the product detail URL
- **WHEN** the user is configuring a new provider and leaves the product detail URL blank
- **THEN** the system proceeds with the discovery using only the catalog URL, relying on the first product found for detail analysis.

## ADDED Requirements

### Requirement: El SiteProfile del wizard es la fuente de verdad para ejecución
El `SiteProfile` generado al finalizar el wizard (incluyendo `StrategyType`, `Strategies[]`, `Selectors`, y `SecondarySelectors`) SHALL ser utilizado sin modificaciones por la ejecución de producción. El sistema de producción SHALL respetar y usar estos valores como configurados por el wizard, sin ignorar ni sobreescribir la estrategia seleccionada.

#### Scenario: Ejecución producción usa la estrategia configurada por el wizard
- **WHEN** el wizard configura un `SiteProfile` con `StrategyType = "Generic"` y `Strategies = [List, Families]`
- **THEN** la ejecución de producción de ese sitio usa `ListExtractionStrategy` como primera estrategia y `FamiliesExtractionStrategy` como fallback

#### Scenario: Ejecución producción usa los selectores configurados por el wizard
- **WHEN** el wizard configura selectores específicos para un proveedor (e.g., `productCard`, `sku`, `name`, `image`)
- **THEN** la ejecución de producción aplica exactamente esos selectores para extraer datos de productos
