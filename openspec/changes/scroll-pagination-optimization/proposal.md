## Why

Durante la extracción de catálogos que utilizan scroll infinito o carga diferida (lazy loading), el proceso de scraping finaliza prematuramente o no carga todos los elementos disponibles al quedarse en la parte superior de la página. Para lograr una paginación efectiva, el navegador debe desplazarse dinámicamente hasta el último elemento de producto visible o hasta el footer de la página, esperar la hidratación de nuevos elementos vía AJAX o eventos DOM, y extraer la información de forma iterativa hasta agotar la carga o alcanzar el límite de productos configurado.

## What Changes

- **Desplazamiento Progresivo e Interactivo (Targeted Scroll & Footer Navigation)**:
  - Navegar y realizar scroll automático hasta el último elemento de producto actualmente renderizado en el DOM o hasta el footer de la página.
- **Detección Dinámica de Nuevos Productos (Incremental Hydration Waiting)**:
  - Esperar a que la página cargue y renderice nuevos nodos de productos tras el evento de scroll.
- **Bucle de Paginación Efectiva (Scroll-Loop Extraction)**:
  - Iterar la extracción de productos adicionales descubiertos de forma progresiva.
  - Detener la iteración automáticamente cuando no aparezcan nuevos productos tras múltiples intentos de scroll o cuando se alcance el límite máximo configurado (`MaxProductsPerScrape`).

## Capabilities

### New Capabilities
- `scroll-pagination-scraping`: Paginación efectiva por desplazamiento incremental hacia el último producto renderizado o footer, con espera activa de hidratación DOM e iteración progresiva de extracción.

### Modified Capabilities
- N/A

## Impact

- **Engine de Scraping (`PlaywrightScrapingService`)**: Implementación del método de scroll iterativo hasta el último producto/footer (`ScrollToBottomAndHydrateAsync`) y lógica de ciclo de paginación en estrategias de catálogo.
- **Estrategias de Scraping (`DirectExtractionStrategy`, `GenericPlaywrightStrategy`)**: Integración del bucle de hidratación por scroll en la recolección de tarjetas de producto.
