## MODIFIED Requirements

### Requirement: Use specified Product Detail URL for analysis
The system SHALL use the explicitly provided "Product Detail URL" (if available) to analyze the product details (e.g., description, characteristics) instead of fetching the first product from the catalog list.

#### Scenario: Analyzing details with explicit URL
- **WHEN** a provider discovery is initiated and a Product Detail URL is provided
- **THEN** the scraping strategy uses this explicit URL to infer detail selectors and validate the structure, ignoring the detail page of the first item in the catalog.

#### Scenario: Analyzing details without explicit URL
- **WHEN** a provider discovery is initiated and no Product Detail URL is provided
- **THEN** the scraping strategy falls back to extracting the URL of the first product in the catalog and uses it to infer detail selectors.

## ADDED Requirements

### Requirement: El descubrimiento de URLs usa la misma lógica que el wizard
El sistema SHALL usar los mismos métodos de descubrimiento de URLs (`DiscoverRelatedProductUrlsAsync`) en la ejecución de producción que en el test del wizard. El descubrimiento SHALL basarse en los selectores del `SiteProfile` y en `StrategyType`, sin lógica hardcodeada por nombre de proveedor.

#### Scenario: Descubrimiento basado en estrategia del SiteProfile
- **WHEN** un `SiteProfile` con `StrategyType = "Generic"` y `Strategies = [Families]` inicia una ejecución
- **THEN** el sistema usa `FamiliesExtractionStrategy` para descubrir URLs de productos, tal como haría el wizard

#### Scenario: Descubrimiento para Shopify no usa browser
- **WHEN** un `SiteProfile` con `StrategyType = "Shopify"` inicia una ejecución
- **THEN** el sistema usa `ShopifyApiStrategy` para obtener productos via la API `/products.json`, sin Playwright
