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
