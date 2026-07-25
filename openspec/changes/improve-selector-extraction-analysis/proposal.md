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
