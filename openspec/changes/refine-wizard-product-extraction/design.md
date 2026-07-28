## Context

El wizard de alta de proveedores de ScrapSAE presenta fallas en la forma en que analiza el producto (perdiendo selectores duales) y en la forma en que ejecuta la prueba de extracción (demo). Actualmente, el flujo de Test muta el estado productivo (creando perfiles temporales y usando staging) y aplica límites rígidos. Según el análisis de la arquitectura (Estrategia integral de Manus), el wizard debe actuar como un plano de control que configure y simule la extracción de manera aislada, sin estado persistente y reportando con un modelo autocontenido.

## Goals / Non-Goals

**Goals:**
- Actualizar el paso de Análisis para extraer exhaustivamente todos los datos del producto detalle y preservar la riqueza de los selectores detectados.
- Habilitar en el Análisis el descubrimiento de URLs candidatas (listado/catálogo) similares a la URL de detalle.
- Incluir un campo en el paso de Test para que el usuario configure explícitamente el número de productos (presupuesto).
- Ejecutar la prueba de extracción utilizando un request unificado (por ejemplo, `mode=Demo`), garantizando cero persistencia de negocio y obteniendo un reporte autocontenido.

**Non-Goals:**
- No se reescribirán todas las estrategias (Generic, List, etc.) de ScrapSAE en este cambio; solo se ajustará la forma en que el Wizard invoca al runner.
- No se abordará la persistencia final de producción ni la reconciliación avanzada de productos.

## Decisions

- **Presupuesto de Productos:** El ViewModel del Wizard (`ProviderWizardViewModel`) añadirá una propiedad para capturar la cantidad de productos de prueba. Este valor se inyectará en la configuración de la ejecución como el presupuesto límite.
- **Modo Demo en Runner:** La API/ejecutor expondrá un flag `IsDemo` (o enumeración de modo) que instruirá al `ScrapingRunner` a no invocar los repositorios de staging ni guardar el perfil del proveedor de forma definitiva, procesando en memoria y devolviendo los `ReconciledProduct` o `ScrapedProduct` directamente en la respuesta o reporte.
- **Análisis Extendido:** El `PageAnalysisResult` retendrá todos los campos detectados de la URL de detalle, y también se buscará poblar un listado de `CandidateUrls` extraídas del DOM que compartan contexto o patrón con la URL original.

## Risks / Trade-offs

- **Riesgo:** Desacoplar la demo de la base de datos staging puede requerir ajustes en el polling de resultados si el UI dependía de polling en BD.
- **Mitigación:** Retornar los resultados íntegramente en la respuesta HTTP de la invocación de demo (ya que el presupuesto está acotado, por ejemplo, 5-10 productos) o usar el WebSocket existente (SignalR) emitiendo eventos etiquetados con el `DemoSessionId`.
