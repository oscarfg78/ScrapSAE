# Línea base documental para la estrategia de extracción

**Estado:** borrador de auditoría, no aplicado al repositorio.

## Principio rector reconstruido

La intención más reciente de OpenSpec es que el **wizard de alta sea el punto de descubrimiento, configuración y validación**, pero no un motor alternativo. El wizard debe invocar los mismos contratos y módulos que utilizará la ejecución real; el `SiteProfile` confirmado debe convertirse en la **fuente de verdad inmutable de configuración**, y la producción debe reproducir los campos observados en la demostración cuando el sitio no haya cambiado.

| ID | Requisito canónico reconstruido | Fuente principal |
|---|---|---|
| R-WIZ-01 | El wizard debe capturar URL de catálogo, URL de detalle opcional y marca, validar las entradas y conservar el estado ante errores reintentables. | `add-provider-wizard/specs/provider-wizard/spec.md`; `align-process-execution-with-wizard-methods/specs/provider-wizard-product-detail/spec.md`; `wizard-brand-and-test-limits/specs/wizard-brand-capture/spec.md` |
| R-WIZ-02 | Si falta la URL de detalle, el análisis de catálogo debe descubrir activamente una URL representativa y mostrarla para confirmación. | `improve-selector-extraction-analysis/specs/provider-wizard-product-detail/spec.md`; `two-phase-analysis/spec.md` |
| R-ANA-01 | El análisis debe descargar contenido renderizado, detectar primero rasgos de plataforma, podar el DOM y separar análisis de catálogo y análisis de detalle. | `page-analysis-ai/spec.md`; `shopify-scraping-strategy/specs/provider-wizard/spec.md`; `selector-optimization/spec.md`; `two-phase-analysis/spec.md` |
| R-ANA-02 | La IA debe devolver localizadores duales CSS/XPath robustos, estrategia recomendada, selectores secundarios, campos y niveles de confianza en un resultado estructurado. | `page-analysis-ai/spec.md`; `fix-wizard-and-support-xpath/specs/provider-discovery/spec.md`; `selector-optimization/spec.md` |
| R-WIZ-03 | La configuración propuesta debe mostrarse en un formulario editable antes de probarla. | `add-provider-wizard/specs/provider-wizard/spec.md` |
| R-DEMO-01 | El test del wizard debe ejecutar extracción real usando la misma lógica común que producción, no una simulación paralela, y limitarse a 10 productos. | `fix-wizard-and-support-xpath/specs/provider-wizard-product-detail/spec.md`; `wizard-brand-and-test-limits/specs/wizard-test-limits/spec.md`; `align-process-execution-with-wizard-methods/specs/strategy-driven-execution/spec.md` |
| R-DEMO-02 | Si existe estrategia o URL de detalle, el test debe recorrer catálogo y detalle y devolver SKU, nombre, imagen, precio, características y descripción disponible. | `improve-product-detail-extraction/specs/provider-wizard-product-detail/spec.md`; `wizard-detail-testing/spec.md`; `advanced-product-detail-extraction/spec.md`; `improve-product-details-extraction/specs/product-details-extraction/spec.md` |
| R-DEMO-03 | El wizard debe mostrar los productos extraídos, el valor de cada campo, su estado/confianza y mensajes accionables cuando no haya resultados. | `add-provider-wizard/specs/provider-wizard/spec.md`; `improve-product-detail-extraction/specs/wizard-detail-testing/spec.md` |
| R-PROF-01 | Al confirmar, se persiste el `SiteProfile` con límite normal de 120; al cancelar, los datos temporales deben eliminarse, con limpieza periódica de huérfanos. | `add-provider-wizard/specs/provider-wizard/spec.md`; `page-analysis-ai/spec.md`; `wizard-brand-and-test-limits/specs/wizard-test-limits/spec.md` |
| R-EXEC-01 | `SiteProfile.StrategyType`, `Strategies`, `Selectors` y `SecondarySelectors` deben ser la única fuente de verdad; producción no debe reescribirlos ni elegir por variables de entorno o nombre del proveedor. | `align-process-execution-with-wizard-methods/specs/provider-wizard-product-detail/spec.md`; `strategy-driven-execution/spec.md` |
| R-EXEC-02 | Cada ejecución debe recibir un `ScrapeExecutionContext` inmutable y aislado, incluyendo modo headless, login manual, conservación del navegador, fallback de captura y límite temporal. | `align-process-execution-with-wizard-methods/specs/scrape-execution-context/spec.md` |
| R-STR-01 | Para perfiles genéricos, el orquestador debe ejecutar estrategias habilitadas por prioridad; sin configuración explícita usa Direct → List → Families. | `align-process-execution-with-wizard-methods/specs/strategy-driven-execution/spec.md` |
| R-STR-02 | Shopify debe intentar su vía nativa `/products.json` y caer a análisis HTML solo ante restricción; los demás pathways no deben depender de Shopify. | `shopify-scraping-strategy/specs/shopify-integration/spec.md`; `align-process-execution-with-wizard-methods/specs/provider-discovery/spec.md` |
| R-DISC-01 | La ejecución principal debe reutilizar las rutinas avanzadas de descubrimiento del wizard y combinar URLs preconfiguradas con URLs descubiertas sin perjudicar proveedores que no necesitan descubrimiento. | `wizard-brand-and-test-limits/specs/scraping-screen-discovery-integration/spec.md` |
| R-PARITY-01 | Con el mismo `SiteProfile` y el mismo estado del sitio, la ejecución final debe producir los mismos campos que el test del wizard. | `align-process-execution-with-wizard-methods/specs/strategy-driven-execution/spec.md` |
| R-MAP-01 | La marca del proveedor debe estar disponible para el mapeo de salida; el payload de Flashly debe excluir `source_url` y `supplier name` y aplicar la sobrescritura de marca sin mutar el dato bruto. | `customize-supplier-brand-specs/specs/supplier-specs-mapping/spec.md` |

## Evolución y conflictos documentales

| Tema | Evidencia | Resolución provisional para la estrategia |
|---|---|---|
| Límite del test | El cambio inicial define 120; `enhance-wizard-simulation` define 5; `wizard-brand-and-test-limits` define 10. | **10 para demo y 120 para ejecución normal**, por ser la especificación posterior y explícitamente diferenciada. |
| URL de detalle omitida | Una versión usa el primer producto; una versión posterior exige descubrimiento activo durante Fase 1. | Prevalece el **descubrimiento activo**, con fallback determinista y diagnóstico si no se resuelve. |
| Formato de selectores | Hay strings CSS/XPath, JSON anidado `DualSelector` y prefijos Playwright. | El contrato final debe normalizarse; hasta entonces no se puede afirmar interoperabilidad completa. |
| Orquestación | Documentos antiguos describen rutas separadas o selección por entorno/nombre; el cambio de alineación exige `SiteProfile` + `StrategyOrchestrator`. | Prevalece la configuración del perfil; las rutas legacy solo pueden existir como adaptadores explícitos y observables, nunca como fallback silencioso. |
| Estado de implementación | Varios cambios figuran `complete`, pero `align-process-execution-with-wizard-methods` mantiene cinco validaciones reales pendientes; `add-provider-wizard` tiene tareas duplicadas y dependencias pendientes. | Toda casilla de tareas se considera **afirmación**, no evidencia, hasta contrastarla con código y pruebas. |
| Extracción de detalle | Existen dos cambios casi homónimos, uno completo y otro sin iniciar, con alcance solapado. | Requiere consolidación en una sola capacidad canónica antes de implementar más cambios. |

## Vacíos normativos críticos

| Vacío | Por qué impide una estrategia fiable |
|---|---|
| Semántica de combinación | OpenSpec define fallback secuencial, pero no especifica cuándo mezclar resultados de varios pathways, cómo resolver conflictos por campo ni qué estrategia tiene autoridad. |
| Aislamiento de fallos | No existe una política normativa para que el fallo de una estrategia no cancele las demás ni para distinguir “no aplicable”, “sin resultados”, “error recuperable” y “error fatal”. |
| Proveniencia | No se exige registrar qué pathway, URL y selector produjo cada campo, lo que impide explicar la demo y comparar con producción. |
| Normalización de selectores | No hay contrato único para CSS, XPath, JSON dual, selectores secundarios ni atributos; esto puede romper la paridad wizard/producción. |
| Deduplificación y fusión | No se define identidad canónica de producto, prioridad SKU/URL/handle, ni reglas para fusionar catálogo, detalle, API y extensión. |
| Criterio de éxito | “Al menos un producto” es insuficiente; faltan umbrales por cobertura de campos, validez, duplicados, errores y confianza. |
| Resultado de demo | No hay un DTO canónico que diferencie producto bruto, normalizado, enriquecido, rechazado, diagnóstico y trazas. |
| Persistencia temporal | El diseño inicial crea perfiles temporales en la misma persistencia; no está decidido si la demo puede ejecutarse sin efectos laterales. |
| Contrato de descubrimiento | Falta definir presupuestos de paginación, profundidad, número de URLs, dominio permitido, canónicos, robots/rate limit y terminación. |
| Pruebas contractuales | No hay una matriz normativa que obligue a cada pathway a superar el mismo contract test ni una prueba de paridad demo/producción. |
| Versionado | No se registra qué versión de configuración/selectores produjo una ejecución, dificultando reproducibilidad y rollback. |
| Observabilidad | Faltan eventos y métricas obligatorios por fase, estrategia, selector, URL, duración, producto y motivo de descarte. |

## Diagnóstico de OpenSpec

OpenSpec **sí es utilizable** y es el marco adecuado, pero actualmente funciona como colección de cambios superpuestos y no como fuente canónica única. La carpeta contiene diez cambios activos, varios marcados como completos sin archivar, dos cambios de detalle solapados y especificaciones base que no reflejan toda la evolución reciente. Además, `openspec/config.yaml` no define contexto de dominio ni reglas de calidad por artefacto. La estrategia final deberá proponer una fase de saneamiento y un nuevo cambio paraguas; no se creará ni modificará ningún artefacto hasta obtener aprobación.
