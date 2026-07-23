## Context

El sistema actualmente intenta extraer información genérica usando un `PageAnalysisService` que descarga el DOM con Playwright, lo envía completo (o casi completo) a OpenAI, y pide selectores CSS.
Sin embargo, algunos proveedores como Shopify no tienen catálogos estructurados de forma tan sencilla. Sus listas de productos son a veces inyectadas por Javascript en componentes fuertemente acoplados. `Mejora Web Scraping en .NET.md` sugiere el uso de Keyed Services para inyectar estrategias particulares por proveedor (como una estrategia de Shopify que intente consumir `/products.json` de forma nativa) y el uso de poda del DOM (DOM Pruning) cuando se tenga que analizar la página vía OpenAI.

## Goals / Non-Goals

**Goals:**
- Configurar .NET 8 Keyed Services para resolver instancias específicas de scraping (ej. ShopifyStrategy vs GenericStrategy).
- Implementar Poda de DOM en `PageAnalysisService` para reducir el tamaño del HTML enviado al LLM.
- Detectar proveedores Shopify automáticamente en el Wizard y crear una configuración asociada que no dependa puramente de selectores CSS frágiles.

**Non-Goals:**
- Migrar todo el motor actual a C# nativo abandonando la IA; la IA se usará como respaldo robusto.
- Cambiar la base de datos o el frontend de escritorio de ScrapSAE en gran medida, solo la forma en que los sitios se configuran y extraen.

## Decisions

- **Keyed Services**: 
  - *Rationale*: .NET 8 tiene soporte nativo para `[FromKeyedServices]`. Almacenaremos una clave de estrategia en la tabla de proveedores. Si es Shopify, usamos la implementación `ShopifyScraperStrategy`.
- **Detección en el Wizard**: 
  - *Rationale*: Antes de invocar OpenAI, analizaremos el HTML para encontrar `window.Shopify` o links a `cdn.shopify.com`. Si se encuentra, marcaremos el proveedor como "Shopify" y podremos evitar el uso intensivo de LLM si consumimos su API.
- **DOM Pruning**:
  - *Rationale*: Antes de invocar a `gpt-4o`, se removerán tags `<script>`, `<style>`, `<svg>`, y nodos `display: none` vía AngleSharp para abaratar costos de token y mejorar la inferencia.

## Risks / Trade-offs

- **[Risk]** Bloqueos de la API de Shopify (HTTP 429).
  - *Mitigation*: Emplear Polly con Exponential Backoff para peticiones a la API del proveedor.
- **[Risk]** Poda de DOM muy agresiva perdiendo atributos data vitales.
  - *Mitigation*: Solo eliminar tags declarativos como estilos y scripts sin modificar los metadatos o las etiquetas schema.org.
