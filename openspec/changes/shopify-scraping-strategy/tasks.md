## 1. Implementación de Estrategia por Proveedor (Keyed Services)

- [x] 1.1 Definir interfaz `IProviderScraperStrategy` en `ScrapSAE.Core`.
- [x] 1.2 Implementar `GenericPlaywrightStrategy` para análisis genérico con OpenAI/Playwright.
- [x] 1.3 Implementar `ShopifyApiStrategy` utilizando consumo del endpoint nativo `/products.json`.
- [x] 1.4 Registrar las estrategias en el contenedor DI con `.AddKeyedScoped()` en `Program.cs` de la API.

## 2. Poda de DOM (DOM Pruning)

- [x] 2.1 Modificar `PageAnalysisService` para instanciar `AngleSharp` y parsear el HTML obtenido de Playwright.
- [x] 2.2 Crear método de limpieza que remueva tags `<script>`, `<style>`, `<link>`, y nodos no visibles.
- [x] 2.3 Utilizar el HTML podado en la consulta que se envía a `gpt-4o`.

## 3. Modificaciones al Wizard Discovery

- [x] 3.1 Actualizar el modelo `Provider` en la DB para admitir un enum o string de `StrategyType`.
- [x] 3.2 Modificar el endpoint de análisis para detectar firmas de Shopify en el HTML (`window.Shopify`, cdn.shopify.com).
- [x] 3.3 Devolver en `PageAnalysisResult` el tipo de estrategia detectada para configurarlo automáticamente.
- [x] 3.4 Actualizar la UI del Wizard para guardar el `StrategyType` en la creación del Proveedor.

## 4. Refactorización de Resiliencia con Polly

- [x] 4.1 Añadir `Microsoft.Extensions.Http.Polly` a la API si no está.
- [x] 4.2 Configurar una política de *Exponential Backoff* al `HttpClient` de la estrategia de Shopify.
