## ADDED Requirements

### Requirement: ScrapeExecutionContext encapsula los parámetros de ejecución
El sistema SHALL exponer un tipo `ScrapeExecutionContext` (record inmutable en `ScrapSAE.Core.DTOs`) con las propiedades `IsHeadless`, `ManualLogin`, `KeepBrowser`, `ScreenshotFallback`, y `MaxProductsOverride` (nullable). El endpoint `api/scraping/run` SHALL construir este objeto desde los query params y pasarlo a `ScrapingRunner.RunForSiteAsync` en lugar de establecer environment variables.

#### Scenario: Endpoint construye el contexto correctamente
- **WHEN** el endpoint `POST /api/scraping/run/{siteId}` recibe query params `headless=true&manualLogin=false`
- **THEN** el sistema construye un `ScrapeExecutionContext { IsHeadless = true, ManualLogin = false }` y lo pasa al runner

#### Scenario: Ejecución concurrente no mezcla configuraciones
- **WHEN** dos solicitudes de scrape llegan simultáneamente con parámetros distintos (e.g., headless=true y headless=false)
- **THEN** cada ejecución usa su propio `ScrapeExecutionContext` sin interferencia entre ambas
