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
