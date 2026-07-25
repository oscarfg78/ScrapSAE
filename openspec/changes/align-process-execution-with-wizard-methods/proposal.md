## Why

La ejecución del proceso de scraping en producción no está alineada con los métodos y técnicas que funcionan en el wizard. El wizard tiene un flujo claro y probado (análisis IA → selectores → `SiteProfile` con `StrategyType` + `Strategies[]` → `RunScrapingAsync`) que consistentemente extrae productos con éxito. Sin embargo, `ScrapingRunner` y `PlaywrightScrapingService.ScrapeAsync` tienen rutas de ejecución divergentes, lógica hardcodeada específica de proveedores, y usa environment variables como mecanismo de configuración en tiempo de ejecución (frágil y no thread-safe). Necesitamos unificar la capa de ejecución para que respete lo que el wizard ya sabe que funciona: el `SiteProfile.StrategyType` + `SiteProfile.Strategies[]` como fuente de verdad.

## What Changes

- Establecer `SiteProfile.StrategyType` y `SiteProfile.Strategies[]` como la **única fuente de verdad** para seleccionar el modo de ejecución, eliminando la dependencia en environment variables de runtime y lógica hardcodeada por nombre de proveedor (e.g., "Festo").
- Crear un `ScrapeExecutionContext` (objeto de parámetros tipado) que reemplace el paso de configuración por environment variables (`SCRAPSAE_MODE`, `SCRAPSAE_HEADLESS`, etc.) en el endpoint `api/scraping/run`.
- Hacer que `ScrapingRunner.RunForSiteAsync` respete `SiteProfile.StrategyType` para seleccionar la estrategia correcta (Direct, List, Families, Shopify) a través del `StrategyOrchestrator` ya existente.
- Crear un adaptador/método `ExecuteWithWizardStyleAsync` en `ScrapingRunner` que siga exactamente el mismo flujo que el wizard: navegar, extraer con los selectores del `SiteProfile`, aplicar las `Strategies[]` en orden de prioridad.
- Unificar la ruta de "modo families" para que use el `StrategyOrchestrator` en lugar de la lógica inline específica de Festo.
- Documentar las capas de ejecución con comentarios de arquitectura para mantener claridad de responsabilidades.

## Capabilities

### New Capabilities
- `scrape-execution-context`: Objeto tipado `ScrapeExecutionContext` que encapsula los parámetros de configuración de una ejecución (headless, manualLogin, mode) en lugar de environment variables.
- `strategy-driven-execution`: Método de ejecución que usa `SiteProfile.Strategies[]` y `StrategyOrchestrator` como ruta principal, aplicando la misma selección de estrategia que el wizard configuró.

### Modified Capabilities
- `provider-wizard-product-detail`: La prueba en el wizard y la ejecución normal usarán el mismo flujo de estrategias, asegurando que lo que funciona en el test del wizard funcione idéntico en producción.
- `provider-discovery`: El descubrimiento de URLs en ejecución normal usará los mismos métodos robustos que ya usa el wizard (basado en `StrategyType` y `Selectors`).

## Impact

- `ScrapingRunner.cs` (ServicesAPI) - método `RunForSiteAsync` y el endpoint `api/scraping/run`
- `PlaywrightScrapingService.cs` (Infrastructure/Scraping) - simplificación de la selección de modo en `ScrapeAsync`
- `Program.cs` (API) - endpoint de scraping para usar `ScrapeExecutionContext` en lugar de environment variables
- `StrategyOrchestrator.cs` - posibles ajustes para integración como ruta principal
- Estrategias individuales (`DirectExtractionStrategy`, `ListExtractionStrategy`, `FamiliesExtractionStrategy`) - verificar que usen los selectores del `SiteProfile` tal como lo hace el wizard
