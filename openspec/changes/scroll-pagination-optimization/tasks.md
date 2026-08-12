## 1. Engine Core & Scroll Helper

- [x] 1.1 Implementar `ScrollToLastProductAndHydrateAsync` en `PlaywrightScrapingService` para realizar scroll dinámico hacia la última tarjeta de producto y el footer.
- [x] 1.2 Implementar el monitoreo del conteo incremental de productos en el DOM y retardo de hidratación AJAX tras cada desplazamiento.

## 2. Integración con Estrategias de Scraping

- [x] 2.1 Actualizar la estrategia de extracción directa (`DirectExtractionStrategy`) para utilizar el bucle de hidratación por scroll de forma iterativa.
- [x] 2.2 Integrar el ciclo de scroll iterativo en la recolección de tarjetas de producto en `PlaywrightScrapingService`.

## 3. Verificación & Límites de Ejecución

- [x] 3.1 Garantizar que el bucle de scroll se detenga inmediatamente al alcanzar el límite configurado (`MaxProductsPerScrape`).
- [x] 3.2 Validar la compilación y funcionamiento del sistema de paginación por scroll.
