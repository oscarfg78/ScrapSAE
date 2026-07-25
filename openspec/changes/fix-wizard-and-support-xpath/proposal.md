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
