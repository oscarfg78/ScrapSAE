# Auditoría estratégica de ScrapSAE

## Restricciones de trabajo

- No modificar el repositorio ni generar código nuevo antes de la validación del usuario.
- Analizar el estado efectivo del árbol de trabajo, incluidas modificaciones locales y archivos no versionados.
- Usar OpenSpec en modo de exploración: lectura, trazado y clarificación; no aplicar ni archivar cambios.
- Mantener intactos todos los cambios locales existentes.

## Inventario inicial

- Repositorio local: `C:\Proyectos\ScrapSAE`.
- Rama: `extension1`, cuatro commits por delante de `origin/extension1` al iniciar la auditoría.
- No existe `AGENTS.md` en el proyecto, `C:\Proyectos` ni `C:\`.
- Componentes principales: API, Core, Desktop, Extension, Infrastructure, Web, Worker y suites de pruebas.
- Corpus documental versionado: 29 archivos bajo `docs`.
- Corpus OpenSpec versionado: 58 archivos; además existen cambios OpenSpec no versionados que representan el trabajo más reciente.
- OpenSpec está configurado con esquema `spec-driven` y su CLI está disponible.

## Estado local relevante

Archivos modificados existentes antes del análisis:

- `src/ScrapSAE.Api/Program.cs`
- `src/ScrapSAE.Api/Services/ScrapingRunner.cs`
- `src/ScrapSAE.Core/DTOs/DTOs.cs`
- `src/ScrapSAE.Core/DTOs/PageAnalysisDTOs.cs`
- `src/ScrapSAE.Core/Interfaces/IProviderScraperStrategy.cs`
- `src/ScrapSAE.Core/Interfaces/IServices.cs`
- `src/ScrapSAE.Desktop/ViewModels/ProviderWizardViewModel.cs`
- `src/ScrapSAE.Desktop/Views/ProviderWizardView.xaml`
- `src/ScrapSAE.Infrastructure/AI/OpenAIProcessorService.cs`
- `src/ScrapSAE.Infrastructure/AI/PageAnalysisService.cs`
- `src/ScrapSAE.Infrastructure/Scraping/PlaywrightScrapingService.cs`
- `src/ScrapSAE.Infrastructure/Scraping/Strategies/DirectExtractionStrategy.cs`
- `src/ScrapSAE.Infrastructure/Scraping/StrategyOrchestrator.cs`
- `src/ScrapSAE.Worker/Worker.cs`
- `tests/ScrapSAE.Api.Tests/ApiUnitTests.cs`
- `tests/ScrapSAE.Api.Tests/Stubs/StubScrapingService.cs`

Archivos o directorios no versionados existentes antes del análisis:

- `docs/architecture-scraping.md`
- `openspec/changes/align-process-execution-with-wizard-methods/`
- `openspec/changes/enhance-wizard-simulation/`
- `openspec/changes/fix-wizard-and-support-xpath/`
- `openspec/changes/improve-selector-extraction-analysis/`
- `src/ScrapSAE.Core/DTOs/ScrapeExecutionContext.cs`

## Estado de cambios OpenSpec

| Cambio | Tareas | Estado declarado |
|---|---:|---|
| improve-selector-extraction-analysis | 10/10 | complete |
| enhance-wizard-simulation | 7/7 | complete |
| fix-wizard-and-support-xpath | 6/6 | complete |
| align-process-execution-with-wizard-methods | 18/23 | in-progress |
| improve-product-detail-extraction | 0/7 | in-progress |
| wizard-brand-and-test-limits | 19/19 | complete |
| improve-product-details-extraction | 9/9 | complete |
| customize-supplier-brand-specs | 13/13 | complete |
| shopify-scraping-strategy | 13/13 | complete |
| add-provider-wizard | 40/49 | in-progress |

## Observaciones iniciales

El árbol de trabajo contiene una evolución activa del wizard y del pipeline de scraping. La auditoría debe distinguir explícitamente entre: especificaciones base, cambios OpenSpec activos o completados aún no archivados, implementación confirmada, implementación local no confirmada y documentación potencialmente desactualizada. El estado `complete` de una lista de tareas no se considerará evidencia suficiente de funcionamiento: se contrastará contra código, registro de dependencias, pruebas y flujo de ejecución real.

## Hallazgos de código: contratos iniciales

### `ScrapeExecutionContext.cs`

El record existe y encapsula `IsHeadless`, `ManualLogin`, `KeepBrowser`, `ScreenshotFallback` y `MaxProductsOverride`, cumpliendo parcialmente el cambio de alineación. Sin embargo, `ScrapeExecutionContext.WizardTest` fija `MaxProductsOverride = 2` y los comentarios también hablan de dos productos, mientras que la especificación vigente reconstruida exige **10 productos para demo**. Esta divergencia es evidencia directa y debe rastrearse hasta los callers; también puede coexistir con otros límites definidos en el ViewModel o el perfil temporal.

### `DTOs.cs`

`ScrapedProduct` contiene datos brutos y enriquecidos en el mismo objeto: SKU, título, descripción, HTML, captura base64, imagen principal y galería, precio, categoría, marca, `SourceUrl`, atributos, URLs de navegación, adjuntos, `AiEnriched` y `CharacteristicsHtml`. No incluye provenance por campo, nombre de estrategia, selector utilizado, estado de validación ni colección de diagnósticos.

`ProcessedProduct` es un segundo contrato con SKU, nombre, marca, modelo, descripción, características, especificaciones, categorías, precio, moneda, stock, imágenes, adjuntos, confianza y datos originales. Tampoco registra lineage por campo o por estrategia.

`SiteSelectors` es un modelo tipado extenso con selectores de lista, tarjeta, enlace, búsqueda, campos básicos, paginación, variantes, familias, detalle, galería, adjuntos y stock. Convive conceptualmente con los diccionarios `Selectors` y `SecondarySelectors` descritos por OpenSpec; debe verificarse si ambas representaciones se traducen de forma uniforme o se bifurcan.

`DirectUrlScrapeOptions` introduce otro pathway con `InspectOnly`, `SingleProductOnly` y `ExpandRelated`. `OperationResult<T>`, `DirectUrlResult` e `InspectUrlsResponse` aportan contratos de inspección directa separados del resultado del runner. Esto confirma que hay más de un modelo de resultado y que aún no existe un resultado canónico común para demo y producción.

## Traza efectiva: wizard → API → runner → pathways

### Wizard y demo

`ProviderWizardViewModel.ExecuteAnalyzeAsync` llama `ApiClient.AnalyzePageAsync` y copia `CandidateDetailUrl` cuando el usuario no dio una URL de detalle. `PopulateConfigFromAnalysis` convierte cada `DualSelector` a `string` mediante `ToString()`, por lo que almacena **JSON serializado dentro de valores string** de `WizardConfig`; después `BuildSiteProfile` coloca esos strings en un `Dictionary<string,string>` con claves `productContainer`, `productCard`, `sku`, `name`, `image`, `price` y `characteristics`. Esta codificación necesita un normalizador común para que CSS/XPath no se interpreten de forma distinta entre estrategias.

`ExecuteRunTestScrapeAsync` persiste un `SiteProfile` temporal, lo vuelve a actualizar con `MaxProductsPerScrape = 5`, invoca el endpoint normal `api/scraping/run/{id}` y luego consulta **todos** los staging products para filtrar por `SiteId` y limitar el preview a 10. Por tanto, sí reutiliza la ejecución normal, pero el test no es side-effect-free y existen tres límites discordantes: **2** en `ScrapeExecutionContext.WizardTest`, **5** en el perfil temporal efectivo y **10** en el preview/especificación más reciente. El `ScrapeExecutionContext.WizardTest` ni siquiera se transmite desde `ApiClient.RunScrapingAsync`; el endpoint tampoco acepta `MaxProductsOverride`.

El preview se reconstruye desde `StagingProduct.AIProcessedJson`, no desde el resultado de extracción del runner; solo muestra SKU, nombre, primera imagen, precio, conteo de especificaciones y URL. No conserva estrategia, selector, valor crudo, confidence por campo, errores ni trazas. Si el procesamiento/persistencia falla aunque la extracción fuera correcta, la demo aparece vacía. Al guardar, se renombra/reutiliza el site temporal y se fija 120; al cancelar se intenta eliminar y se delega a limpieza best-effort.

### Contratos y endpoints

`PageAnalysisResult` implementa `DualSelector` CSS/XPath, `StrategyType`, detail URL candidata, secundarios, recomendaciones, campos detectados y resumen. `DetectedField`, sin embargo, solo tiene un `Selector` string y confianza; el preview no expone esas confianzas. `SiteProfile.Selectors` es `object` JSONB mientras `SiteSelectors` es un tipo distinto y extenso; la coexistencia explica conversiones repetidas y riesgo de pérdida de contrato.

`ApiClient.RunScrapingAsync` transmite headless/manualLogin/keepBrowser/screenshotFallback, pero no un override de límite. `Program.cs` construye `ScrapeExecutionContext` sin `MaxProductsOverride`. `Program.cs` registra tres `IScrapingStrategy` genéricas y un `StrategyOrchestrator`, además de dos `IProviderScraperStrategy` keyed: `GenericPlaywrightStrategy` y `ShopifyApiStrategy`.

El endpoint alternativo `/api/scraping/inspect/{siteId}` sigue configurando `SCRAPSAE_HEADLESS`, `SCRAPSAE_FORCE_MANUAL_LOGIN`, `SCRAPSAE_DIRECT_URLS` y `SCRAPSAE_INSPECT_ONLY` como variables de entorno globales, llama `ScrapeDirectUrlsAsync` y restaura el estado en `finally`. No es seguro frente a concurrencia.

### `ScrapingRunner`

`RunForSiteAsync` declara que el perfil es la única fuente de verdad, pero inmediatamente proyecta el contexto a variables de entorno globales. Carga URLs aprendidas, las serializa en `SCRAPSAE_LEARNED_URLS`, llama `DiscoverProductUrlsAsync`, mezcla por URL con `HashSet` y vuelve a publicar el pool en esa variable. El error de descubrimiento está aislado y no aborta la ejecución. El routing superior sí usa `StrategyType` para resolver la estrategia keyed, cayendo a `Generic`, pero la estrategia genérica delega al servicio Playwright donde aún existen múltiples rutas.

El runner procesa y persiste los productos después del scrape, ejecuta análisis post-ejecución y puede aplicar sugerencias automáticamente. Debe verificarse que esa autoactualización no altere los selectores confirmados por el wizard ni rompa reproducibilidad. Sus helpers posteriores realizan deep enrichment adicional y mantienen reglas hardcodeadas por nombre de proveedor, incluida marca/categoría, según el panorama del archivo.

### `StrategyOrchestrator`

Ejecuta estrategias secuencialmente por prioridad, devuelve en la primera que produzca cualquier producto y atrapa excepciones para continuar. Esto implementa fallback y aislamiento básico, pero **no combinación de resultados** ni criterio de calidad: un producto parcial bloquea pathways posteriores. Si hay estrategias explícitas, auto-inyecta `List` cuando detecta `productContainer`, contradiciendo la fuente de verdad estricta. Sin estrategias usa Direct → List → Families.

### `PlaywrightScrapingService`

`DiscoverProductUrlsAsync` abre la URL base y llama `DiscoverRelatedProductUrlsAsync`, pero su comentario “no aplica si ya existen URLs” no coincide con el cuerpo, que no comprueba URLs existentes. Usa `site.MaxProductsPerScrape` y tiene aislamiento best-effort.

En `ScrapeAsync`, el orden efectivo empieza por `SCRAPSAE_DIRECT_URLS`, luego `SCRAPSAE_LEARNED_URLS`; cualquiera de esos pools causa retorno inmediato mediante `ScrapeDirectUrlsAsync`, por lo que omite el orquestador. Después convierte `site.Selectors` a `SiteSelectors`, valida heurísticamente y contiene un caso por nombre `Festo`. El contexto tipado se usa en parte, pero persisten variables de entorno para login y URL inicial. Tras abrir navegador, intenta el `StrategyOrchestrator`; si no hay resultados, entra en una ruta legacy extensa con heurísticas Festo/Searchanise/families/búsqueda y detalle. La arquitectura real es, por tanto, una cadena de shortcuts y fallback legacy, no un pipeline declarativo único.

## Estrategias concretas, autoactualización y pathways laterales

### Estrategias `Direct`, `List` y `Families`

`DirectExtractionStrategy` y `ListExtractionStrategy` contienen su propio parser de `SiteProfile.Selectors`: aceptan tanto objetos `DualSelector` como JSON stringificado y hacen fallback CSS→XPath. Esa tolerancia oculta la degradación contractual del wizard en vez de resolverla en una capa común. `Direct` exige simultáneamente SKU y título; `List` solo exige título, por lo que sus criterios de éxito no son equivalentes. Ambos duplican resolución de selector y parseo de precio.

`ListExtractionStrategy` necesita `productContainer` y `productCard`, no aplica `MaxProductsPerScrape`, no pagina y asigna el listado como `SourceUrl` salvo que encuentre `detailLink`; el wizard/análisis no genera explícitamente `detailLink` en su modelo principal, por lo que cae al primer `<a>` de la tarjeta. Una lista parcial puede ser considerada éxito y detener el orquestador, impidiendo enriquecimiento posterior por otras estrategias.

`FamiliesExtractionStrategy` usa un contrato distinto y frágil: `GetSelector` solo funciona si `site.Selectors` es exactamente `Dictionary<string,object>`, no soporta `DualSelector`, y requiere claves `familyLink`, `variantTable`, `variantRow`, `variantSku`, `variantName`, `variantPrice` que el wizard actual no construye. Navega secuencialmente por todas las familias y tampoco respeta `MaxProductsPerScrape`. En el estado actual no es realmente intercambiable con Direct/List.

### Shopify

`ShopifyApiStrategy` es un pathway keyed independiente que obtiene `/products.json`, pagina y respeta `site.MaxProductsPerScrape`, pero trae páginas de 250 y solo corta después de agregar la página completa: puede superar el límite. Usa únicamente la primera variante y la primera imagen, no expone inventario/adjuntos/atributos y `ScrapeDirectUrlsAsync` lanza `NotImplementedException`. Por ello no puede participar de forma uniforme en enriquecimiento directo ni combinarse con el pathway genérico bajo el mismo contrato.

### Análisis IA

`PageAnalysisService` sí ejecuta análisis de catálogo y detalle en una misma petición: carga catálogo, detecta Shopify, recorta el DOM, usa la URL de detalle del usuario o descubre una candidata y envía ambos skeletons a GPT. La salida estricta exige `DualSelector` para siete campos, secundarios, estrategias recomendadas, campos detectados y resumen. Sin embargo, no incluye selectores `detailLink`, `familyLink`, `variant*`, paginación, login, atributos ni capacidades; por ello puede recomendar `Families` sin producir la configuración mínima que esa estrategia requiere. La confianza vive en `DetectedFields`, no en los selectores persistidos.

### Autoactualización post-ejecución

La implementación `ScrapSAE.Api.Services.ConfigurationUpdaterService` que usa `ScrapingRunner` aplica automáticamente sugerencias con confianza ≥0.7. Interpreta `PropertyName` como clave de selector y consulta/parchea rutas `sites`/`selectors_json`, mientras el CRUD principal opera sobre `config_sites` y `SiteProfile.Selectors`; esto sugiere desalineación de tabla/esquema. Sobrescribe sin preservar secundarios y guarda historial solo en memoria. En una estrategia reproducible, la demo/wizard no debe activar mutación automática; las propuestas deben ser versionadas, validadas y aprobadas antes de promoverse.

### Cobertura automatizada

Las pruebas existentes del runner cubren inserción en staging, payload hacia IA y fallback estructurado, usando `StubScrapingService` o mocks. No ejercitan el wizard, `/api/sites/analyze`, `CandidateDetailUrl`, serialización de `DualSelector`, `MaxProductsOverride`, selección keyed, Shopify, descubrimiento, concurrencia de env vars, combinación de pathways ni paridad demo/producción. Las pruebas E2E desktop solo verifican apertura/título y una pestaña; no recorren el wizard.

### Extensión de navegador

La extensión constituye otro extractor independiente. `extractor.ts` usa selectores CSS propios (`SiteSelectors` TypeScript), extrae listado y variantes en el DOM, pagina o hace scroll, y después enriquece productos parciales mediante `fetch` de URLs de detalle y merge conservador. Tiene un contrato de claves distinto al wizard/Core (`titleSelector`, `productListSelector`, `detail*`, `variant*`, etc.) y no soporta XPath. El endpoint `/api/extension/process` solo serializa productos y llama `IAIProcessorService`; devuelve `ProcessedProduct` sin pasar por `ScrapingRunner.ProcessScrapedProductsAsync`, staging, deduplicación, deep enrichment, trazas o aprendizaje. Es un pathway válido, pero hoy está aislado del pipeline común y no puede combinarse coherentemente con los extractores servidor.

## Traza end-to-end confirmada

### Wizard → análisis → demo → guardado

El wizard llama al análisis de catálogo/detalle y recibe `PageAnalysisResult`, pero al poblar la configuración transforma cada `DualSelector` mediante `ToString()` a un JSON embebido en `Dictionary<string,string>`. Conserva `SecondarySelectors` aparte, pero no persiste la confianza ni la justificación por selector. Las prioridades recomendadas por IA tampoco se conservan: se reducen a booleanos y luego se reconstruyen fijas como Direct=1, List=2, Families=3.

Para la demo, el wizard crea y persiste un proveedor `[TEMP]`, lo vuelve a actualizar con `MaxProductsPerScrape=5`, llama al mismo endpoint de ejecución normal y después consulta toda la tabla de staging, filtra por `SiteId` y toma hasta 10. Por tanto, la demo sí atraviesa runner/postproceso/staging, pero no es una ejecución aislada ni de solo lectura: crea estado persistente, ejecuta post-análisis y puede auto-modificar configuración. El contrato `ScrapeExecutionContext.WizardTest`, que define `MaxProductsOverride=2`, no se usa. La demo y producción se diferencian por mutar temporalmente el perfil, no por un límite por ejecución.

El preview se reconstruye desde `StagingProduct.AIProcessedJson`, no desde el resultado crudo ni desde evidencias por pathway. Muestra SKU, nombre, primera imagen, precio, conteo de especificaciones y campos encontrados/faltantes. No permite ver qué strategy/pathway aportó cada campo, selectores usados, confianza, warnings, errores, URLs descubiertas, adjuntos, variantes, ni resultados descartados. `TotalProductsFound` procede del runner, mientras la colección preview procede de una consulta separada a staging, lo que abre posibilidad de inconsistencia temporal.

Al guardar, el mismo proveedor temporal se renombra/actualiza con `MaxProductsPerScrape=120`. Si la prueba falla, el usuario todavía puede quedar en el flujo sin una regla explícita de aprobación basada en calidad. `RequiresLogin=false` se fija siempre y el contexto del análisis no deriva login/paginación/capacidades.

### Runner y selección de motores

`ScrapingRunner` carga el perfil, enriquece selectores, crea contexto de control y propaga opciones mediante variables de entorno globales de proceso. También carga URLs aprendidas y, si no hay `SCRAPSAE_DIRECT_URLS`, ejecuta descubrimiento aditivo, mezcla learned+discovered y vuelve a publicar el pool mediante `SCRAPSAE_LEARNED_URLS`. Esto viola aislamiento por ejecución y es vulnerable a contaminación entre ejecuciones concurrentes; además el `finally` restaura headless/login/browser/screenshot, pero en el rango auditado no restaura explícitamente `SCRAPSAE_LEARNED_URLS`.

La selección de nivel proveedor usa keyed `IProviderScraperStrategy` por `StrategyType`: Shopify o Generic, con fallback a Generic. Dentro de Generic, `PlaywrightScrapingService` tiene otro plano de estrategia (`IScrapingStrategy`: Direct/List/Families), más múltiples fallbacks legacy. Son dos niveles de orquestación con vocabularios solapados pero contratos diferentes.

Después de extraer, todos los productos del runner pasan por `ProcessScrapedProductsAsync`; su enriquecimiento incremental puede volver a llamar `ScrapeDirectUrlsAsync` sobre hasta cuatro URLs candidatas y hace merge si parecen el mismo producto. Este es un punto común útil, pero la extensión no lo utiliza y Shopify direct URL no está implementado.

El análisis post-ejecución se ejecuta también en la demo y puede aplicar sugerencias automáticamente a la configuración persistida. Esto impide que una prueba sea reproducible y estrictamente no destructiva.

### Persistencia y compatibilidad

`SiteProfileSchemaCompatibility` intenta persistir columnas avanzadas y, si faltan, embebe secundarios, estrategias y `StrategyType` dentro de `Selectors` mediante claves legacy en `config_sites`. En lectura los restaura y retira esas claves. Este fallback evita pérdida total, pero deja múltiples representaciones del mismo contrato y obliga a normalización ad hoc. Contrasta con el actualizador post-ejecución, que usa rutas `sites`/`selectors_json`, reforzando el riesgo de que la autoactualización escriba en un esquema distinto del CRUD del wizard.

## Auditoría de módulos comunes y paridad de ejecución

### Incompatibilidad de vocabularios de selector

El wizard persiste claves `productContainer`, `productCard`, `sku`, `name`, `image`, `price`, `characteristics`. El modelo `SiteSelectors` usado por el motor Playwright espera `ProductListSelector`, `ProductLinkSelector`, `TitleSelector`, `SkuSelector`, etc. `FillSelectorsFromJson` admite PascalCase/camelCase/snake_case de esas propiedades, pero **no** admite los alias del wizard (`productContainer`, `productCard`, `name`, `sku`, `image`, `price`). Además, su `GetSelector` solo devuelve valores JSON string y no interpreta objetos `DualSelector`; cuando recibe el JSON string producido por `DualSelector.ToString()`, lo conserva como texto literal.

Las estrategias internas `Direct` y `List` sí saben leer las claves del wizard y parsear un `DualSelector` embebido. Por ello, el sistema funciona de manera accidental solo si llega al `StrategyOrchestrator`. Cualquier ruta que pase antes por `SiteSelectors` —descubrimiento, scraping directo, múltiples fallbacks legacy— queda desalineada con el perfil generado por el wizard.

### El supuesto descubrimiento aditivo sustituye el flujo

`ScrapingRunner` denomina “aditiva” a la fase de descubrimiento, mezcla discovered+learned y publica el pool en `SCRAPSAE_LEARNED_URLS`. Sin embargo, al entrar en `PlaywrightScrapingService.ScrapeAsync`, la presencia de esa variable provoca un **retorno temprano** a `ScrapeDirectUrlsAsync`; no se ejecutan el `StrategyOrchestrator` ni los fallbacks estándar. Por tanto, el descubrimiento no potencia el pathway configurado: lo reemplaza.

`ScrapeDirectUrlsAsync` vuelve a deserializar `site.Selectors` como `SiteSelectors`; con el perfil del wizard suele perder los selectores principales. En modo no-single usa `ExtractFestoProductsFromDetailPageAsync` incluso para URLs genéricas. La expansión de relacionados solo se activa para Festo y luego puede navegar recursivamente con un máximo hardcodeado de 10000, aunque el bucle exterior comprueba `MaxProductsPerScrape`; el callback recursivo recibe un límite distinto. Esto no constituye un pathway genérico, independiente ni uniformemente limitado.

### Aislamiento insuficiente

Aunque `ScrapeExecutionContext` se pasa a `ScrapeAsync`, el runner convierte varias propiedades en variables globales de proceso. Las URLs directas/aprendidas y marcadores de login manual también son globales. El endpoint de ejecución solo construye contexto con login/headless/keep browser/screenshot; no expone `MaxProductsOverride` ni usa `WizardTest`. La demo no puede expresar límites, mutación permitida, pathways habilitados o persistencia como opciones tipadas por ejecución.

## Wizard, presentación y verificabilidad

El análisis produce URL candidata de detalle, idioma, selectores duales, secundarios, recomendaciones con prioridad/razón, campos con confianza/nota y resumen. El paso 2 muestra resumen, `DetectedFields` y recomendaciones. Sin embargo, al pasar a configuración se pierden idioma, timestamp, razones, prioridades originales y confianza como datos operativos; los selectores duales aparecen en los `TextBox` como JSON serializado, aunque la UI los etiqueta como “Selectores CSS”, lo que induce a editar un formato que no es CSS puro.

La UI permite habilitar Direct/List/Families mediante checkboxes, pero no muestra capacidades, precondiciones, coste, timeout, muestras, fallback/ensemble, ni dependencias. Tampoco ofrece orden editable: la prioridad vuelve a fijarse en código. No hay sección para selectores secundarios, paginación, login, URLs semilla, Shopify, detalle, variantes, adjuntos o límites.

Durante el test, el paso 4 anuncia literalmente “Información Simulada”, aunque el código ejecuta scraping real y persiste en staging; esto contradice el comportamiento. Tras obtener productos, el ViewModel calcula coberturas y cambia inmediatamente a `CurrentStep=5`, de modo que la tabla detallada del paso 4 no queda como revisión estable. El botón “Ver resultados” del paso 4 está enlazado a `GoToConfigCommand`, cuyo manejador vuelve al paso 3, no al paso 5. El paso 5 muestra “Test exitoso” por la mera existencia de al menos un producto, sin umbral de aceptación por campos, errores, pathway o calidad.

El preview no muestra `ImageUrl`, `SourceUrl`, `MissingFields`, adjuntos, stock, moneda, galería, descripción, categorías, variantes, evidencia de selector, confianza ni procedencia por strategy, aunque los módulos comunes pueden producir muchos de esos campos. Por ello no satisface la necesidad de “mostrar la información extraída después del análisis y de la ejecución en modo demo” de forma auditable.

La cancelación de la ventana ejecuta el comando async sin esperar su finalización y cierra tras 200 ms. La eliminación del temporal es best-effort; si falla queda hasta 60 minutos y depende de un servicio cada 15 minutos. Ese servicio también elimina proveedores no temporales duplicados por nombre, conservando el de `CreatedAt` más reciente; es un efecto lateral no relacionado con la demo y potencialmente destructivo.

La búsqueda exhaustiva en `tests/**/*.cs` devuelve cero referencias a `ProviderWizard`, `PageAnalysis`, `/api/sites/analyze`, `WizardTest`, `MaxProductsOverride`, `StrategyOrchestrator`, las cuatro estrategias concretas, `SecondarySelectors` y `RecommendedStrategies`. Las pruebas HTTP sustituyen el `IScrapingService` real por un stub, por lo que no validan el circuito crítico ni la combinabilidad real.
