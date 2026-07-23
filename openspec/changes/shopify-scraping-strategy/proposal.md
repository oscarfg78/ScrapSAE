## Why

Actualmente, el Wizard de configuración intenta extraer selectores estáticos desde el DOM puro. Sin embargo, los proveedores modernos como Shopify listan productos dinámicamente o estructuran sus datos de tal forma que los selectores CSS fallan o no abarcan todas las variaciones. Necesitamos un enfoque estratégico más robusto alineado a nuestra nueva arquitectura para descubrir el esquema y extraer los datos exitosamente usando la integración nativa con Shopify y los enfoques semánticos propuestos (Keyed Services, JSON-LD, y Shopify API).

## What Changes

- Implementación del patrón Strategy con `Keyed Services` para manejar integraciones específicas por plataforma (ej. Shopify, genérico).
- Mejora del Wizard (Discovery) para detectar automáticamente si el sitio está impulsado por Shopify o tiene datos JSON-LD.
- En sitios Shopify, intentar consumir nativamente `products.json` u optimizar la estrategia LLM para extraer colecciones y marcas específicas.
- Refactorización de la tubería de análisis para emplear Poda de DOM (DOM Pruning) reduciendo tokens enviados al LLM y mejorando el éxito en la respuesta de OpenAI estructurada.

## Capabilities

### New Capabilities
- `shopify-integration`: Integración específica para descubrir y extraer datos estructurados de Shopify vía API o metadatos nativos.
- `dom-pruning-analyzer`: Sistema de limpieza y poda del DOM antes del envío del contenido HTML al servicio de OpenAI para reducir el ruido, mejorar la precisión de los esquemas, y bajar costos de token.

### Modified Capabilities
- `provider-wizard`: Se modifica el comportamiento de descubrimiento para priorizar metadatos, detección de plataforma (Shopify) y delegar a la estrategia específica correspondiente en vez de un análisis genérico.

## Impact

- `ScrapSAE.Infrastructure.AI`: Refactorizado para incorporar pre-procesamiento del DOM y análisis de metadatos (JSON-LD).
- `ScrapSAE.Api`: Integración de Keyed Services de .NET 8 en el contenedor de dependencias (`IServiceCollection`).
- `ScrapSAE.Core`: Nuevos modelos DTO y configuración específica para las estrategias de extracción (Shopify API endpoint).
