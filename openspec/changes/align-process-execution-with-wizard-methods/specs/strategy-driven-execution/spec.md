## ADDED Requirements

### Requirement: StrategyOrchestrator como ruta principal de ejecución
El sistema SHALL usar `SiteProfile.StrategyType` y `SiteProfile.Strategies[]` como la única fuente de verdad para seleccionar cómo ejecutar el scraping. `ScrapingRunner.RunForSiteAsync` SHALL delegar al `StrategyOrchestrator` para sites con `StrategyType == "Generic"` (o nulo), iterando las estrategias configuradas en `Strategies[]` en orden de prioridad. La selección de estrategia NO SHALL depender de environment variables ni del nombre del sitio.

#### Scenario: Site configurado con estrategia Direct usa DirectExtractionStrategy
- **WHEN** un `SiteProfile` tiene `StrategyType = "Generic"` y `Strategies = [{ StrategyName = "Direct", Priority = 1 }]`
- **THEN** el runner ejecuta `DirectExtractionStrategy` como primera y única estrategia

#### Scenario: Site configurado con múltiples estrategias prueba en orden
- **WHEN** un `SiteProfile` tiene `Strategies = [Direct(p=1), List(p=2), Families(p=3)]`
- **THEN** el runner prueba `Direct` primero; si no extrae productos, prueba `List`; si tampoco, prueba `Families`

#### Scenario: Site Shopify usa ShopifyApiStrategy directamente
- **WHEN** un `SiteProfile` tiene `StrategyType = "Shopify"`
- **THEN** el runner usa `ShopifyApiStrategy` sin pasar por el `StrategyOrchestrator` de estrategias genéricas

#### Scenario: Site sin Strategies configuradas usa defaults del Orchestrator
- **WHEN** un `SiteProfile` tiene `Strategies = []` o nulo
- **THEN** el `StrategyOrchestrator` aplica el orden por defecto: Direct → List → Families

### Requirement: La ejecución en producción es consistente con el resultado del wizard
El resultado de ejecutar el scraping en producción para un `SiteProfile` SHALL producir los mismos campos de producto (SKU, nombre, imagen, precio, características) que la prueba del wizard produjo al crear dicho perfil, dado el mismo estado del sitio web.

#### Scenario: Selectores del wizard se usan en ejecución producción
- **WHEN** el wizard configura `productCardSelector = ".product-item"` en un `SiteProfile` y luego se ejecuta el scraping normal para ese perfil
- **THEN** `DirectExtractionStrategy` (u otra estrategia aplicable) usa `site.Selectors["productCard"]` = `".product-item"` para encontrar productos
