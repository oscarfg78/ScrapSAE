## 1. ScrapeExecutionContext (Parámetros tipados)

- [x] 1.1 Crear record `ScrapeExecutionContext` en `ScrapSAE.Core/DTOs/` con propiedades: `IsHeadless`, `ManualLogin`, `KeepBrowser`, `ScreenshotFallback`, `MaxProductsOverride` (nullable int)
- [x] 1.2 Actualizar la firma de `ScrapingRunner.RunForSiteAsync` para aceptar `ScrapeExecutionContext context` como parámetro adicional
- [x] 1.3 Actualizar el endpoint `POST /api/scraping/run/{siteId}` en `Program.cs` para construir `ScrapeExecutionContext` desde query params y pasarlo al runner
- [x] 1.4 Eliminar del endpoint la escritura de env vars (`SCRAPSAE_MANUAL_LOGIN`, `SCRAPSAE_HEADLESS`, `SCRAPSAE_MODE`, `SCRAPSAE_KEEP_BROWSER`, `SCRAPSAE_SCREENSHOT_FALLBACK`) y el bloque `finally` de restauración
- [x] 1.5 Propagar `ScrapeExecutionContext` (o sus valores relevantes) desde `RunForSiteAsync` hasta `IScrapingService.ScrapeAsync` via parámetros explícitos o contexto de ambiente local a la llamada

## 2. Strategy-Driven Execution (StrategyType como fuente de verdad)

- [x] 2.1 En `ScrapingRunner.RunForSiteAsync`, reemplazar la lectura de `SCRAPSAE_MODE` con lectura de `site.StrategyType` para seleccionar el modo de ejecución
- [x] 2.2 Implementar el routing por `StrategyType`: `"Shopify"` → `ShopifyApiStrategy` directo; `"Generic"` / null → `StrategyOrchestrator`; otros → fallback a lógica legacy
- [x] 2.3 Conectar `IStrategyOrchestrator.ExecuteStrategiesAsync(page, site, executionId, token)` como la ruta principal para sites Generic, pasando la `IPage` ya inicializada del runner
- [x] 2.4 Verificar que `DirectExtractionStrategy` lee `site.Selectors["productCard"]`, `site.Selectors["sku"]`, etc. exactamente como los configura el wizard en `BuildSiteProfile`
- [x] 2.5 Verificar que `ListExtractionStrategy` lee `site.Selectors["productContainer"]` y la paginación desde `site.Selectors`
- [x] 2.6 Verificar que `FamiliesExtractionStrategy` usa las estrategias de `site.Strategies` para determinar la navegación de familias
- [x] 2.7 Asegurar que `StrategyOrchestrator.GetEnabledStrategies` usa `site.Strategies[]` correctamente y aplica fallback a Direct→List→Families cuando está vacío

## 3. Alineación del flujo de navegación (Browser setup)

- [x] 3.1 Refactorizar `PlaywrightScrapingService.ScrapeAsync` para que acepte el `ScrapeExecutionContext` y use `context.IsHeadless` / `context.ManualLogin` en lugar de leer env vars dentro del servicio
- [x] 3.2 Eliminar los bloques `if (shouldUseFestoHybrid)` y `if (isFestoName && ...)` que hardcodean la lógica por nombre de proveedor; el `StrategyOrchestrator` maneja la selección
- [x] 3.3 Mantener la ruta legacy de `PlaywrightScrapingService.ScrapeAsync` como fallback temporal (documentado con comentario) hasta validar que la nueva ruta funciona con todos los proveedores

## 4. Validación y Pruebas

- [ ] 4.1 Ejecutar scrape de prueba con el proveedor Festo (StrategyType=Generic, Strategies=[Families]) y verificar que produce productos equivalentes al resultado del wizard
- [ ] 4.2 Ejecutar scrape de prueba con un proveedor Shopify y verificar que usa ShopifyApiStrategy
- [ ] 4.3 Ejecutar scrape de prueba con un proveedor Generic (Direct strategy) y verificar que usa los selectores configurados por el wizard
- [ ] 4.4 Ejecutar dos scrapes simultáneos con parámetros distintos y verificar que no hay mezcla de configuración (thread-safety del `ScrapeExecutionContext`)
- [ ] 4.5 Verificar que los logs de ejecución muestran la estrategia seleccionada (nombre de estrategia) y no el modo legacy ("traditional"/"families")

## 5. Documentación de arquitectura

- [x] 5.1 Agregar comentarios de arquitectura al inicio de `PlaywrightScrapingService.cs` explicando las capas: Wizard → SiteProfile → StrategyOrchestrator → Strategy
- [x] 5.2 Agregar comentario en `ScrapingRunner.RunForSiteAsync` indicando el contrato: "SiteProfile.StrategyType + Strategies[] son la fuente de verdad"
- [x] 5.3 Actualizar el README del proyecto o crear un documento `docs/architecture-scraping.md` con el diagrama de capas de ejecución
