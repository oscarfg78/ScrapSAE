## 1. Integración de Heurística y Limpieza de DOM (AngleSharp)

- [x] 1.1 Modificar `PageAnalysisService` para implementar un método de limpieza de DOM (remover scripts, styles, svg).
- [x] 1.2 Implementar heurística con AngleSharp para extraer un "DOM Skeleton" enfocado en contenedores semánticos (`table`, `ul`, `[class*='product']`).
- [x] 1.3 Refinar el System Prompt de OpenAI (`BuildProcessedProductVisionRequest` o similar) para exigir CSS limpio y XPath relativos (`//` o `.//`) basados en el DOM Skeleton.

## 2. Refactorización de Análisis en Dos Fases en la API

- [x] 2.1 Renombrar/Ajustar el endpoint actual de análisis para manejar dos fases (o crear dos métodos separados internamente `AnalyzeCatalogAsync` y `AnalyzeDetailAsync`).
- [x] 2.2 En la Fase 1 (Catálogo): Instruir a la IA o usar AngleSharp para extraer un enlace representativo a un detalle de producto (`DetailUrl`).
- [x] 2.3 En la Fase 2 (Detalle): Descargar el HTML de la `DetailUrl` obtenida (o provista) y ejecutar la extracción profunda (SKU, Nombre, Imagen, Precio, Características).
- [x] 2.4 Consolidar los resultados de ambas fases en el `PageAnalysisResult`.

## 3. Ajustes en la Interfaz del Wizard (Desktop)

- [x] 3.1 Actualizar `ProviderWizardViewModel` para enviar la URL del catálogo y esperar/recibir la URL de detalle sugerida por la API.
- [x] 3.2 (Opcional/Menor) Mostrar al usuario un feedback de que el proceso está en "Analizando Catálogo..." y luego "Analizando Detalle...".
- [x] 3.3 Validar que el botón "Ejecutar test de scraping" utilice la misma lógica y no falle.
