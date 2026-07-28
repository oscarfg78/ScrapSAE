

---
SOURCE: corpus/openspec/changes/add-provider-wizard/specs/page-analysis-ai/spec.md
---

## ADDED Requirements

### Requirement: Endpoint de análisis de página
El sistema SHALL exponer el endpoint `POST /api/sites/analyze` que reciba una URL, descargue el HTML renderizado de la página usando Playwright y lo analice con GPT para detectar la estructura del catálogo de productos.

#### Scenario: Solicitud válida retorna análisis estructurado
- **WHEN** el cliente envía `POST /api/sites/analyze` con body `{ "url": "https://proveedor.com/catalogo" }`
- **THEN** el API retorna HTTP 200 con un objeto `PageAnalysisResult` incluyendo: selectores primarios, selectores secundarios, estrategias recomendadas, campos detectados con nivel de confianza, y un resumen textual del análisis

#### Scenario: URL inaccesible retorna error descriptivo
- **WHEN** Playwright no puede cargar la URL (timeout, DNS error, etc.)
- **THEN** el API retorna HTTP 422 con un mensaje descriptivo del error de acceso

#### Scenario: Timeout de análisis
- **WHEN** el proceso de descarga + análisis supera 30 segundos
- **THEN** el API retorna HTTP 408 con mensaje "El análisis superó el tiempo límite"

### Requirement: Descarga HTML con Playwright (modo headless)
El servicio de análisis SHALL usar Playwright en modo headless para renderizar completamente la página (incluyendo JavaScript) antes de extraer el HTML, esperando al evento `networkidle` para asegurar que el contenido dinámico esté cargado.

#### Scenario: Página con contenido dinámico se carga completamente
- **WHEN** la URL corresponde a una SPA o página con carga asíncrona de productos
- **THEN** el HTML extraído contiene los elementos del catálogo (no solo el esqueleto vacío)

#### Scenario: HTML se trunca para optimizar tokens
- **WHEN** el HTML del body supera 50,000 caracteres
- **THEN** el sistema extrae los primeros 50,000 caracteres del body, priorizando las secciones con mayor densidad de elementos de lista (`<ul>`, `<div>` repetidos)

### Requirement: Análisis IA con GPT para detección de estructura
El servicio SHALL enviar el HTML truncado a GPT con un prompt especializado en detección de catálogos de productos de e-commerce, solicitando structured output con el esquema `PageAnalysisResult`.

#### Scenario: GPT detecta estructura de lista de productos
- **WHEN** la página contiene una lista o grid de tarjetas de productos
- **THEN** el análisis retorna el selector CSS del contenedor de lista y el selector de cada tarjeta de producto individual

#### Scenario: GPT detecta campos de datos de producto
- **WHEN** la página contiene elementos con SKU, nombre, imagen y precio
- **THEN** el análisis retorna selectores CSS individuales para cada campo con nivel de confianza `high`, `medium` o `low` según la certeza de la detección

#### Scenario: GPT detecta campos con nivel de confianza
- **WHEN** un campo (por ejemplo, precio) no está claramente presente en el HTML analizado
- **THEN** el campo correspondiente tiene nivel de confianza `low` y el selector es `null` o una sugerencia tentativa

#### Scenario: GPT recomienda estrategia de scraping
- **WHEN** la página es una lista de productos directa (no paginada por familias o categorías)
- **THEN** la estrategia recomendada es `Direct` con prioridad 1

#### Scenario: GPT recomienda estrategia por familias
- **WHEN** la página contiene categorías o familias de productos que llevan a sub-listas
- **THEN** la estrategia recomendada incluye `Families` con prioridad 1 y `List` como fallback

### Requirement: Estructura del DTO PageAnalysisResult
El sistema SHALL retornar un DTO `PageAnalysisResult` con los siguientes campos obligatorios y opcionales que permitan al wizard poblar directamente el formulario de configuración del `SiteProfile`.

#### Scenario: Resultado completo con todos los campos
- **WHEN** el análisis es exitoso y detecta todos los campos
- **THEN** el `PageAnalysisResult` contiene: `ProductContainerSelector` (string), `ProductCardSelector` (string), `SkuSelector` (string?), `NameSelector` (string), `ImageSelector` (string?), `PriceSelector` (string?), `CharacteristicsSelector` (string?), `SecondarySelectors` (Dictionary<string, List<string>>), `RecommendedStrategies` (List<StrategyRecommendation>), `DetectedFields` (List<DetectedField> con Name, Selector, Confidence), `AnalysisSummary` (string), `PageTitle` (string), `DetectedLanguage` (string)

#### Scenario: Resultado parcial cuando página no es catálogo de productos
- **WHEN** la página analizada no parece ser un catálogo de productos (es una home, blog, etc.)
- **THEN** el `PageAnalysisResult` retorna `IsProductCatalog = false` y un `AnalysisSummary` explicando por qué no se pudo detectar la estructura

### Requirement: Limpieza de sites temporales
El sistema SHALL eliminar automáticamente los `SiteProfile` con nombre prefijado `[TEMP]` que tengan más de 1 hora de antigüedad, para evitar acumulación de datos de prueba huérfanos.

#### Scenario: Job de limpieza elimina sites temporales expirados
- **WHEN** existen registros en `config_sites` con `Name` iniciando con `[TEMP]` y `CreatedAt` hace más de 60 minutos
- **THEN** el sistema los elimina en la próxima ejecución del job de limpieza (que corre cada 15 minutos)


---
SOURCE: corpus/openspec/changes/add-provider-wizard/specs/provider-wizard/spec.md
---

## ADDED Requirements

### Requirement: Iniciar wizard desde botón principal
El sistema SHALL proporcionar un botón "Agregar Proveedor" visible en la pantalla principal de ScrapSAE.Desktop que abra el wizard de creación de proveedores como ventana modal.

#### Scenario: Usuario abre el wizard
- **WHEN** el usuario hace clic en "Agregar Proveedor" en la pantalla principal
- **THEN** se abre una ventana modal `ProviderWizardView` en el Paso 1 (Ingreso de URL), con todos los campos vacíos y limpios

### Requirement: Paso 1 — Ingreso de URL
El wizard SHALL solicitar al usuario la URL base del catálogo de productos del proveedor en el Paso 1, con validación de formato antes de permitir avanzar.

#### Scenario: URL válida permite continuar
- **WHEN** el usuario ingresa una URL con esquema http o https y hace clic en "Analizar"
- **THEN** el wizard avanza al Paso 2 mostrando un indicador de carga mientras solicita el análisis al API

#### Scenario: URL inválida bloquea avance
- **WHEN** el usuario hace clic en "Analizar" con una URL vacía o con formato inválido
- **THEN** se muestra un mensaje de error de validación y el wizard no avanza

### Requirement: Paso 2 — Análisis IA de la página
El wizard SHALL llamar al endpoint `POST /api/sites/analyze` con la URL proporcionada y mostrar al usuario los resultados del análisis IA, incluyendo selectores sugeridos, campos detectados y nivel de confianza por campo.

#### Scenario: Análisis exitoso muestra resultados
- **WHEN** el API retorna un `PageAnalysisResult` exitoso
- **THEN** el wizard muestra: selectores primarios y secundarios sugeridos, estrategia recomendada, lista de campos detectados (SKU, nombre, imagen, precio, características) con indicador de confianza (Alta/Media/Baja) por cada uno

#### Scenario: Análisis falla por timeout o error
- **WHEN** el API retorna un error (timeout, página no accesible, etc.)
- **THEN** el wizard muestra un mensaje de error descriptivo y un botón "Reintentar" sin perder la URL ingresada

#### Scenario: Análisis en progreso muestra spinner
- **WHEN** el wizard está esperando la respuesta del análisis (puede tardar hasta 30s)
- **THEN** se muestra un spinner de carga con el mensaje "Analizando estructura del catálogo..." y un botón "Cancelar"

### Requirement: Paso 3 — Revisión y ajuste de configuración
El wizard SHALL mostrar la configuración propuesta por la IA en un formulario editable donde el usuario pueda ajustar los selectores, el nombre del proveedor y las estrategias de scraping antes de ejecutar el test.

#### Scenario: Usuario puede editar selectores propuestos
- **WHEN** el wizard está en el Paso 3
- **THEN** todos los campos de configuración (nombre, selectores primarios, selectores secundarios, estrategias) son editables con los valores propuestos por la IA pre-poblados

#### Scenario: Campos obligatorios validados
- **WHEN** el usuario hace clic en "Ejecutar Test" en el Paso 3
- **THEN** el sistema valida que el nombre del proveedor no esté vacío y que al menos un selector de producto esté definido; si no, muestra errores inline

### Requirement: Paso 4 — Test de scraping de validación
El wizard SHALL ejecutar un scrape de prueba con la configuración propuesta (máximo 120 productos) y mostrar los resultados en tiempo real, permitiendo al usuario validar que el scraping funciona correctamente antes de guardar.

#### Scenario: Scrape de prueba exitoso muestra preview de productos
- **WHEN** el test de scraping extrae al menos 1 producto
- **THEN** el wizard muestra una tabla con los primeros productos extraídos, indicando para cada uno los campos obtenidos (SKU, imagen, nombre, precio, características) con íconos de check/advertencia/error

#### Scenario: Scrape de prueba sin resultados
- **WHEN** el test de scraping no extrae ningún producto
- **THEN** el wizard muestra un mensaje de advertencia "No se encontraron productos. Revisa los selectores." con un botón "Volver a ajustar" que regresa al Paso 3

#### Scenario: Límite de productos en test
- **WHEN** el site tiene más de 120 productos disponibles
- **THEN** el test extrae solo los primeros 120 y muestra "Mostrando 120 de N productos encontrados"

### Requirement: Paso 5 — Confirmación y guardado
El wizard SHALL guardar el `SiteProfile` en Supabase con los valores confirmados por el usuario y mostrar una pantalla de éxito con acceso directo al proveedor recién creado.

#### Scenario: Guardado exitoso del proveedor
- **WHEN** el usuario hace clic en "Guardar Proveedor" en el Paso 5
- **THEN** el sistema persiste el `SiteProfile` con `IsActive = true`, `RequiresLogin = false`, `MaxProductsPerScrape = 120`, y retorna al usuario a la pantalla principal con el nuevo proveedor seleccionado

#### Scenario: Error al guardar
- **WHEN** el API retorna un error al intentar guardar el SiteProfile
- **THEN** el wizard muestra un mensaje de error y permite reintentar sin perder la configuración

### Requirement: Cancelación del wizard sin guardar
El wizard SHALL permitir al usuario cancelar el proceso en cualquier paso sin crear ningún proveedor persistente.

#### Scenario: Cancelación durante test — elimina site temporal
- **WHEN** el usuario cancela el wizard después de que el test de scraping creó un site temporal (prefijado `[TEMP]`)
- **THEN** el sistema elimina automáticamente el site temporal de Supabase y cierra el wizard

#### Scenario: Cancelación antes del test — no crea datos
- **WHEN** el usuario cancela el wizard en los pasos 1, 2 o 3
- **THEN** el wizard se cierra sin crear ningún registro en Supabase


---
SOURCE: corpus/openspec/changes/align-process-execution-with-wizard-methods/specs/provider-discovery/spec.md
---

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


---
SOURCE: corpus/openspec/changes/align-process-execution-with-wizard-methods/specs/provider-wizard-product-detail/spec.md
---

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


---
SOURCE: corpus/openspec/changes/align-process-execution-with-wizard-methods/specs/scrape-execution-context/spec.md
---

## ADDED Requirements

### Requirement: ScrapeExecutionContext encapsula los parámetros de ejecución
El sistema SHALL exponer un tipo `ScrapeExecutionContext` (record inmutable en `ScrapSAE.Core.DTOs`) con las propiedades `IsHeadless`, `ManualLogin`, `KeepBrowser`, `ScreenshotFallback`, y `MaxProductsOverride` (nullable). El endpoint `api/scraping/run` SHALL construir este objeto desde los query params y pasarlo a `ScrapingRunner.RunForSiteAsync` en lugar de establecer environment variables.

#### Scenario: Endpoint construye el contexto correctamente
- **WHEN** el endpoint `POST /api/scraping/run/{siteId}` recibe query params `headless=true&manualLogin=false`
- **THEN** el sistema construye un `ScrapeExecutionContext { IsHeadless = true, ManualLogin = false }` y lo pasa al runner

#### Scenario: Ejecución concurrente no mezcla configuraciones
- **WHEN** dos solicitudes de scrape llegan simultáneamente con parámetros distintos (e.g., headless=true y headless=false)
- **THEN** cada ejecución usa su propio `ScrapeExecutionContext` sin interferencia entre ambas


---
SOURCE: corpus/openspec/changes/align-process-execution-with-wizard-methods/specs/strategy-driven-execution/spec.md
---

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


---
SOURCE: corpus/openspec/changes/customize-supplier-brand-specs/specs/supplier-specs-mapping/spec.md
---

## ADDED Requirements

### Requirement: Supplier brand override
The system SHALL support configuring a specific "brand" string for each supplier.

#### Scenario: Supplier has a brand configured
- **WHEN** configuring a supplier via the ScrapSAE API or database
- **THEN** the system persists the provided brand value associated with that supplier

### Requirement: Exclude specific specifications from online store payload
The system MUST NOT include the "source_url" or "supplier name" specifications in the product data sent to the Flashly integration or online store.

#### Scenario: Formatting scraped product for Flashly
- **WHEN** the integration service maps the scraped product specs to the target payload
- **THEN** the "source_url" specification is omitted from the resulting payload
- **THEN** the "supplier name" specification is omitted from the resulting payload

### Requirement: Apply supplier brand override to scraped product
The system SHALL replace any scraped "brand" specification value with the supplier's configured brand before sending it to the online store.

#### Scenario: Supplier brand override is applied
- **WHEN** the scraped product is mapped for the Flashly integration
- **THEN** if the supplier has a configured brand override, the product's "brand" specification is set to that value
- **THEN** if the supplier does NOT have a configured brand override, the product's "brand" specification retains its original scraped value (or is omitted if that's the default behavior)


---
SOURCE: corpus/openspec/changes/enhance-wizard-simulation/specs/main/spec.md
---

# Especificaciones de Funcionalidad

1. **AI Dual Extraction**: El prompt de la IA se actualiza para pedir {"css": "...", "xpath": "..."} en cada campo.
2. **Estrategia Resiliente**: GetSelector() eval�a si el string es un JSON parseable a DualSelector (o eval�a ambos). Si es un string simple mantiene retrocompatibilidad.
3. **Wizard Demo Mode**:
   - ProviderWizardViewModel.ExecuteRunTestScrapeAsync configurar� MaxProductsPerScrape = 5 temporalmente (antes era 2).
   - Se extraer� un listado base.
   - En la interfaz ProviderWizardView.xaml se a�adir� un indicador "Demo Mode" cuando se visualiza la tabla final del Test.


---
SOURCE: corpus/openspec/changes/fix-wizard-and-support-xpath/specs/provider-discovery/spec.md
---

## MODIFIED Requirements

### Requirement: Sugerencia de selectores y variables de extraccion por Inteligencia Artificial
La IA (OpenAIProcessorService) SHALL sugerir el mejor selector aplicable de forma inteligente para cada campo y propiedad. Esta sugerencia SHALL distinguir y reportar selectores CSS validos y rutas XPath funcionales, pre-configurando su viabilidad segun la estructura provista.

#### Scenario: Analisis de catalogos complejos
- **WHEN** el usuario ingresa a un sitio en el que no existen identificadores de clase directos ni tags estructurados simples, requiriendo busqueda relacional
- **THEN** la Inteligencia artificial provee selectores del tipo XPath asegurandose de incluir el estandar de deteccion que asimile la API, por lo general el prefijo '//' o 'xpath='.


---
SOURCE: corpus/openspec/changes/fix-wizard-and-support-xpath/specs/provider-wizard-product-detail/spec.md
---

## MODIFIED Requirements

### Requirement: Flujo de validacion y prueba en el Wizard (Test Scrape)
El Wizard SHALL ejecutar una prueba de extraccion de productos contra la pagina web usando los selectores ingresados. Para la ejecucion de la prueba, el orquestador de scraping y el contexto de ejecucion SHALL ser instanciados de manera que los selectores XPath y CSS sean transmitidos correctamente.

#### Scenario: Prueba exitosa desde la UI del Wizard
- **WHEN** el usuario da click en Test Scrape despues de obtener sugerencias de la IA
- **THEN** la prueba se realiza exitosamente recuperando los productos y evitando el error inmediato por ausencia de variables legacy, y soportando selectores XPath en caso de estipularse.


---
SOURCE: corpus/openspec/changes/fix-wizard-and-support-xpath/specs/xpath-selector-support/spec.md
---

## ADDED Requirements

### Requirement: Soporte para selectores XPath en extraccion de productos
El sistema SHALL permitir definir y procesar expresiones XPath como selectores para ubicar elementos durante el web scraping, de forma equivalente a como se procesan los selectores CSS.

#### Scenario: Analisis de URL y ejecucion del motor XPath
- **WHEN** un usuario ingresa un selector XPath o la IA lo sugiere para un campo
- **THEN** el motor de Playwright evalua exitosamente la ruta xpath y retorna los contenidos esperados sin error de parseo.


---
SOURCE: corpus/openspec/changes/improve-product-detail-extraction/specs/advanced-product-detail-extraction/spec.md
---

## ADDED Requirements

### Requirement: Complex Detail Extraction
The extraction strategy SHALL successfully parse complex nested HTML descriptions, such as `tab-content-description`, into a readable text format or a structured list (JSON string).

#### Scenario: Product with nested HTML in description
- **WHEN** the product description is inside deep nested elements
- **THEN** the system iterates through the child nodes and extracts relevant details, avoiding truncated text.


---
SOURCE: corpus/openspec/changes/improve-product-detail-extraction/specs/provider-wizard-product-detail/spec.md
---

## ADDED Requirements

### Requirement: Integration of Details Testing in Wizard
The Provider Wizard's Test step SHALL execute a detailed extraction test when a Product Detail URL was provided or successfully discovered as fallback.

#### Scenario: Test Step runs with Detail URL
- **WHEN** the user navigates to the "Test" step in the wizard
- **THEN** the wizard initiates a test that spans both catalog and detail extraction phases.


---
SOURCE: corpus/openspec/changes/improve-product-detail-extraction/specs/wizard-detail-testing/spec.md
---

## ADDED Requirements

### Requirement: Product Detail API Testing
The Provider Wizard API Test endpoint SHALL fetch the detail page for tested products (if a detail strategy is enabled) to validate the detail extraction.

#### Scenario: Testing a catalog with detail URLs
- **WHEN** the user runs the "Test" step and a detail page strategy was found
- **THEN** the test endpoint also fetches and extracts details from the detail page, returning them alongside SKU, Name, and Price.

### Requirement: UI Confidence Indicator for Details
The Desktop Wizard "Test" UI SHALL display the extracted `Characteristics` and calculate a confidence score for it.

#### Scenario: Viewing Test Results
- **WHEN** the user views the result of the analysis
- **THEN** the field `Characteristics` is shown with the extracted sample and its confidence score.


---
SOURCE: corpus/openspec/changes/improve-product-details-extraction/specs/product-details-extraction/spec.md
---

## ADDED Requirements

### Requirement: Description Extraction
The system SHALL extract extended product descriptions from product pages, including HTML sections specifying descriptions or highlighted specifications (e.g. `product-description` or `tab-content-description`).

#### Scenario: Description section exists on product page
- **WHEN** a product page is scraped and it contains an extended description HTML block
- **THEN** the scraper SHALL extract the HTML content of the description block and include it in the data sent for AI processing or directly parse it into the product's Description field.

### Requirement: JSON Specification Output
The extracted description information SHALL be included in the final exported payload either in the `Description` field or embedded within the `Specifications` JSON as "Extended Description" or "Detalles", ensuring the information reaches the online store.

#### Scenario: Data processing produces final payload
- **WHEN** the system generates the product data payload for the online store or CSV export
- **THEN** the payload SHALL contain the extracted detailed description.


---
SOURCE: corpus/openspec/changes/improve-selector-extraction-analysis/specs/provider-wizard-product-detail/spec.md
---

## MODIFIED Requirements

### Requirement: Fallback behavior for Product Detail URL
The "Product Detail URL" field SHALL be optional, but the system MUST actively try to discover one if it is omitted.

#### Scenario: User omits the product detail URL
- **WHEN** the user is configuring a new provider and leaves the product detail URL blank
- **THEN** the system performs Phase 1 analysis on the catalog URL to extract a valid product detail URL, and uses it automatically for Phase 2 analysis.
- **AND THEN** the wizard UI displays the automatically discovered detail URL for confirmation.


---
SOURCE: corpus/openspec/changes/improve-selector-extraction-analysis/specs/selector-optimization/spec.md
---

## ADDED Requirements

### Requirement: DOM Pre-Analysis Heuristic
The `PageAnalysisService` SHALL apply a pre-analysis step using AngleSharp to parse the raw HTML and extract the most relevant DOM hierarchy (e.g., lists, grids, tables) before sending data to the AI model.

#### Scenario: Complex HTML is simplified
- **WHEN** the service receives a large, complex HTML page
- **THEN** it parses the DOM, removes non-structural elements (scripts, styles, hidden nodes), and produces a simplified representation (DOM Skeleton) focused on product containers.

### Requirement: AI Selector Generation with dual locators
The AI model SHALL generate dual locators (`css` and `xpath`) for each required property based on the simplified DOM skeleton, avoiding brittle paths.

#### Scenario: AI generates robust selectors
- **WHEN** the simplified DOM is analyzed
- **THEN** the AI returns a robust CSS selector (using classes/IDs) and a robust relative XPath.


---
SOURCE: corpus/openspec/changes/improve-selector-extraction-analysis/specs/two-phase-analysis/spec.md
---

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


---
SOURCE: corpus/openspec/changes/shopify-scraping-strategy/specs/dom-pruning-analyzer/spec.md
---

## ADDED Requirements

### Requirement: DOM Pruning for AI Analyzer
The system SHALL support pruning of the DOM tree before sending it to OpenAI to minimize context size and improve structured data extraction.

#### Scenario: Removing noise
- **WHEN** the HTML document is downloaded for analysis
- **THEN** the system SHALL remove invisible elements, scripts, styles, and empty structural containers before passing it to the language model.


---
SOURCE: corpus/openspec/changes/shopify-scraping-strategy/specs/provider-wizard/spec.md
---

## ADDED Requirements

### Requirement: Discovery of Platform specifics
The Wizard discovery process SHALL adapt its AI analysis strategy by detecting platform-specific traits before doing raw HTML analysis.

#### Scenario: Shopify Provider configuration
- **WHEN** the user provides a Shopify-powered URL to the Wizard
- **THEN** the system SHALL detect it's Shopify and configure the new provider to use the Shopify strategy, retrieving data natively or structuring semantic extraction properly.


---
SOURCE: corpus/openspec/changes/shopify-scraping-strategy/specs/shopify-integration/spec.md
---

## ADDED Requirements

### Requirement: Native Shopify Integration API Support
The system SHALL intercept or provide an integration point specifically for Shopify sites to bypass HTML scraping when possible.

#### Scenario: Fallback to products.json
- **WHEN** the site is identified as Shopify
- **THEN** the system SHALL attempt to query `<url>/products.json` or equivalent pagination endpoint to fetch the JSON schema of products directly, falling back to HTML parsing if restricted (403/429).


---
SOURCE: corpus/openspec/changes/wizard-brand-and-test-limits/specs/scraping-screen-discovery-integration/spec.md
---

## ADDED Requirements

### Requirement: Reusable Discovery Logic in Main Scraping
The system SHALL execute the advanced discovery and testing logic originally built for the Wizard within the main Scraping execution cycle (e.g., when the user clicks "Iniciar" in the main Scraping UI). This logic must complement the standard URL processing.

#### Scenario: Running main scraping job
- **WHEN** a user initiates a scraping job for a supplier
- **THEN** the system invokes the discovery routines to find product URLs dynamically, adding them to the processing queue
- **AND** the standard scraping logic processes both pre-configured URLs and newly discovered URLs without failing on suppliers that don't need discovery.

### Requirement: Scraping UI Progress Feedback
The main Scraping UI SHALL display granular states corresponding to the discovery phases.

#### Scenario: Viewing real-time progress
- **WHEN** the scraping job is running
- **THEN** the UI displays the current phase (e.g., "Explorando paginación", "Extrayendo productos") visually, utilizing a progress bar or timeline indicator, distinct from the raw console output.


---
SOURCE: corpus/openspec/changes/wizard-brand-and-test-limits/specs/wizard-brand-capture/spec.md
---

## ADDED Requirements

### Requirement: Capture Brand in Wizard
The system SHALL provide a mechanism in the first step of the configuration Wizard to capture the "Brand" (Marca) name associated with the supplier's products.

#### Scenario: User inputs brand name
- **WHEN** the user is configuring a new supplier site in the Wizard's initial step
- **THEN** they see an input field labeled "Marca (Brand)"
- **AND** the captured value is stored in the Wizard's state and subsequently assigned to the `SiteProfile` configuration under a mechanism that allows product mapping to retrieve it.


---
SOURCE: corpus/openspec/changes/wizard-brand-and-test-limits/specs/wizard-test-limits/spec.md
---

## ADDED Requirements

### Requirement: Test Limit Configuration
The system SHALL enforce a maximum of 10 products when running the test extraction phase in the Wizard, while defaulting to 120 products for actual scraping jobs.

#### Scenario: Running test extraction
- **WHEN** the user initiates the test phase in the Wizard
- **THEN** the scraper processes a maximum of 10 products

#### Scenario: Saving supplier profile
- **WHEN** the user successfully completes the Wizard and saves the configuration
- **THEN** the persisted `SiteProfile` sets its processing limit (e.g., `MaxProductsPerJob` or equivalent configuration) to 120 by default.
