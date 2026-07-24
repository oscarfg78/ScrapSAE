## Context

En el Wizard de Alta de Proveedores, actualmente el sistema usa la "URL del catálogo" para tratar de encontrar productos, y de ahí toma el primer producto que encuentre (como un ancla en la paginación) para analizar la estructura de los campos de detalle del producto (por ejemplo, descripción).
Sin embargo, a menudo el primer producto de un catálogo no cuenta con una descripción completa, lo que ocasiona que el análisis de la estructura del sitio falle o no detecte los selectores correctamente.

## Goals / Non-Goals

**Goals:**
- Permitir al usuario ingresar una "URL de detalle de producto" de forma opcional durante la configuración en el wizard.
- Propagar esta URL a los servicios de descubrimiento.
- Usar esta URL en lugar de obtener la URL del primer producto del catálogo cuando esté disponible, mejorando la confiabilidad del proceso de descubrimiento.

**Non-Goals:**
- No se modificará el proceso de descubrimiento de listados/catálogos, solo el de detalles de producto.
- No se elimina la posibilidad de descubrir productos sin esta URL (se mantendrá el comportamiento actual como fallback).

## Decisions

- **Modificación UI:** Se agregará un campo `ProductDetailUrl` en el paso inicial (`Step 1: URL`) del wizard.
- **Flujo de Datos:** `ProviderWizardViewModel` recibirá y pasará este dato a `ScrapingRunner.RunDiscoveryAsync` y `ScrapingRunner.ValidateConfigurationAsync`.
- **Modificación en Contratos:** `DiscoveryConfig` u objetos similares pasados al motor de Scraping (e.g. `PlaywrightScrapingService`) incluirán la propiedad `ProductDetailUrl`.
- **Estrategias:** En las estrategias concretas (`IScrapingStrategy`), el flujo actual donde se obtiene el primer elemento para inferir detalle se modificará para primero validar si `ProductDetailUrl` no es nulo o vacío, y si es el caso, usar esa URL en su lugar.

## Risks / Trade-offs

- **Risk:** El usuario podría poner la URL de detalle de un producto de otro sitio diferente al catálogo.
  - **Mitigation:** Se asumirá que el análisis fallará o devolverá una baja confianza, lo que el usuario podrá notar. En un futuro se podría validar que el host coincida.
