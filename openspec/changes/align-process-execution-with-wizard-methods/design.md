## Context

El sistema de scraping tiene dos capas que hoy operan con lógica parcialmente desconectada:

1. **Wizard** (`ProviderWizardViewModel` + `PageAnalysisService`): Analiza la URL con IA, genera un `SiteProfile` con `StrategyType` + `Strategies[]` explícitas, y llama `api/scraping/run` con `MaxProductsPerScrape=2`. Esta es la ruta **conocida que funciona**.

2. **Ejecución de producción** (`ScrapingRunner.RunForSiteAsync` + `PlaywrightScrapingService.ScrapeAsync`): Lee `SCRAPSAE_MODE` (environment variable) para seleccionar entre modos "traditional" / "families", y tiene múltiples rutas hardcodeadas (detección por nombre "Festo", `isSearchaniseListing`, `isFestoHybrid`, etc.). El `StrategyOrchestrator` existe y está registrado pero **no es la ruta principal** de ejecución.

**El problema central**: la ejecución en producción no usa `SiteProfile.Strategies[]` como fuente de verdad. En cambio, usa environment variables (`SCRAPSAE_MODE`, `SCRAPSAE_MANUAL_LOGIN`, `SCRAPSAE_HEADLESS`, etc.) que se establecen en el endpoint HTTP y se leen dentro del servicio de scraping. Esto es:
- Frágil: condición de carrera en concurrencia (múltiples scrapes simultáneos)
- No alineado: el wizard configura `StrategyType` en el `SiteProfile` pero la ejecución puede ignorarlo
- Difícil de mantener: lógica de selección de modo dispersa en múltiples capas

## Goals / Non-Goals

**Goals:**
- Que `SiteProfile.StrategyType` y `SiteProfile.Strategies[]` sean la única fuente de verdad para seleccionar cómo ejecutar el scraping
- Eliminar el uso de environment variables como mecanismo de configuración de runtime entre el endpoint HTTP y el servicio de scraping
- Crear un `ScrapeExecutionContext` tipado que pase parámetros de ejecución de forma explícita y thread-safe
- Hacer que el `StrategyOrchestrator` sea la ruta principal de ejecución (respetando las estrategias configuradas en el wizard)
- La ruta de "familias" (Festo) debe seleccionarse porque `SiteProfile.Strategies` contiene `FamiliesExtractionStrategy`, no porque el nombre del sitio contenga "Festo"
- Documentar las capas de arquitectura claramente

**Non-Goals:**
- Reescribir el motor completo de `PlaywrightScrapingService` (es muy grande; solo ajustar la selección de modo y el paso de parámetros)
- Cambiar la lógica interna de cada estrategia (`DirectExtractionStrategy`, etc.) — solo integrarlas como ruta principal
- Remover todas las env vars del sistema (solo las que pasan config entre el endpoint y el servicio de scraping)
- Modificar la UI del wizard (ya funciona correctamente)

## Decisions

### D1: Introducir `ScrapeExecutionContext` como objeto de parámetros de ejecución

**Decisión**: Crear un record/clase `ScrapeExecutionContext` que encapsule `IsHeadless`, `ManualLogin`, `KeepBrowser`, `ScreenshotFallback`, y `MaxProducts`. El endpoint HTTP instanciará este contexto desde los query params y lo pasará a `ScrapingRunner.RunForSiteAsync(siteId, context, token)`.

**Alternativa descartada**: Mantener las environment variables y solo documentarlas. Descartado porque el problema de thread-safety en concurrencia es real y crecerá con el tiempo.

**Rationale**: Pasar parámetros explícitos es más seguro, testeable y escalable. El runner ya tiene todos los deps necesarios para propagarlos.

### D2: Usar `SiteProfile.StrategyType` para selección de estrategia en `ScrapingRunner`

**Decisión**: En `RunForSiteAsync`, usar `site.StrategyType` (que el wizard ya configura correctamente: "Generic", "Shopify", "Direct", "List", "Families") para seleccionar el camino de ejecución, en lugar de leer `SCRAPSAE_MODE`. Mapear:
- `"Shopify"` → `ShopifyApiStrategy`
- `"Generic"` o `null` → usar `StrategyOrchestrator` con `site.Strategies[]` en orden de prioridad
- `"Direct"` / `"List"` / `"Families"` → como override específico si la lista `Strategies[]` está vacía

**Alternativa descartada**: Agregar un campo `ExecutionMode` nuevo al `SiteProfile`. Descartado porque `StrategyType` + `Strategies[]` ya contienen exactamente esta información, configurada por el wizard.

### D3: `StrategyOrchestrator` como ruta principal para sites Generic

**Decisión**: Para sites con `StrategyType == "Generic"` (o nulo), `ScrapingRunner` llamará `StrategyOrchestrator.ExecuteStrategiesAsync(page, site, executionId, token)` que ya itera las estrategias en orden de prioridad desde `site.Strategies[]`. Esto alinea producción con lo que el wizard configura.

**Alternativa descartada**: Mantener la lógica actual de `PlaywrightScrapingService.ScrapeAsync` como ruta principal. Descartado porque ese método tiene demasiadas ramificaciones hardcodeadas que divergen del wizard.

### D4: Las estrategias individuales reciben el `SiteProfile` completo

**Decisión**: Verificar que `DirectExtractionStrategy`, `ListExtractionStrategy`, y `FamiliesExtractionStrategy` lean los selectores desde `site.Selectors` y `site.SecondarySelectors` — exactamente como los configuró el wizard. Si alguna estrategia usa rutas alternativas de selección de selectors, alinearla.

## Risks / Trade-offs

- **[Riesgo] `PlaywrightScrapingService.ScrapeAsync` tiene lógica probada para casos edge** → Mitigación: Mantener ese método intacto inicialmente como fallback. La nueva ruta principal `StrategyOrchestrator` será el camino feliz; si produce cero resultados, se puede delegar a `ScrapeAsync` como fallback temporal.

- **[Riesgo] `ScrapingRunner` llama a `EnrichSiteSelectorsAsync` que puede modificar los selectores** → Mitigación: Verificar que este enriquecimiento sea aditivo (no destructivo) y que preserve los selectores configurados por el wizard.

- **[Trade-off] Cambiar la firma de `RunForSiteAsync`** → El endpoint `api/scraping/run` debe actualizarse para instanciar el `ScrapeExecutionContext`. Esto requiere pruebas de regresión del endpoint, pero simplifica el código del endpoint significativamente.

- **[Riesgo] Sites existentes sin `Strategies[]` configuradas** → Mitigación: Si `site.Strategies` está vacío, aplicar default igual al `StrategyOrchestrator` actual (Direct → List → Families).

## Migration Plan

1. Crear `ScrapeExecutionContext` como record inmutable en `ScrapSAE.Core.DTOs`
2. Actualizar `ScrapingRunner.RunForSiteAsync` para aceptar el context y eliminar la lectura de env vars
3. Actualizar el endpoint `api/scraping/run` para construir el context y eliminaar la escritura de env vars
4. Ajustar `ScrapingRunner` para usar `site.StrategyType` / `site.Strategies[]` + `StrategyOrchestrator`
5. Verificar estrategias individuales contra el `SiteProfile` del wizard
6. Probar con un scrape real de los mismos proveedores usados en el wizard (Festo, IDSupply, Shopify)

**Rollback**: El código original de `PlaywrightScrapingService.ScrapeAsync` se mantiene intacto. Si la nueva ruta falla, se puede revertir el endpoint a usar env vars mientras se investiga.
