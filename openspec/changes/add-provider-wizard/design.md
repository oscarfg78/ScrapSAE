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
