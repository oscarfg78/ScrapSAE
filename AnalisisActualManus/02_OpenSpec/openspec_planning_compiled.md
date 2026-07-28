

---
SOURCE: corpus/openspec/changes/add-provider-wizard/design.md
---

## Context

ScrapSAE es una aplicación de scraping adaptativo para catálogos de proveedores, compuesta por:
- **ScrapSAE.Api** (ASP.NET Core Minimal API): controla el motor de scraping, CRUD de `SiteProfile` en Supabase (`config_sites`), y los endpoints de ejecución.
- **ScrapSAE.Desktop** (WPF .NET): interfaz de usuario que consume la API local.
- **ScrapSAE.Infrastructure**: contiene `PlaywrightScrapingService`, `OpenAIProcessorService`, y las estrategias de scraping (Direct, List, Families).

Actualmente, crear un proveedor requiere completar manualmente un formulario complejo en Desktop: base URL, selectores CSS (primarios, secundarios), estrategias de scraping, credenciales, etc. No existe asistencia inteligente para determinar estos valores. El resultado es una curva de aprendizaje alta y tasas de error elevadas en proveedores nuevos.

## Goals / Non-Goals

**Goals:**
- Wizard de 5 pasos en WPF que guíe al usuario desde la URL hasta el proveedor configurado y validado.
- Nuevo endpoint `POST /api/sites/analyze` que descargue el HTML de la URL con Playwright y lo envíe a GPT para análisis estructural del catálogo.
- El análisis IA retorna: selectores primarios, selectores secundarios, estrategias sugeridas, estructura de datos (campos SKU/nombre/imagen/precio/características), nombres de clases relevantes, y una evaluación de confianza.
- Test de scrape de prueba (máximo 120 productos) dentro del wizard para validar la configuración propuesta antes de guardar.
- Guardado final del `SiteProfile` en Supabase con valores por defecto: `IsActive = true`, `RequiresLogin = false`, `MaxProductsPerScrape = 120`.
- Vista previa de los productos extraídos mostrando SKU, imagen, nombre, precio y características, con indicadores visuales de completitud.

**Non-Goals:**
- Soporte de login/autenticación en el wizard (el wizard crea proveedores públicos; la configuración de login sigue siendo manual en el formulario avanzado).
- Edición de proveedores existentes desde el wizard (solo creación).
- Análisis de páginas que requieran interacción compleja (infinite scroll, filtros dinámicos); el wizard analiza la página tal como carga.
- Cambios en el esquema de base de datos de Supabase.

## Decisions

### D1: Dónde vive el análisis IA — en el API, no en Desktop

El análisis requiere Playwright para renderizar el HTML y el `IAIProcessorService` (OpenAI). Ambos ya están disponibles en `ScrapSAE.Api`. Crear un endpoint en la API mantiene la lógica pesada en el servidor y el Desktop solo consume REST.

**Alternativa considerada:** Hacer el análisis directamente en Desktop usando HttpClient. Rechazado porque requeriría duplicar la lógica de browser y AI, y complicaría el proceso de construcción.

### D2: Arquitectura del Wizard — ventana modal WPF (UserControl por paso)

Un `Window` modal con un `ContentControl` que cambia entre `UserControl` según el paso activo. El `ProviderWizardViewModel` maneja el estado global del wizard (URL ingresada, resultado del análisis, configuración final, resultados del scrape de prueba).

**Alternativa considerada:** `TabControl` visible. Rechazado porque permite navegar entre pasos sin completar el flujo secuencial, lo cual puede producir configuraciones incompletas.

### D3: Prompt de análisis IA — HTML truncado + instrucciones específicas de e-commerce

El HTML de páginas de proveedores puede ser enorme (>500KB). El endpoint `analyze` extrae solo el HTML visible (sin scripts, sin estilos inline), lo trunca a 50,000 caracteres del body, y lo envía a GPT-4o con un prompt especializado en detección de estructuras de catálogos de productos. El prompt solicita explícitamente: selectores CSS para product card, SKU, nombre, imagen, precio y características; nombres de clases CSS del contenedor de lista de productos; y la estrategia de scraping más adecuada.

**Alternativa considerada:** Enviar el HTML completo. Rechazado por límite de tokens y costo. Enviar solo texto visible. Rechazado porque los selectores CSS requieren el HTML estructurado.

### D4: Test de scrape de prueba — usa el endpoint existente `POST /api/scraping/run/{siteId}`

Para el scrape de prueba, el wizard: (1) crea temporalmente el `SiteProfile` en Supabase con la configuración propuesta por la IA, (2) ejecuta `POST /api/scraping/run/{tempSiteId}`, (3) muestra resultados, (4) si el usuario confirma, el site queda guardado; si cancela, se elimina.

**Alternativa considerada:** Crear un endpoint de scrape efímero que no persista nada. Rechazado porque el `ScrapingRunner` actual está diseñado para trabajar con `SiteProfile` registrado en Supabase, y refactorizarlo representaría un cambio mayor fuera del scope.

### D5: Formato de respuesta del análisis — DTO estructurado (no JSON libre)

`PageAnalysisResult` es un DTO fuertemente tipado con propiedades explícitas para cada campo de interés (SkuSelector, NameSelector, etc.). GPT retorna JSON con este esquema mediante function calling / structured output. Esto garantiza deserialización segura en el API.

## Risks / Trade-offs

- **[Risk] Calidad del análisis IA varía según sitio** → Mitigación: El wizard muestra un indicador de confianza por campo (Alta/Media/Baja) y permite al usuario editar los selectores propuestos antes de ejecutar el test.
- **[Risk] HTML truncado puede omitir la estructura de productos** → Mitigación: El endpoint captura el HTML después de que Playwright termina la carga (networkidle), y extrae preferentemente la sección del body donde aparece mayor densidad de elementos de lista.
- **[Risk] El scrape de prueba puede dejar sites temporales huérfanos si Desktop se cierra abruptamente** → Mitigación: Los sites temporales se marcan con `Name` prefijado `[TEMP]`; un job de limpieza en el API los elimina tras 1 hora.
- **[Risk] Tiempo de análisis excesivo** → Mitigación: El endpoint de análisis tiene un timeout de 30s; el wizard muestra un spinner con cancelación.
- **[Risk] Costo de tokens OpenAI** → Mitigación: el HTML se trunca a 50K caracteres; se usa GPT-4o-mini por defecto para el análisis de estructura (más barato), reservando GPT-4o para el procesamiento de productos.

## Migration Plan

1. Desplegar cambios en `ScrapSAE.Api` (nuevo endpoint `/api/sites/analyze` y `PageAnalysisService`).
2. Desplegar `ScrapSAE.Desktop` con la nueva ventana `ProviderWizardView`.
3. El botón "Nuevo Proveedor" en Desktop abrirá el wizard por defecto; el formulario manual permanece accesible como opción alternativa en el mismo flujo.
4. Sin cambios en la base de datos — el wizard usa el modelo `SiteProfile` existente.

## Open Questions

- ¿Se debe usar GPT-4o o GPT-4o-mini para el análisis de estructura? (Recomendación: GPT-4o-mini para menor costo; confirmación pendiente).
- ¿El scrape de prueba debe guardar los productos en `staging_products` o solo mostrarlos en memoria? (Propuesta: mostrar en memoria para el preview, el usuario decide si ejecuta un scrape real después).


---
SOURCE: corpus/openspec/changes/add-provider-wizard/proposal.md
---

## Why

Actualmente, agregar un nuevo proveedor a ScrapSAE requiere configurar manualmente los selectores CSS, estrategias de scraping y estructura de datos directamente en el formulario de la aplicación Desktop, sin ninguna guía inteligente. Esto es propenso a errores, lento, y requiere conocimiento técnico profundo de la estructura HTML del sitio proveedor. Un wizard asistido por IA que analice automáticamente el código fuente de la página y proponga la configuración óptima reducirá drásticamente el tiempo de incorporación de nuevos proveedores y aumentará la tasa de éxito del scraping desde el primer intento.

## What Changes

- **Nueva pantalla de wizard multi-paso** en la aplicación Desktop WPF para agregar proveedores, reemplazando el formulario plano actual con un flujo guiado.
- **Nuevo endpoint de análisis de página** (`POST /api/sites/analyze`) en la API que recibe una URL, descarga el HTML con Playwright (respetando JavaScript), y lo envía a GPT para análisis estructural.
- **Motor de análisis IA** que procesa el HTML crudo y retorna: esquema de datos detectado, nombres de clases CSS relevantes, selectores primarios y secundarios sugeridos para producto/SKU/imagen/precio/características, y las estrategias de scraping recomendadas (Direct, List, Families).
- **Test de scraping de prueba** desde el wizard: antes de guardar, ejecuta un scrape de hasta 120 productos usando la configuración propuesta por la IA, muestra resultados en tiempo real y permite ajustar antes de confirmar.
- **Guardado automático** del `SiteProfile` en Supabase (`config_sites`) con todos los campos pre-poblados: `IsActive = true`, `RequiresLogin = false`, `MaxProductsPerScrape = 120`, selectores, estrategias y `SecondarySelectors`.
- **Vista previa de productos extraídos** en el último paso del wizard, mostrando SKU, imagen, nombre y características detectadas, con indicadores de campos encontrados/ausentes.

## Capabilities

### New Capabilities
- `provider-wizard`: Wizard guiado multi-paso para crear un nuevo proveedor con análisis de URL asistido por IA, propuesta automática de selectores y validación con scrape de prueba.
- `page-analysis-ai`: Endpoint y servicio de análisis de página que descarga HTML y usa GPT para detectar la estructura de datos del catálogo de productos, incluyendo clases CSS, selectores, SKU, imagen, nombre, precio y características.

### Modified Capabilities
- `provider-management`: El flujo actual de creación de proveedores (formulario plano en Desktop) se amplía integrando el nuevo wizard como punto de entrada principal, manteniendo el formulario manual como opción avanzada.

## Impact

- **ScrapSAE.Api**: Nuevo endpoint `POST /api/sites/analyze` con `PageAnalysisService`. Requiere acceso a `IScrapingService` (Playwright) y `IAIProcessorService` (OpenAI).
- **ScrapSAE.Desktop**: Nueva ventana/vista `ProviderWizardView.xaml` con `ProviderWizardViewModel.cs`. Integración con `ApiClient` para llamar al endpoint de análisis y al endpoint de scrape de prueba.
- **ScrapSAE.Core**: Nuevos DTOs `PageAnalysisRequest`, `PageAnalysisResult`, `WizardScrapePreviewResult`.
- **Supabase**: Sin cambios de esquema. El wizard utiliza la tabla `config_sites` existente con el modelo `SiteProfile` actual.
- **OpenAI**: Nuevo prompt especializado en análisis de estructura HTML de tiendas en línea para extracción de catálogos de productos.


---
SOURCE: corpus/openspec/changes/add-provider-wizard/tasks.md
---

## 1. DTOs y Modelos del Dominio (ScrapSAE.Core)

- [x] 1.1 Crear `PageAnalysisRequest` DTO con propiedad `Url` (string)
- [x] 1.2 Crear `DetectedField` DTO con propiedades `Name`, `Selector` (nullable), `Confidence` (enum: High/Medium/Low)
- [x] 1.3 Crear `StrategyRecommendation` DTO con `StrategyName` y `Priority`
- [x] 1.4 Crear `PageAnalysisResult` DTO con todos los campos definidos en la spec (`ProductContainerSelector`, `ProductCardSelector`, `SkuSelector`, `NameSelector`, `ImageSelector`, `PriceSelector`, `CharacteristicsSelector`, `SecondarySelectors`, `RecommendedStrategies`, `DetectedFields`, `AnalysisSummary`, `PageTitle`, `DetectedLanguage`, `IsProductCatalog`)
- [x] 1.5 Agregar `WizardScrapePreviewProduct` DTO para el resultado del scrape de prueba (SKU, nombre, imagen, precio, características, campos detectados)

## 2. Servicio de Análisis de Página (ScrapSAE.Infrastructure / ScrapSAE.Api)

- [x] 2.1 Crear `IPageAnalysisService` interface en `ScrapSAE.Core.Interfaces` con método `AnalyzeAsync(string url, CancellationToken)`
- [x] 2.2 Crear `PageAnalysisService` en `ScrapSAE.Infrastructure.AI` que use `IScrapingService` para obtener HTML renderizado via Playwright (modo headless, esperar `networkidle`)
- [x] 2.3 Implementar lógica de extracción y truncado del HTML del body (máximo 50,000 chars, priorizando secciones con mayor densidad de listas)
- [x] 2.4 Implementar prompt especializado para GPT que solicite structured output con el esquema `PageAnalysisResult`: detectar contenedor de lista, tarjetas de producto, selectores por campo (SKU/nombre/imagen/precio/características), nivel de confianza por campo, y estrategia recomendada
- [x] 2.5 Implementar deserialización del JSON retornado por GPT al DTO `PageAnalysisResult` con manejo robusto de errores
- [x] 2.6 Agregar timeout de 30s al proceso completo de análisis (browser + AI)
- [x] 2.7 Registrar `PageAnalysisService` en el DI de `ScrapSAE.Api/Program.cs`

## 3. Endpoint de Análisis (ScrapSAE.Api)

- [x] 3.1 Agregar endpoint `POST /api/sites/analyze` en `Program.cs` que reciba `PageAnalysisRequest` y llame a `IPageAnalysisService`
- [x] 3.2 Retornar HTTP 200 con `PageAnalysisResult` en caso de éxito
- [x] 3.3 Retornar HTTP 422 con mensaje descriptivo si la URL es inaccesible
- [x] 3.4 Retornar HTTP 408 si el análisis supera el timeout de 30s
- [x] 3.5 Agregar endpoint `DELETE /api/sites/temp` (o lógica de limpieza) que elimine `SiteProfile` con nombre prefijado `[TEMP]` y `CreatedAt` hace más de 60 minutos

## 4. Job de Limpieza de Sites Temporales (ScrapSAE.Api)

- [x] 4.1 Crear `TempSiteCleanupService` como `IHostedService` que corra cada 15 minutos
- [x] 4.2 El servicio consulta `config_sites` por registros con `name` LIKE `[TEMP]%` y `created_at < now() - 60min` y los elimina
- [x] 4.3 Registrar el hosted service en `Program.cs`

## 5. ApiClient en Desktop (ScrapSAE.Desktop)

- [ ] 5.1 Agregar método `AnalyzePageAsync(string url)` en `ApiClient.cs` que llame a `POST /api/sites/analyze`
- [ ] 5.2 Agregar método `DeleteTempSitesAsync()` que llame al endpoint de limpieza de temporales

## 6. ProviderWizardViewModel (ScrapSAE.Desktop)

- [ ] 6.1 Crear `ProviderWizardViewModel.cs` con propiedades observables para el estado del wizard: `CurrentStep` (int), `Url`, `AnalysisResult`, `WizardConfig` (editable), `ScrapePreviewProducts`, `IsBusy`, `StatusMessage`
- [ ] 6.2 Implementar comando `AnalyzeCommand` (Paso 1→2): llama `AnalyzePageAsync` y actualiza `AnalysisResult`; maneja errores y timeout
- [ ] 6.3 Implementar `PopulateConfigFromAnalysis()` que mapea `PageAnalysisResult` a los campos editables del `WizardConfig` (Paso 2→3)
- [x] 6.1 Crear `ProviderWizardViewModel.cs` con propiedades observables para el estado del wizard: `CurrentStep` (int), `Url`, `AnalysisResult`, `WizardConfig` (editable), `ScrapePreviewProducts`, `IsBusy`, `StatusMessage`
- [x] 6.2 Implementar comando `AnalyzeCommand` (Paso 1→2): llama `AnalyzePageAsync` y actualiza `AnalysisResult`; maneja errores y timeout
- [x] 6.3 Implementar `PopulateConfigFromAnalysis()` que mapea `PageAnalysisResult` a los campos editables del `WizardConfig` (Paso 2→3)
- [x] 6.4 Implementar validación del formulario de configuración (Paso 3): nombre no vacío, al menos un selector de producto definido
- [x] 6.5 Implementar comando `RunTestScrapeCommand` (Paso 3→4): crea site temporal `[TEMP] NombreProveedor`, llama `POST /api/scraping/run/{tempSiteId}`, captura resultados
- [x] 6.6 Implementar lógica del Paso 4: si 0 productos encontrados, retornar al Paso 3 con mensaje; si >0, avanzar al Paso 5 con preview
- [x] 6.7 Implementar comando `SaveProviderCommand` (Paso 5): actualiza el site temporal (quitar prefijo `[TEMP]`, poner `IsActive=true`, `RequiresLogin=false`, `MaxProductsPerScrape=120`) o crea uno nuevo si no había temporal; navega de vuelta a la pantalla principal con el site seleccionado
- [x] 6.8 Implementar comando `CancelCommand`: si hay site temporal, lo elimina; cierra la ventana sin guardar

## 7. ProviderWizardView (ScrapSAE.Desktop WPF)

- [x] 7.1 Crear `ProviderWizardView.xaml` como `Window` modal con `DataContext` = `ProviderWizardViewModel`
- [x] 7.2 Implementar indicador de pasos (breadcrumb visual 1-2-3-4-5) con el paso actual resaltado
- [x] 7.3 Implementar **Paso 1** (UI): campo de texto para URL con botón "Analizar"
- [x] 7.4 Implementar **Paso 2** (UI): spinner de carga durante análisis; al completar, mostrar tabla de campos detectados (nombre, selector sugerido, indicador confianza con color: verde=High, amarillo=Medium, rojo=Low), estrategia recomendada y resumen textual del análisis
- [x] 7.5 Implementar **Paso 3** (UI): formulario editable con TextBox para nombre del proveedor, selectores primarios y secundarios; CheckBox para estrategias habilitadas; mensajes de validación inline
- [x] 7.6 Implementar **Paso 4** (UI): spinner durante el scrape; al completar, DataGrid con preview de productos (columnas: SKU, Nombre, Imagen URL, Precio, # Características); iconos check/advertencia por campo; mensaje "Mostrando N/Max productos"
- [x] 7.7 Implementar **Paso 5** (UI): pantalla de resumen con estadísticas del test (N productos extraídos, campos con cobertura alta/media/baja), botón "Guardar Proveedor" y botón "Volver a Ajustar"
- [x] 7.8 Implementar spinner overlay global en la ventana (visible cuando `IsBusy = true`) con botón "Cancelar" que cancele el `CancellationToken`
- [x] 7.9 Manejar todos los estados de error con mensajes amigables y opciones de retry

## 8. Integración con Pantalla Principal (ScrapSAE.Desktop)

- [x] 8.1 Agregar botón "Agregar Proveedor" prominente en la sección de proveedores de `MainWindow.xaml`
- [x] 8.2 Implementar comando en `MainViewModel.cs` que instancie y abra `ProviderWizardView` como dialog modal
- [x] 8.3 Después de cerrar el wizard con éxito, recargar la lista de proveedores y seleccionar el proveedor recién creado

## 9. Pruebas de Integración

- [ ] 9.1 Probar el endpoint `POST /api/sites/analyze` con al menos 2 URLs de proveedores reales distintos y verificar que el `PageAnalysisResult` es coherente
- [ ] 9.2 Probar el flujo completo del wizard en Desktop con un proveedor de prueba, desde la URL hasta el guardado final
- [ ] 9.3 Verificar que la cancelación en Paso 4 (después del scrape temporal) elimina correctamente el site temporal de Supabase
- [ ] 9.4 Verificar que el job de limpieza de sites `[TEMP]` funciona correctamente


---
SOURCE: corpus/openspec/changes/align-process-execution-with-wizard-methods/design.md
---

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


---
SOURCE: corpus/openspec/changes/align-process-execution-with-wizard-methods/proposal.md
---

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


---
SOURCE: corpus/openspec/changes/align-process-execution-with-wizard-methods/tasks.md
---

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


---
SOURCE: corpus/openspec/changes/customize-supplier-brand-specs/design.md
---

## Context

Currently, the ScrapSAE integration sends scraped products directly to Flashly. Some scraped specifications (like `source_url` and `supplier name`) are for internal or temporary use and should not be published on the online store. Furthermore, the scraped `brand` specification is sometimes captured with incorrect or temporary values. We need a way to override this value based on the supplier settings and prevent internal specifications from being transmitted to Flashly.

## Goals / Non-Goals

**Goals:**
- Add a configuration field to the `Provider` or `Supplier` entity to store a brand override value.
- Intercept the scraped product data before sending it to Flashly.
- Exclude `source_url` and `supplier name` from the product specifications list.
- Replace the `brand` specification value with the supplier's configured brand override value.

**Non-Goals:**
- Modifying how the scraper extracts the data (the scraping logic remains intact).
- Adding complex filtering rules based on regular expressions or multiple conditions (simple omission and replacement).

## Decisions

1. **Entity Update:** We will add a `BrandOverride` string property to the `Provider` entity in `ScrapSAE.Core`. This will require a database migration.
2. **Payload Modification Point:** The data transformation will happen in the integration service layer (likely where we map scraped data to the Flashly API payload). This prevents modifying core scraped data and isolates the logic to the Flashly integration boundary.
3. **Filtering Logic:** We will simply filter out `Specification` entries whose name (case-insensitive) matches "source_url" or "supplier name".
4. **Override Logic:** We will search for a `Specification` named "brand" (case-insensitive). If found, and if the associated `Provider` has a non-null, non-empty `BrandOverride`, we will update the specification's value.

## Risks / Trade-offs

- **Risk:** The names of specifications ("brand", "source_url", "supplier name") might change or have slight variations (e.g., "Supplier Name").
  - **Mitigation:** Use case-insensitive string comparisons. If names are prone to change, we might need a more robust configuration in the future, but hardcoded strings are sufficient for this initial requirement.
- **Risk:** Applying the brand override relies on the provider entity being available during the payload generation.
  - **Mitigation:** Ensure the provider information is fetched or passed along with the scraped product data before building the Flashly payload.


---
SOURCE: corpus/openspec/changes/customize-supplier-brand-specs/proposal.md
---

## Why

Currently, when scraping products, some specifications like "source_url" and "supplier name" are being sent to the online store, which is undesirable. Additionally, the "brand" specification is sometimes captured with temporary or incorrect values during scraping, and we need a way to assign the brand based on the supplier the records were obtained from. This change allows setting a specific brand value per supplier and prevents sending internal/undesired specifications to the final store.

## What Changes

- Add capability to configure a "brand" override value for each supplier (proveedor).
- When preparing data to send to the online store (Flashly integration), filter out "source_url" specification so it is not sent.
- Filter out "supplier name" specification so it is not sent.
- When sending data to the online store, if the supplier has a configured brand override, replace the scraped "brand" specification value with the configured one.

## Capabilities

### New Capabilities
- `supplier-specs-mapping`: Allows mapping and filtering of scraped specifications based on supplier settings before sending them to the online store.

### Modified Capabilities

## Impact

- Database schema or entity for Supplier (Proveedor) to include the brand override field.
- The payload generation logic for the Flashly integration will be updated to filter specific properties and override the brand.


---
SOURCE: corpus/openspec/changes/customize-supplier-brand-specs/tasks.md
---

## 1. Entity and Database Updates

- [x] 1.1 Add `BrandOverride` property to `Provider` (Supplier) entity in `ScrapSAE.Core`.
- [x] 1.2 Create and apply Entity Framework Core database migration for the new `BrandOverride` field (Using SQL script since EF is not used).
- [x] 1.3 Verify database migration applied successfully.

## 2. API and DTO Updates

- [x] 2.1 Update `ProviderDto` or equivalent response models to include `BrandOverride` (Uses `SiteProfile` entity directly).
- [x] 2.2 Update Provider creation/update requests and handlers to accept `BrandOverride`.

## 3. Flashly Integration Update

- [x] 3.1 Locate the payload generation logic for Flashly integration (`FlashlyProductMapper.ToFlashlyDto`).
- [x] 3.2 Ensure the `Provider` entity or its `BrandOverride` configuration is available during mapping (Assigned `Site` to products in `Worker.cs`).
- [x] 3.3 Implement filtering logic to omit any `Specification` named "source_url".
- [x] 3.4 Implement filtering logic to omit any `Specification` named "supplier name".
- [x] 3.5 Implement override logic to find the "brand" specification and replace its value with `BrandOverride` (or add it if missing), also updating the `SupplierName` DTO property.

## 4. Testing and Verification

- [x] 4.1 Test Provider creation/update through the API to ensure `BrandOverride` is saved.
- [x] 4.2 Run a test scraping/integration task and verify that "source_url" and "supplier name" are omitted from the sent payload.
- [x] 4.3 Verify that the "brand" specification is correctly overridden based on the provider's `BrandOverride` value.


---
SOURCE: corpus/openspec/changes/enhance-wizard-simulation/design.md
---

## Context

Actualmente el Wizard ayuda a configurar selectores y los prueba limitando la extracci�n a un n�mero muy bajo de productos. Sin embargo, no proporciona una visualizaci�n rica de "Demo Mode", y el Worker (ScrapSAE.Worker) en el backend fallaba en producci�n por el problema de compilaci�n de la DLL bloqueada. Adicionalmente, el an�lisis actual de GPT pide sugerir un solo selector CSS, el cual puede no ser �ptimo o romper si el DOM cambia sutilmente, por lo que requerimos extraer y almacenar dualidad CSS/XPath para cada campo y hacer que la estrategia de scraping sea resiliente al intentar ambos.

## Goals / Non-Goals

**Goals:**
- Probar un l�mite m�s robusto (5 productos en total: 1 en el test base y 4 adicionales en las tarjetas detalladas o como parte de la iteraci�n).
- Proporcionar un Demo Mode en la UI del Wizard.
- Modificar el sistema de an�lisis y extracci�n de selectores para soportar una estructura que contenga { "css", "xpath" } por selector.
- Garantizar que el Worker y la API compartan 100% la misma l�gica.

**Non-Goals:**
- Alterar el formato de Exportaci�n (CSV/Flashly).
- Redise�ar el ScrapSAE.Worker internamente.

## Decisions

**Decisi�n 1: Estructura del JSON en GPT**
GPT devolver� un objeto JSON para cada selector esperado: {"css": "...", "xpath": "..."}. En el backend de SiteProfile.Selectors (JSONB) esto se almacenar� como string, es decir, JSON anidado, o simplemente el Wizard adaptar� esto a 2 selectores, pero para no romper esquema en base de datos, mantendremos el JSON.
*Alternativa considerada*: Agregar columnas en DB, lo cual descartamos por fricci�n y complejidad.

**Decisi�n 2: Fallback Autom�tico en GetSelector**
La funci�n GetSelector en las estrategias (ListExtractionStrategy / DirectExtractionStrategy) intentar� hacer .QuerySelectorAsync() primero con el CSS. Si no encuentra nada, usar� el XPath.

## Risks / Trade-offs

- **Riesgo**: El Wizard podr�a tardar m�s tiempo procesando 5 productos durante el Test Scrape.
  **Mitigaci�n**: 5 productos es un n�mero razonable que permite validar selectores repetitivos sin causar timeouts extremos de Playwright.



---
SOURCE: corpus/openspec/changes/enhance-wizard-simulation/proposal.md
---

# Mejorar la simulaci�n del Wizard y Extracci�n Dual (CSS/XPath)

## 1. Problema actual
Aunque hemos solucionado el bug de extracci�n en memoria (el problema del cast de los selectores), el proceso *real* segu�a sin encontrar productos porque el archivo ScrapSAE.Infrastructure.dll estaba bloqueado por el sistema, impidiendo que la compilaci�n actualizara los binarios de ejecuci�n. 

Adem�s, necesitamos mayor garant�a de que el proceso real ser� exitoso. El usuario necesita ver una simulaci�n (Demo Mode) en el Wizard que sea 100% fiel al proceso real y que incluya m�ltiples extracciones (1 listado + 5 productos detallados), y que la IA proponga tanto selectores CSS como XPath para maximizar la resiliencia, en lugar de elegir s�lo uno.

## 2. Soluci�n Propuesta

### A. Demo Mode y Extracci�n M�ltiple en el Wizard
- Al ejecutar el "Test Scrape" (Paso 4) en el Wizard, aumentaremos el l�mite temporal a 5 productos (actualmente extrae 2 o a veces s�lo 1).
- Mostraremos la informaci�n extra�da de los 5 productos en una interfaz de simulaci�n "Demo Mode" dentro del Wizard para garantizar que la calidad de los datos (precio, sku, caracter�sticas, imagen) es �ptima antes de guardar.
- Re-usaremos exactamente la misma l�gica de RunScrapingAsync y PlaywrightScrapingService (ya lo hacemos, pero ahora explicitaremos que el comportamiento del backend debe ser id�ntico, y el worker solo cambiar� el l�mite MaxProductsPerScrape).

### B. Extracci�n Simult�nea de CSS y XPath
- Modificaremos el Prompt de GPT (OpenAIProcessorService.cs) para que devuelva un objeto estructurado para cada campo, el cual contenga **tanto el CSS �ptimo como el XPath �ptimo**.
- El SelectorAnalysisRequest y el DTO de respuesta deber�n soportar esta estructura dual (CssSelector y XPathSelector).
- En el orquestador (StrategyOrchestrator) y las estrategias (ListExtractionStrategy, DirectExtractionStrategy), al buscar un selector, se intentar� usar primero el CSS y si falla o est� vac�o, se usar� el XPath como un *fallback* autom�tico. Esto garantiza m�xima resiliencia sin que el usuario tenga que "elegir" uno manualmente.

## 3. Impacto en Componentes

- **OpenAIProcessorService.cs**: Actualizaci�n de Prompt y JSON Schema para devolver pares de { "css": "...", "xpath": "..." }.
- **SiteProfile / WizardConfig**: La estructura en base de datos (Selectors JSONB) guardar� las preferencias como strings crudos o adaptaremos el parser para leer estas sub-propiedades. (Para mantener retrocompatibilidad y simplicidad, el Wizard puede guardar el mejor de los dos, o guardar un objeto y el GetSelector se encarga de probar ambos).
- **ProviderWizardViewModel**: Mostrar hasta 5 productos en la tabla del Preview. UI de "Modo Demo".
- **Estrategias (ListExtractionStrategy / DirectExtractionStrategy)**: L�gica de Fallback (CSS -> XPath) implementada robustamente en GetSelector.


---
SOURCE: corpus/openspec/changes/enhance-wizard-simulation/tasks.md
---

## 1. Extracci�n Dual de IA (CSS y XPath)

- [x] 1.1 Actualizar el prompt de OpenAIProcessorService.cs para instruir a la IA que devuelva un objeto { css: "...", xpath: "..." } para cada campo en lugar de un string �nico.
- [x] 1.2 Actualizar el JSON Schema esperado por GPT (BuildSelectorAnalysisRequest) para que cada campo principal (productContainer, productCard, name, sku, etc.) sea un objeto con las propiedades css y xpath.

## 2. Ejecuci�n Resiliente (Fallback)

- [x] 2.1 Refactorizar GetSelector en ListExtractionStrategy.cs para detectar si el JSON parseado tiene propiedades css y xpath (usando un DTO auxiliar o deserializando en Dictionary<string, JsonElement>).
- [x] 2.2 Modificar la extracci�n en ListExtractionStrategy.cs para que primero intente encontrar el elemento usando el css provisto, y si no encuentra nada o el css est� vac�o, intente con el xpath.
- [x] 2.3 Replicar la misma l�gica de robustez de parseo JSON y fallback CSS -> XPath en DirectExtractionStrategy.cs.

## 3. Demo Mode en el Wizard

- [x] 3.1 Cambiar MaxProductsPerScrape de 2 a 5 en el m�todo ExecuteRunTestScrapeAsync de ProviderWizardViewModel.cs para que la simulaci�n pruebe varios elementos de la lista y del detalle.
- [x] 3.2 A�adir una etiqueta de texto en ProviderWizardView.xaml (en la pesta�a de Preview) que indique expl�citamente "Demo Mode: Informaci�n Simulada".


---
SOURCE: corpus/openspec/changes/fix-wizard-and-support-xpath/design.md
---

## Context
Actualmente el Wizard utiliza OpenAIProcessorService para analizar el HTML de un catalogo y sugerir selectores de productos, precios y SKUs. Sin embargo, el esquema actual esta enfocado en selectores CSS. En algunos sitios, extraer el dato es mas facil y robusto mediante XPath, pero no se esta sugiriendo nativamente. Adicionalmente, hay un bug actual en la vista previa del Wizard ("No se encontraron productos") que esta rompiendo el flujo de alta, posiblemente debido a que el motor del orquestador o la construccion del contexto no esta recibiendo los selectores en memoria correctamente.

## Goals / Non-Goals

**Goals:**
- Actualizar el schema de la IA (OpenAIProcessorService) para que pueda sugerir selectores XPath si son mas robustos o directos.
- Corregir el test de extraccion del Wizard para que funcione con el nuevo motor orquestado (StrategyOrchestrator).
- Asegurar que las estrategias de Playwright usen XPath o CSS transparentemente (aprovechando el auto-detect de Playwright para "//").

**Non-Goals:**
- No se reescribiran estrategias enteras de extraccion, solo la integracion de XPath y la resolucion del bug en el endpoint de prueba.

## Decisions

- **Modificacion del Schema y Prompt de OpenAI**: Instruiremos a la IA para que si elige XPath, inicie el string obligatoriamente con // (o xpath=), y si es CSS con sus prefijos estandar (., #). Playwright detecta esto automaticamente sin cambiar codigo base.
- **Bug Fix del Wizard Test**: Verificaremos el endpoint de API (ScrapingController / WizardController) que lanza la prueba, asegurando que popule correctamente el ScrapeExecutionContext con las estrategias y selectores generados en tiempo real antes de llamar al StrategyOrchestrator.

## Risks / Trade-offs

- **Risk**: XPath suele ser mas fragil ante redise�os menores de la pagina web.
  - **Mitigation**: El prompt de la IA se ajustara para que priorice CSS por su resiliencia, pero opte por XPath SOLAMENTE si el CSS no es suficiente (ej. hijos de elementos sin clases, td/tr de tablas, etc.).


---
SOURCE: corpus/openspec/changes/fix-wizard-and-support-xpath/proposal.md
---

## Why
El proceso de scraping actualmente asume o prioriza selectores CSS sugeridos por la IA, pero existen elementos donde es mucho mas facil o robusto acceder a traves de expresiones XPath. Ademas, el Wizard actualmente reporta un fallo durante la prueba de extraccion ("No se encontraron productos"). Anadir soporte para que la inteligencia artificial analice y determine si es mejor usar un selector CSS o un XPath brindara una mayor eficiencia y robustez en la extraccion, permitiendo al sistema de scraping usar la mejor estrategia por campo.

## What Changes
- Se corregira el fallo actual en el motor de pruebas del Wizard que impide encontrar productos en la vista previa.
- La IA (OpenAIProcessorService) sera ajustada en su prompt y validaciones para que evalue y sugiera el uso de XPath o CSS segun la estructura del DOM de cada proveedor.
- El modelo de datos y los perfiles de la base de datos se actualizaran para entender explicitamente cuando un selector es de tipo XPath o CSS.
- Se permitira ejecutar pruebas dobles (CSS vs XPath) de ser necesario.

## Capabilities

### New Capabilities
- xpath-selector-support: Capacidad para analizar y emplear expresiones XPath sugeridas por la IA para la extraccion de elementos complejos.

### Modified Capabilities
- provider-discovery: La sugerencia de estructura de catalogo ahora podra optar por XPath si proporciona mayor confiabilidad sobre CSS.
- provider-wizard-product-detail: El testing del Wizard validara e interpretara ambas formas sin fallar.

## Impact
- ScrapSAE.Infrastructure.AI.OpenAIProcessorService
- ScrapSAE.Core.DTOs.PageAnalysisDTOs
- ScrapSAE.Desktop.ViewModels.ProviderWizardViewModel
- Funciones de scraping nativas (PlaywrightScrapingService y Estrategias)


---
SOURCE: corpus/openspec/changes/fix-wizard-and-support-xpath/tasks.md
---

## 1. Modificacion de IA (OpenAIProcessorService)

- [x] 1.1 Actualizar el prompt de AnalyzeSelectorsAsync para indicar que puede sugerir XPath o CSS. Si es XPath debe llevar prefijo '//' o 'xpath='.
- [x] 1.2 Revisar si es necesario algun cambio menor en el schema, aunque con el prefijo deberia bastar para los strings devueltos.

## 2. Correccion del Wizard Test (Bug Fix)

- [x] 2.1 Revisar el endpoint de ScrapingController que usa el Wizard para lanzar el test de prueba.
- [x] 2.2 Corregir la inyeccion de los selectores detectados por la IA hacia el ScrapeExecutionContext temporal de prueba para que el orquestador no aborte por falta de configuracion. (La inyección estaba bien, el problema era que ListExtractionStrategy fallaba al hacer cast de `site.Selectors` a `Dictionary<string, object>`).
- [x] 2.3 Probar el paso 4 del Wizard para confirmar que recupera productos en lugar de dar error "No se encontraron productos".

## 3. Soporte de XPath en Ejecucion (PlaywrightScrapingService)

- [x] 3.1 Comprobar ListExtractionStrategy y DirectExtractionStrategy para asegurar que pasan el string del selector tal cual a Playwright, aprovechando su resolucion automatica de selectores XPath.


---
SOURCE: corpus/openspec/changes/improve-product-detail-extraction/design.md
---

## Context

During the provider discovery and configuration process, we added the ability to specify a Product Detail URL. However, the current extraction strategy fails to parse complex HTML blocks representing the product description, such as `tab-content-description` which contains multiple nested tags. We need to improve the extraction logic to correctly gather and format this data, and add proper validation for this specific step in the Provider Wizard's "Test" tab.

## Goals / Non-Goals

**Goals:**
- Upgrade the extraction mechanism in `ScrapingRunner` and/or `PageAnalysisService` to better parse deep, nested product descriptions and output them cleanly (e.g., iterating child nodes to form a JSON list or clean text).
- Update the API test endpoint to test product detail extraction for sample products.
- Enhance the Provider Wizard UI "Test" step to display product detail extraction results and a confidence indicator.

**Non-Goals:**
- Completely rewriting the HTML parser.
- Adding machine learning text summarization (we will rely on DOM structure and basic LLM extraction or rule-based parsing).

## Decisions

- **Decision 1: AI-Assisted DOM parsing for Details**: We will enhance `PageAnalysisService` to specifically instruct the AI to extract structured product characteristics from complex description DOMs (like `tab-content-description`), returning a JSON object or stringified list.
- **Decision 2: Product Detail validation in `/api/providers/test`**: The test endpoint will fetch the detail page (if a detail strategy is configured) for the first few products and validate that the detail extraction works, returning the extracted detail field alongside SKU/Name/Price.
- **Decision 3: Desktop UI Changes**: `ProviderWizardViewModel.cs` and `ProviderWizardView.xaml` will be updated to display the `Characteristics` field with its confidence during the "Test" phase, similar to other catalog fields.

## Risks / Trade-offs

- **Risk:** Fetching detail pages for all tested products might increase the test step duration significantly.
  - **Mitigation:** Limit the detail testing to only the first 2-3 products found in the catalog to keep the test step fast while ensuring the extraction rule works.


---
SOURCE: corpus/openspec/changes/improve-product-detail-extraction/proposal.md
---

## Why

The current product detail detection isn't working robustly on complex product detail pages. For example, when there are nested elements like `tab-content-description` containing multiple specification details, the extractor fails to obtain the complete description or parse it cleanly into a structured format. Improving this extraction strategy and validating it thoroughly in the wizard's "Test" step is essential for high-quality data ingestion.

## What Changes

- Update the extraction logic to handle complex HTML structures for product details, aggregating inner texts or formatting them into a structured JSON list for subsequent parsing.
- Modify the wizard's "Test" step so that it actually fetches the product detail page for tested products and validates the product detail extraction.
- Display a confidence indicator and the extracted details for the detail-level analysis in the wizard's "Test" step.

## Capabilities

### New Capabilities
- `advanced-product-detail-extraction`: Adds capabilities to parse and clean up complex description structures into structured JSON or cohesive descriptions during extraction.
- `wizard-detail-testing`: Incorporates the detail page extraction test inside the "Test" step, providing a confidence indicator for product detail discovery.

### Modified Capabilities
- `provider-wizard-product-detail`: Update the requirement to include testing the extracted product details in the wizard's testing phase.

## Impact

- Wizard UI (Test step will show detail page analysis results)
- Scraping Engine / Analysis Services (Strategy logic to parse nested HTML elements like `tab-content-description` into JSON lists or clean text)
- API endpoint for product testing to include detail-level extraction.


---
SOURCE: corpus/openspec/changes/improve-product-detail-extraction/tasks.md
---

## 1. Extraction Engine Updates

- [ ] 1.1 Update `PageAnalysisService` (or equivalent AI/DOM parser) to handle complex HTML blocks for product details (e.g., iterating nested nodes in `tab-content-description`).
- [ ] 1.2 Modify `ScrapingRunner` to apply the improved detail extraction strategy and return a clean text or JSON structured list.

## 2. API Test Endpoint Modification

- [ ] 2.1 Update `TestScrapingConfig` endpoint to optionally perform a detail page fetch for the first N products.
- [ ] 2.2 Ensure the test response includes the extracted product details/characteristics alongside the catalog data.

## 3. Provider Wizard UI Enhancements

- [ ] 3.1 Update `ProviderWizardViewModel.cs` to handle the `Characteristics` field from the API test response.
- [ ] 3.2 Modify `ProviderWizardView.xaml` to display the extracted detail data and its confidence indicator in the Test Results tab.
- [ ] 3.3 Verify UI responsiveness and layout after adding the new indicators.


---
SOURCE: corpus/openspec/changes/improve-product-details-extraction/design.md
---

## Context

Currently, the scraping process extracts text and specific attributes from product pages. However, rich descriptions located in tabs or specific containers (such as `#tab-content-description` or `.product-description`) may not be captured properly, either because they require specific selectors or because they are lost when converting the entire page to plain text for AI processing.

## Goals / Non-Goals

**Goals:**
- Provide a reliable mechanism to target and extract extended product descriptions.
- Pass the extracted description content accurately to the resulting product payload (either directly or via the AI processing step).
- Allow configuration per-supplier (SiteProfile) using `SecondarySelectors` or similar mechanism to target the description element.

**Non-Goals:**
- Completely rewriting the scraping engine.
- Automatically guessing the description container without any configuration.

## Decisions

- **Decision 1: Configuration via SecondarySelectors**:
  We will use the existing `SecondarySelectors` dictionary on the `SiteProfile` (e.g., key `"description"`) to allow users to specify the CSS selector for the description block.
- **Decision 2: Extraction logic in PlaywrightScrapingService**:
  The `PlaywrightScrapingService` will check for the `"description"` key in `SecondarySelectors`. If found, it will extract the `innerHTML` or `innerText` of that element.
- **Decision 3: Mapping via AIProcessor or Direct Assignment**:
  If the AI is used to process the product, we can append the extracted description text to the AI context to ensure it incorporates it into the final JSON. Alternatively, we can inject it directly into the `Specifications` JSON as "Description" or map it to the `Description` property of `StagingProduct` if the AI leaves it empty. We will aim to map it to the `Description` property or `Specifications` dictionary.

## Risks / Trade-offs

- **Risk**: Description HTML might be too large for the AI context limit.
  - **Mitigation**: We will extract `innerText` instead of raw HTML, or truncate it if it exceeds a certain threshold.
- **Risk**: Selectors might change on the provider's website.
  - **Mitigation**: Using `SecondarySelectors` allows the user to update the selector dynamically from the UI without code changes.


---
SOURCE: corpus/openspec/changes/improve-product-details-extraction/proposal.md
---

## Why

Currently, when scraping products, some websites contain detailed product descriptions (such as features, technical data, or extended descriptions) inside specific HTML sections (like description tabs). This information is either lost or not fully captured by the scraper. Improving the extraction of these details ensures that the final exported product contains comprehensive information that is valuable for the online store.

## What Changes

- Modify the scraping engine (or the AI extraction phase) to correctly capture the product's extended details/description when available on the page.
- Add support for a "description" or "details" selector if needed, or ensure the full relevant DOM content is passed to the AI to extract a robust `description`.
- Ensure the extracted details are mapped into the `description` field or appended to the `specifications` JSON.

## Capabilities

### New Capabilities
- `product-details-extraction`: Improves the capture and extraction of extended product descriptions from product pages and mapping them to the final output.

### Modified Capabilities

## Impact

- `PlaywrightScrapingService` or `ScrapingRunner`: To capture the description element.
- `OpenAIProcessorService`: To properly parse the extended description into the resulting JSON.
- Database/Export models: To ensure the new data correctly flows to Flashly or CSV.


---
SOURCE: corpus/openspec/changes/improve-product-details-extraction/tasks.md
---

## 1. Core Implementation

- [x] 1.1 Update `PlaywrightScrapingService` (or relevant scraping logic) to look up a secondary selector with the key `"description"`.
- [x] 1.2 If the `"description"` selector matches an element on the page, extract its `innerText` (or `innerHTML` stripped of dangerous tags).
- [x] 1.3 Map the extracted description text to the `ScrapedProduct` structure or pass it explicitly to the AI processor context, so it is either directly set in the `Description` property or merged into `Specifications`.

## 2. API and JSON Mapping

- [x] 2.1 Update `OpenAIProcessorService` to receive the extended description explicitly and instruct the AI to use it for the final JSON's `description` field.
- [x] 2.2 Alternatively (or additionally), in `ScrapingRunner.cs` or `RescrapeJobService.cs`, fallback to directly mapping the extracted description to the `StagingProduct` if the AI leaves the `description` field empty.
- [x] 2.3 Verify that the mapping generates the correct Flashly DTO via `FlashlyProductMapper`.

## 3. Testing and Verification

- [x] 3.1 Run a scraping job against a product URL with a known description tab/element using a properly configured `SiteProfile` (with `SecondarySelectors["description"]` set).
- [x] 3.2 Verify that the `StagingProduct` created contains the full extracted description.
- [x] 3.3 Ensure the information is propagated correctly to the final export payload.


---
SOURCE: corpus/openspec/changes/improve-selector-extraction-analysis/design.md
---

## Context

El proceso actual de análisis en el Wizard (`PageAnalysisService`) descarga el HTML de la página (truncándolo por tamaño) y se lo pasa a OpenAI (usando *Structured Outputs*). El problema es que OpenAI con HTML en crudo (a menudo muy anidado o ruidoso) tiende a generar selectores frágiles, muy complejos o poco exactos (ej. usando rutas xpath súper largas que se rompen al mínimo cambio).

Adicionalmente, el Wizard está analizando la "URL de Catálogo", donde tal vez no exista toda la información del producto (por ejemplo, las características o la descripción detallada sólo existen al entrar a la "URL de Detalle").

## Goals / Non-Goals

**Goals:**
- Implementar un análisis en 2 fases en el Wizard (Catálogo y Detalle).
- Utilizar técnicas heurísticas (con `AngleSharp` o analizadores de DOM) para pre-filtrar el HTML o extraer los selectores candidatos obvios ANTES de pasárselo a OpenAI.
- Retornar selectores limpios (clases únicas, IDs, o atributos específicos).

**Non-Goals:**
- Reemplazar completamente a OpenAI por reglas manuales. GPT seguirá tomando la decisión final basada en el pre-análisis.
- Modificar el flujo base del Scraping en ejecución (Worker), este cambio se limita a mejorar cómo se *descubren* los selectores en el Wizard/API.

## Decisions

1. **Análisis en 2 fases orquestado por la API**:
   - `Phase 1: Catalog Analysis`: Analiza la URL del listado. El objetivo es identificar `productContainerSelector`, `productCardSelector` y, lo más importante, extraer un enlace representativo a un producto (`detailLink`).
   - `Phase 2: Detail Analysis`: Descarga la página de detalle del enlace encontrado (o el que el usuario haya proveído opcionalmente) y busca `sku`, `name`, `price`, `image` y `characteristics`.
   - La API (`/api/sites/analyze`) orquestará esto internamente para no complicar la UI, o devolverá un progreso. Por simplicidad, se hará secuencialmente en el mismo endpoint (toma más tiempo, pero es más robusto).

2. **Pre-análisis Heurístico en el DOM (`AngleSharp`)**:
   - En lugar de enviar todo el `body`, el `PageAnalysisService` utilizará `AngleSharp` para buscar elementos con IDs o clases semánticas (ej. `[class*='product']`, `[id*='price']`, `table`, `ul`).
   - El servicio limpiará los atributos inútiles y extraerá la jerarquía básica para que OpenAI la entienda más fácil, generando un "árbol simplificado" (DOM Skeleton).
   - OpenAI usará este DOM Skeleton para elegir el selector correcto.

3. **Uso de XPath relativos y CSS limpios**:
   - Refinaremos el System Prompt de OpenAI para forzar el uso de selectores CSS limpios y evitar XPaths absolutos `html/body/div[1]/...`. Exigiremos el uso de `.//` o `//` enfocados en atributos clave.

## Risks / Trade-offs

- **[Risk] Mayor tiempo de análisis**: Al realizar dos descargas de Playwright (Catálogo + Detalle) y dos llamadas a GPT, el análisis tomará el doble de tiempo (probablemente 40-60 segundos).
  - *Mitigación*: Mantener visible un indicador de estado claro en el UI ("Analizando Catálogo...", "Analizando Detalle...").
- **[Risk] La heurística remueve información vital**: Al limpiar el DOM, podríamos quitar la etiqueta que GPT necesitaba.
  - *Mitigación*: La heurística solo removerá `<script>`, `<style>`, `svg`, clases utilitarias de Tailwind (opcional), pero mantendrá la estructura base.


---
SOURCE: corpus/openspec/changes/improve-selector-extraction-analysis/proposal.md
---

## Why

Actualmente, el proceso del Wizard delega completamente la tarea de encontrar los selectores a GPT, enviándole el HTML truncado de la página. Aunque esto funciona a nivel general, los selectores devueltos suelen ser extraños o ineficientes, provocando que fallen las extracciones reales con Playwright. Necesitamos un enfoque más estructurado: un pre-análisis heurístico en el DOM antes de usar IA y un flujo lógico de "Catálogo -> Detalle de Producto" para asegurar que los selectores resultantes sean los más claros y precisos posibles.

## What Changes

- **Pre-análisis heurístico del DOM**: Antes de pedirle a GPT que genere los selectores, se aplicará una lógica (o framework) de extracción al DOM para identificar patrones comunes (ej. contenedores principales, tablas de especificaciones) que reduzca el ruido y limite las opciones.
- **Detección inteligente de URL de Detalle**: Cuando se provea la "URL del catálogo de productos", el sistema detectará automáticamente un enlace hacia un producto para usarlo como "URL de Detalle".
- **Análisis de dos fases**: 
  1. Análisis de catálogo (lista) enfocado en extraer la URL de los productos (y opcionalmente precio/nombre si están en la tarjeta).
  2. Análisis de detalle, usando la URL de producto localizada, enfocado en extraer descripción, SKU y características adicionales.
- **Selectores óptimos**: Uso de técnicas combinadas (heurística + IA) para retornar los selectores CSS/XPath más directos y resilientes (ej. usando IDs, clases únicas o atributos específicos).

## Capabilities

### New Capabilities
- `selector-optimization`: Define la lógica y herramientas necesarias para el pre-análisis del DOM y generación de selectores limpios (heurísticas + IA).
- `two-phase-analysis`: Orquesta el análisis del Wizard dividiéndolo en Análisis de Catálogo y Análisis de Detalle.

### Modified Capabilities
- `provider-wizard-product-detail`: Ajuste para integrar la extracción y validación automática de la URL de detalle desde el catálogo base, enlazándose al flujo de dos fases.

## Impact

- **UI del Wizard (`ProviderWizardViewModel`)**: Cambios menores para acomodar la retroalimentación de las dos fases del análisis.
- **Backend API (`PageAnalysisService`)**: Refactorización profunda. Pasará de un simple prompt a GPT con el HTML a un pipeline de análisis (descarga HTML -> limpieza -> pre-análisis/framework -> Prompt GPT -> consolidación).
- **Extracción de Scraping**: No debería cambiar sustancialmente, pero se beneficiará de tener selectores `DualSelector` (CSS/XPath) más estables y lógicos.


---
SOURCE: corpus/openspec/changes/improve-selector-extraction-analysis/tasks.md
---

## 1. Integración de Heurística y Limpieza de DOM (AngleSharp)

- [x] 1.1 Modificar `PageAnalysisService` para implementar un método de limpieza de DOM (remover scripts, styles, svg).
- [x] 1.2 Implementar heurística con AngleSharp para extraer un "DOM Skeleton" enfocado en contenedores semánticos (`table`, `ul`, `[class*='product']`).
- [x] 1.3 Refinar el System Prompt de OpenAI (`BuildProcessedProductVisionRequest` o similar) para exigir CSS limpio y XPath relativos (`//` o `.//`) basados en el DOM Skeleton.

## 2. Refactorización de Análisis en Dos Fases en la API

- [x] 2.1 Renombrar/Ajustar el endpoint actual de análisis para manejar dos fases (o crear dos métodos separados internamente `AnalyzeCatalogAsync` y `AnalyzeDetailAsync`).
- [x] 2.2 En la Fase 1 (Catálogo): Instruir a la IA o usar AngleSharp para extraer un enlace representativo a un detalle de producto (`DetailUrl`).
- [x] 2.3 En la Fase 2 (Detalle): Descargar el HTML de la `DetailUrl` obtenida (o provista) y ejecutar la extracción profunda (SKU, Nombre, Imagen, Precio, Características).
- [x] 2.4 Consolidar los resultados de ambas fases en el `PageAnalysisResult`.

## 3. Ajustes en la Interfaz del Wizard (Desktop)

- [x] 3.1 Actualizar `ProviderWizardViewModel` para enviar la URL del catálogo y esperar/recibir la URL de detalle sugerida por la API.
- [x] 3.2 (Opcional/Menor) Mostrar al usuario un feedback de que el proceso está en "Analizando Catálogo..." y luego "Analizando Detalle...".
- [x] 3.3 Validar que el botón "Ejecutar test de scraping" utilice la misma lógica y no falle.


---
SOURCE: corpus/openspec/changes/shopify-scraping-strategy/design.md
---

## Context

El sistema actualmente intenta extraer información genérica usando un `PageAnalysisService` que descarga el DOM con Playwright, lo envía completo (o casi completo) a OpenAI, y pide selectores CSS.
Sin embargo, algunos proveedores como Shopify no tienen catálogos estructurados de forma tan sencilla. Sus listas de productos son a veces inyectadas por Javascript en componentes fuertemente acoplados. `Mejora Web Scraping en .NET.md` sugiere el uso de Keyed Services para inyectar estrategias particulares por proveedor (como una estrategia de Shopify que intente consumir `/products.json` de forma nativa) y el uso de poda del DOM (DOM Pruning) cuando se tenga que analizar la página vía OpenAI.

## Goals / Non-Goals

**Goals:**
- Configurar .NET 8 Keyed Services para resolver instancias específicas de scraping (ej. ShopifyStrategy vs GenericStrategy).
- Implementar Poda de DOM en `PageAnalysisService` para reducir el tamaño del HTML enviado al LLM.
- Detectar proveedores Shopify automáticamente en el Wizard y crear una configuración asociada que no dependa puramente de selectores CSS frágiles.

**Non-Goals:**
- Migrar todo el motor actual a C# nativo abandonando la IA; la IA se usará como respaldo robusto.
- Cambiar la base de datos o el frontend de escritorio de ScrapSAE en gran medida, solo la forma en que los sitios se configuran y extraen.

## Decisions

- **Keyed Services**: 
  - *Rationale*: .NET 8 tiene soporte nativo para `[FromKeyedServices]`. Almacenaremos una clave de estrategia en la tabla de proveedores. Si es Shopify, usamos la implementación `ShopifyScraperStrategy`.
- **Detección en el Wizard**: 
  - *Rationale*: Antes de invocar OpenAI, analizaremos el HTML para encontrar `window.Shopify` o links a `cdn.shopify.com`. Si se encuentra, marcaremos el proveedor como "Shopify" y podremos evitar el uso intensivo de LLM si consumimos su API.
- **DOM Pruning**:
  - *Rationale*: Antes de invocar a `gpt-4o`, se removerán tags `<script>`, `<style>`, `<svg>`, y nodos `display: none` vía AngleSharp para abaratar costos de token y mejorar la inferencia.

## Risks / Trade-offs

- **[Risk]** Bloqueos de la API de Shopify (HTTP 429).
  - *Mitigation*: Emplear Polly con Exponential Backoff para peticiones a la API del proveedor.
- **[Risk]** Poda de DOM muy agresiva perdiendo atributos data vitales.
  - *Mitigation*: Solo eliminar tags declarativos como estilos y scripts sin modificar los metadatos o las etiquetas schema.org.


---
SOURCE: corpus/openspec/changes/shopify-scraping-strategy/proposal.md
---

## Why

Actualmente, el Wizard de configuración intenta extraer selectores estáticos desde el DOM puro. Sin embargo, los proveedores modernos como Shopify listan productos dinámicamente o estructuran sus datos de tal forma que los selectores CSS fallan o no abarcan todas las variaciones. Necesitamos un enfoque estratégico más robusto alineado a nuestra nueva arquitectura para descubrir el esquema y extraer los datos exitosamente usando la integración nativa con Shopify y los enfoques semánticos propuestos (Keyed Services, JSON-LD, y Shopify API).

## What Changes

- Implementación del patrón Strategy con `Keyed Services` para manejar integraciones específicas por plataforma (ej. Shopify, genérico).
- Mejora del Wizard (Discovery) para detectar automáticamente si el sitio está impulsado por Shopify o tiene datos JSON-LD.
- En sitios Shopify, intentar consumir nativamente `products.json` u optimizar la estrategia LLM para extraer colecciones y marcas específicas.
- Refactorización de la tubería de análisis para emplear Poda de DOM (DOM Pruning) reduciendo tokens enviados al LLM y mejorando el éxito en la respuesta de OpenAI estructurada.

## Capabilities

### New Capabilities
- `shopify-integration`: Integración específica para descubrir y extraer datos estructurados de Shopify vía API o metadatos nativos.
- `dom-pruning-analyzer`: Sistema de limpieza y poda del DOM antes del envío del contenido HTML al servicio de OpenAI para reducir el ruido, mejorar la precisión de los esquemas, y bajar costos de token.

### Modified Capabilities
- `provider-wizard`: Se modifica el comportamiento de descubrimiento para priorizar metadatos, detección de plataforma (Shopify) y delegar a la estrategia específica correspondiente en vez de un análisis genérico.

## Impact

- `ScrapSAE.Infrastructure.AI`: Refactorizado para incorporar pre-procesamiento del DOM y análisis de metadatos (JSON-LD).
- `ScrapSAE.Api`: Integración de Keyed Services de .NET 8 en el contenedor de dependencias (`IServiceCollection`).
- `ScrapSAE.Core`: Nuevos modelos DTO y configuración específica para las estrategias de extracción (Shopify API endpoint).


---
SOURCE: corpus/openspec/changes/shopify-scraping-strategy/tasks.md
---

## 1. Implementación de Estrategia por Proveedor (Keyed Services)

- [x] 1.1 Definir interfaz `IProviderScraperStrategy` en `ScrapSAE.Core`.
- [x] 1.2 Implementar `GenericPlaywrightStrategy` para análisis genérico con OpenAI/Playwright.
- [x] 1.3 Implementar `ShopifyApiStrategy` utilizando consumo del endpoint nativo `/products.json`.
- [x] 1.4 Registrar las estrategias en el contenedor DI con `.AddKeyedScoped()` en `Program.cs` de la API.

## 2. Poda de DOM (DOM Pruning)

- [x] 2.1 Modificar `PageAnalysisService` para instanciar `AngleSharp` y parsear el HTML obtenido de Playwright.
- [x] 2.2 Crear método de limpieza que remueva tags `<script>`, `<style>`, `<link>`, y nodos no visibles.
- [x] 2.3 Utilizar el HTML podado en la consulta que se envía a `gpt-4o`.

## 3. Modificaciones al Wizard Discovery

- [x] 3.1 Actualizar el modelo `Provider` en la DB para admitir un enum o string de `StrategyType`.
- [x] 3.2 Modificar el endpoint de análisis para detectar firmas de Shopify en el HTML (`window.Shopify`, cdn.shopify.com).
- [x] 3.3 Devolver en `PageAnalysisResult` el tipo de estrategia detectada para configurarlo automáticamente.
- [x] 3.4 Actualizar la UI del Wizard para guardar el `StrategyType` en la creación del Proveedor.

## 4. Refactorización de Resiliencia con Polly

- [x] 4.1 Añadir `Microsoft.Extensions.Http.Polly` a la API si no está.
- [x] 4.2 Configurar una política de *Exponential Backoff* al `HttpClient` de la estrategia de Shopify.


---
SOURCE: corpus/openspec/changes/wizard-brand-and-test-limits/design.md
---

## Context

El Wizard para crear nuevos perfiles de scraping en ScrapSAE (`ScrapSAE.Desktop`) guía al usuario en la configuración de la URL, paginación, listado de productos, y captura de detalles. Sin embargo, no permite actualmente asignar una "marca" (brand) global para los productos de un proveedor, lo que resulta en productos sin marca en el destino a menos que se configure individualmente en Flashly. 
Además, la validación de prueba actual procesa muchos productos (120 por defecto), lo que vuelve muy lenta la comprobación. Sin embargo, en operación normal, el límite de lotes de productos sí debe ser de 120.

Por otro lado, la lógica utilizada en el Wizard para descubrimiento de productos y familias es sumamente robusta. En contraste, la pantalla de "Scraping" principal actualiza su flujo de ejecución utilizando mecanismos paralelos que a veces pueden diferir. Es crucial que el scraping normal aproveche y ejecute (como fallback o complemento) la misma lógica exacta empleada durante la fase de "prueba" del Wizard para asegurar una alta tasa de éxito. Esto requiere una integración cuidadosa donde se complementen ambos enfoques sin interrumpir a los proveedores que ya funcionan perfectamente. Asimismo, la pantalla principal carece de indicadores visuales granulares que dejen claro en qué estado exacto del descubrimiento (ej. "Extrayendo subfamilias", "Analizando paginación", "Descargando detalles") se encuentra el bot.

## Goals / Non-Goals

**Goals:**
- Capturar un nuevo campo "Marca" en el primer paso del Wizard.
- Limitar a 10 productos la prueba de scraping dentro del Wizard.
- Asegurar que al momento de guardar el perfil en disco, `MaxProductsPerJob` sea 120.
- Integrar la lógica de "Descubrimiento y Prueba" (Wizard) en el flujo principal (`ScrapingRunner` / pantalla de Scraping) como un paso aditivo/complementario que se suma al proceso existente.
- Sugerir y diseñar cambios estructurales en el *front* de la pantalla "Scraping" (ej. un panel dedicado a "Fases de Ejecución" o una "Línea de tiempo" de estados) para reportar el progreso con claridad al usuario.

**Non-Goals:**
- Modificar la forma en que los trabajos de rescraping procesan los productos (el límite en producción debe seguir configurable, pero por defecto a 120).
- Alterar el modelo fundamental de `SiteProfile` más allá de asegurar la asignación del `Brand`.
- Remplazar o romper agresivamente el flujo existente del `PlaywrightScrapingService` que ya operan otros sitios. 

## Decisions

- **Campo Marca en SiteProfile:** El `SiteProfile` persistirá este dato y se capturará en `ScrapSAE.Desktop\ViewModels\WizardViewModel.cs`.
- **Límites diferenciados:** `TestStepViewModel` forzará un límite de 10 productos durante la simulación, pero `SiteConfigurationService` guardará 120 al persistir en disco.
- **Integración Aditiva de Descubrimiento:** El `PlaywrightScrapingService` y el `ScrapingRunner` se refactorizarán para invocar la misma rutina (ej. `ExecuteDiscoveryAndTestAsync` o su equivalente en el código) antes o en paralelo al scraping de las *start URLs*. Si el descubrimiento encuentra nuevas URLs, se agregan al _pool_ de `startUrls` a procesar, complementando (no reemplazando) lo existente.
- **Rediseño del Frontend (Scraping Screen):** En la pantalla "Scraping" (ver imagen de referencia), se reorganizarán los paneles:
  - **Panel de Estado Granular:** Sustituir o enriquecer el espacio debajo de "Estadísticas de Ejecución" con un control que muestre la fase actual (ej. un `ProgressBar` con texto descriptivo dinámico: *Fase 1: Descubrimiento de Catálogo*, *Fase 2: Resolución de Paginación*, *Fase 3: Extracción de Items*).
  - **Consola Mejorada:** Mantener la consola en tiempo real, pero filtrarla opcionalmente por "Solo Errores" o "Descubrimiento" para que el usuario no se abrume con verbosidad innecesaria.
  - **Status Badge:** El área "Estado: Idle" será más prominente, utilizando códigos de color (Verde para explorando, Azul para extrayendo, Amarillo para intentando *fallback*).

## Risks / Trade-offs

- [Risk] Romper proveedores que ya funcionan al inyectar la lógica del Wizard. → Mitigation: Usar la estrategia aditiva. El descubrimiento sumará URLs descubiertas al listado de inicio; si el proveedor existente no necesita descubrimiento, se operará sin cambios o se habilitará el descubrimiento solo si la recolección tradicional arroja menos productos de los esperados.
- [Risk] Interfaz abarrotada en la pantalla de Scraping. → Mitigation: Se agruparán los contadores en una vista de resumen más compacta y se usará un componente estructurado (como un Stepper o Timeline vertical) para indicar el progreso.


---
SOURCE: corpus/openspec/changes/wizard-brand-and-test-limits/proposal.md
---

## Why

Actualmente, el asistente (Wizard) para configurar nuevos proveedores captura información básica pero no permite especificar de antemano el nombre de la marca que se asignará a los productos extraídos. Adicionalmente, durante la prueba de extracción se analizan demasiados productos (120), lo que la hace lenta, cuando solo se requieren unos pocos para verificar que los selectores funcionan. Sin embargo, al guardar el perfil, el número predeterminado de productos a extraer por lote debe configurarse en 120. Esta mejora agilizará la configuración y las pruebas iniciales.

Por otro lado, el motor de "descubrimiento y prueba" que se utiliza en el Wizard es robusto y muy efectivo. Se requiere integrar este mismo motor en la **pantalla principal de Scraping** para mejorar el proceso de extracción regular. Esta integración debe ser retrocompatible y sumar estabilidad sin romper los perfiles de proveedores existentes, acompañándose de mejoras en la UI para brindar mayor visibilidad del estado de ejecución (por ejemplo, mostrando claramente qué fase del descubrimiento se está ejecutando).

## What Changes

- Modificación del Wizard (interfaz de usuario) para incluir un campo de captura de la marca (brand) en el paso inicial o correspondiente.
- Actualización de la lógica de prueba del Wizard para limitar el número de productos extraídos a 10 durante la simulación/test.
- Asegurar que al finalizar el Wizard y guardar el perfil del proveedor, el valor `MaxProductsPerJob` o el parámetro equivalente se establezca por defecto en 120.
- Reutilización del motor de "descubrimiento y prueba" (usado en el Wizard) en la pantalla principal de Scraping.
- Implementación de un modelo híbrido/aditivo donde el proceso de descubrimiento se suma a la lógica de scraping actual para mejorar la fiabilidad.
- Ajustes de diseño y estructura en la interfaz de la pantalla principal de Scraping para mostrar feedback visual detallado del proceso en tiempo real.

## Capabilities

### New Capabilities
- `wizard-brand-capture`: Capacidad de definir la marca asociada a un proveedor directamente desde el Wizard de configuración.
- `wizard-test-limits`: Diferenciación entre el límite de productos para la fase de prueba en el Wizard (10) y el límite para operaciones reales guardadas (120).
- `scraping-screen-discovery-integration`: Integración de la lógica de descubrimiento y test (del Wizard) en el ciclo de scraping principal, manteniendo retrocompatibilidad y mejorando el feedback visual en la UI.

### Modified Capabilities

## Impact

- Interfaz de usuario del Wizard en `ScrapSAE.Desktop`.
- Lógica de testing de scraping invocada desde el Wizard y desde la pantalla de Scraping (`PlaywrightScrapingService` / `ScrapingRunner`).
- Lógica de persistencia de perfiles (`SiteProfile`).
- Interfaz principal de Scraping (`ScrapSAE.Desktop`), específicamente el layout para reportar estados granulares (como "Descubriendo familias", "Explorando paginación", "Extrayendo productos").


---
SOURCE: corpus/openspec/changes/wizard-brand-and-test-limits/tasks.md
---

## 1. UI Modificactions (Wizard)

- [x] 1.1 Add a text input field for "Marca" (Brand) in the `SiteUrlStepView` or the first step of the configuration Wizard in `ScrapSAE.Desktop`.
- [x] 1.2 Bind the new input field to a property in the corresponding ViewModel (e.g. `SiteProfile.SupplierBrand` or a property mapped to `SecondarySelectors["brand"]`).

## 2. Scraping Limits Configuration

- [x] 2.1 Locate the execution logic for the test step in the Wizard (e.g., `TestStepViewModel` or `IScrapingService` invocation).
- [x] 2.2 Temporarily set or pass a limit parameter of 10 for the `MaxProductsPerJob` (or equivalent test configuration) during the Wizard's test run.
- [x] 2.3 Locate the final step of the Wizard where the `SiteProfile` is saved.
- [x] 2.4 Force the `MaxProductsPerJob` (or equivalent saved limit) to 120 just before serializing and saving the configuration to ensure the background/manual scrapes start with that limit.

## 3. Discovery Integration in Scraping Runner

- [x] 3.1 Extract the core discovery logic from the Wizard (e.g. `ExtractProductsFromFamilyPageAsync`, `ExplorePaginationAsync`) into reusable methods in `PlaywrightScrapingService` if they are not already accessible.
- [x] 3.2 In `ScrapingRunner` or the entry point for the main scraping process, invoke this discovery step before or concurrently with the static URL processing.
- [x] 3.3 Ensure the discovered URLs are deduplicated and merged into the target processing list.
- [x] 3.4 Verify that for existing suppliers without complex pagination logic, this step either skips safely or completes without errors (retrocompatibility).

## 4. UI/UX Refactor for Scraping Screen

- [x] 4.1 Update `ScrapSAE.Desktop`'s main Scraping view (e.g., `ScrapingViewModel` / `ScrapingView.xaml`).
- [x] 4.2 Add a new UI control (like a Progress Bar, Stepper, or dynamic Status text block) below "Estadísticas de Ejecución" to report granular phases: "Descubrimiento de Catálogo", "Resolución de Paginación", "Extracción".
- [x] 4.3 Enhance the "Estado" badge to use color-coding (e.g., Green for Exploring, Blue for Extracting).
- [x] 4.4 Provide a filtering toggle on the real-time Console (e.g., "Ver solo Errores") to reduce verbosity.

## 5. Verification

- [x] 5.1 Run the Wizard from the Desktop application.
- [x] 5.2 Verify the "Marca" field is present, enter a test value, and confirm it's present in the saved `SiteProfile`.
- [x] 5.3 Ensure the test execution stops after processing 10 products instead of the full 120.
- [x] 5.4 Open the saved configuration file and verify that the product processing limit is indeed 120 for subsequent jobs.
- [x] 5.5 Start a normal Scraping job from the main UI and verify that the Discovery phase runs, updates the new granular UI indicators, and correctly extracts products.

