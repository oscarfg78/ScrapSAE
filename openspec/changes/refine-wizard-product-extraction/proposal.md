## Why

El proceso de extracción de productos actual en el wizard presenta inconsistencias y fallas silenciosas en la detección de detalles de productos. Este cambio implementa la visión estratégica descrita en el análisis de Manus, para que el wizard sirva como un verdadero plano de control, donde se analice la página de detalle indicada, se extraigan todos los datos del producto, se descubran URLs similares (candidatas), se configure y valide el presupuesto de extracción (número de productos) y se ejecute una prueba demo real y predecible, evitando sobrescrituras silenciosas y rutas alternativas.

## What Changes

- **Paso de Análisis del Wizard:** Mejora para analizar exhaustivamente la página y extraer todos los datos del producto detalle indicado por el usuario, sin ignorar selectores.
- **Descubrimiento de candidatos:** En el wizard, a partir de la URL del producto detalle, extraer un listado de URLs parecidas (candidatos) para alimentar el pipeline de descubrimiento.
- **Configuración de Test (Demo):** Permitir al usuario indicar el número de productos (presupuesto) que desea extraer en el wizard como test.
- **Ejecución Demo Unificada:** Ejecutar la extracción de prueba usando el mismo núcleo de ejecución que producción, pero limitando los resultados al número de productos indicado, sin alterar el estado productivo (sin crear perfiles temporales de negocio).

## Capabilities

### New Capabilities
- `wizard-extraction-demo`: Capacidad de ejecutar una prueba de extracción (demo) desde el wizard, indicando un número específico de productos, devolviendo un reporte sin mutar perfiles de negocio productivos.

### Modified Capabilities
- `provider-wizard-product-detail`: Se actualizará para integrar la extracción exhaustiva de todos los datos del producto, el descubrimiento de URLs similares, y la selección del número de productos para la prueba.

## Impact

- **UI/Desktop:** `ProviderWizardViewModel.cs` y vistas asociadas (Análisis, Config y Test).
- **Servicios de Scraping:** `IScrapingRunner` y sus implementaciones para soportar el modo demo con el presupuesto especificado, y devolver reportes autocontenidos (sin usar la base de datos staging como transporte principal del reporte del test).
