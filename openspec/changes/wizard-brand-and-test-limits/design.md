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
