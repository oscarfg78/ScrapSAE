## Context

Actualmente en ScrapSAE, la integración con Inteligencia Artificial (GPT) durante el scraping y en el Provider Wizard consume créditos de API de forma general. En ocasiones, la IA no aporta valor adicional (por ejemplo si los selectores CSS ya extraen la información completa), generando consumo innecesario. Adicionalmente, la exportación a la tienda (Flashly) envía metadatos sensibles o internos como `source_url` y `supplier name`, y el guardado de productos extraídos se realiza en lote en lugar de ser en tiempo real por cada producto.

En el Provider Wizard, el análisis con GPT actualmente corre el riesgo de fallar por payloads de DOM excesivamente grandes, páginas 404 o falta de insumos, sin reutilizar respuestas previas de GPT como contexto ni aprovechar la configuración de proveedores conocidos y funcionales (como Festo).

## Goals / Non-Goals

**Goals:**
- Implementar un switch "Utilizar IA" y un monitor dinámico que alerte al usuario cuando la IA no esté produciendo extracciones efectivas, permitiendo desactivarla al vuelo.
- Guardar cada producto individualmente en la base de datos inmediatamente al finalizar su extracción.
- Omitir los atributos `source_url` y `supplier name` al exportar productos a la tienda (Flashly).
- Mantener y reutilizar el contexto devuelto por GPT en el Provider Wizard, mitigando fallos por DOM extenso, insumos faltantes o errores 404 antes de realizar llamadas a la API.
- Permitir seleccionar un proveedor base (ej. Festo) en el Wizard para pre-probar sus selectores en la nueva URL. Si coinciden, copiarlos directamente sin gastar IA; si no, enviar los selectores base + DOM a la IA para inferir únicamente las diferencias/XPaths faltantes.

**Non-Goals:**
- Rediseñar por completo el backend de almacenamiento local SQLite o cambiar la arquitectura MVVM existente.
- Eliminar la capacidad de usar IA cuando el usuario decida conscientemente mantenerla tras una alerta.

## Decisions

### 1. Monitor de Eficiencia de IA y Alerta Dinámica en Scraping
- **Decision**: Añadir propiedad `UseAI` en la configuración de ejecuciones de scrap. Crear `AIEfficiencyMonitor` que evalúa cada registro. Si tras un número configurable de registros (ej. 3 a 5 productos) la IA no agrega campos válidos respecto a los selectores base, el motor emite el evento `AIEfficiencyWarningRequested`.
- **UI Dialog**: `MainViewModel` suscribe este evento y despliega un diálogo de confirmación: *"No es necesario que se siga usando IA"*. Si el usuario acepta, `UseAI` pasa a `false` en tiempo de ejecución.
- **Alternatives Considered**: Desactivar automáticamente la IA sin preguntar (rechazado: el usuario debe conservar el control).

### 2. Persistencia Inmediata por Registro (Per-Product Save)
- **Decision**: En `PlaywrightScrapingService` / `DirectExtractionStrategy`, en el bucle de iteración de productos, inmediatamente tras procesar y validar un objeto `Product`, invocar `IProductRepository.SaveProductAsync(product)` y notificar a la UI para actualizar contadores y grillas.
- **Alternatives Considered**: Guardar en lotes de N productos (rechazado: el requerimiento exige guardado al momento de terminar cada registro).

### 3. Exclusión de Metadatos en Exportación a Tienda (Flashly)
- **Decision**: En `FlashlyClient` / `ExportService` (o en la preparación del DTO de exportación), implementar un filtro que remueva las llaves `source_url`, `sourceUrl`, `supplier name`, `supplier_name` de los metadatos y especificaciones del producto antes de enviar el payload HTTP o generar el archivo CSV.

### 4. Mitigación Pre-GPT y Contexto de Análisis en Provider Wizard
- **Decision**: 
  - **Pre-flight Checks**: Antes de enviar información a OpenAI/GPT, validar el código HTTP de respuesta (prevenir 404), la existencia de elementos mínimos en el DOM y sanitizar/recortar el HTML (eliminar `<script>`, `<style>`, `<svg>`, comentarios y espacios en blanco masivos).
  - **Context Persistence**: Guardar el objeto `GptAnalysisResult` en `ProviderWizardViewModel`. Si ocurre un fallo parcial, adjuntar el resultado previo como contexto en la llamada de reintento.

### 5. Plantilla Base de Proveedores y Evaluación Híbrida de Selectores en el Wizard
- **Decision**:
  - Agregar control selector de "Proveedor Base" en `ProviderWizardView` / `ProviderWizardViewModel`.
  - **Test Previo de Selectores**: Probar los selectores del proveedor base directamente contra la nueva URL. Si el test logra extraer campos clave (Título, Precio, SKU) con éxito, adoptar los selectores inmediatamente indicando al usuario que la estructura de página es compatible.
  - **Análisis Híbrido IA**: Si la prueba parcial falla, enviar a GPT el DOM sanitizado junto con los selectores base del proveedor de referencia. Pedir a GPT que devuelva únicamente las correcciones o XPaths faltantes, optimizando significativamente la precisión y el consumo de tokens.

## Risks / Trade-offs

- **[Risk] Alerta de IA excesivamente sensible**: La alerta de desactivar IA podría dispararse en productos legítimos que simplemente carecen de ciertos atributos en la página del proveedor.
  - *Mitigación*: Establecer un umbral de evaluación (ej. mínimo 3-5 productos consecutivos sin aporte de IA) antes de sugerir su desactivación.
- **[Risk] DOM sanitizado pierde contenedores relevantes**: Al limpiar etiquetas HTML, se podría remover información importante para GPT.
  - *Mitigación*: Limpiar únicamente etiquetas no estructurales (`<script>`, `<style>`, `<path>`, `<svg>`) preservando clases, atributos `id`, `data-*` y la estructura jerárquica de la página.
- **[Risk] Persistencia inmediata afecta rendimiento en discos lentos**: Guardar un producto por uno en SQLite puede ser más lento que guardado por lotes.
  - *Mitigación*: Ejecutar `SaveProductAsync` de forma asíncrona fuera del hilo principal de UI.

## Migration Plan

1. Actualizar DTOs y modelos de datos en `ScrapSAE.Core` (`ScrapingOptions`, `Supplier`, `Product`).
2. Implementar `AIEfficiencyMonitor` e integrar guardado per-producto en `ScrapSAE.Infrastructure`.
3. Actualizar sanitización de exportación en `FlashlyClient`.
4. Extender `ProviderWizardViewModel` y servicios de scraping para soportar prueba de selectores base y pre-flight sanitization antes de GPT.
5. Actualizar vistas WPF (`MainView`, `ProviderWizardView`).
