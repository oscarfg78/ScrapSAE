# Matrices del estado actual: contratos, pathways y riesgos

## Criterio de composabilidad

Para esta auditoría, un pathway es **independiente** cuando puede ejecutarse con un contrato tipado, límites y cancelación propios, sin estado global ni precondiciones implícitas de otro pathway. Es **combinable** cuando entrega candidatos y evidencias a un resultado canónico, puede coexistir con otros pathways en la misma ejecución y la reconciliación conserva provenance, conflictos y calidad por campo. El estado actual solo alcanza fallback secuencial en algunas rutas; no implementa ensemble/composición verificable.

## Fragmentación contractual

| Plano | Representación actual | Consecuencia |
|---|---|---|
| Análisis | `PageAnalysisResult` con `DualSelector`, secundarios, recomendaciones y confianza | La información es más rica que la configuración persistida y se degrada al avanzar en el wizard. |
| Configuración del wizard | `WizardSiteConfig` con siete strings y tres booleanos | No puede expresar capacidades, selectores secundarios editables, paginación, familias completas, variantes, login, límites por ejecución ni políticas de combinación. |
| Persistencia | `SiteProfile.Selectors` como `object` JSONB, `SecondarySelectors`, `Strategies` y compatibilidad legacy embebida | Existen varias fuentes representando el mismo concepto; la normalización depende del caller. |
| Motor genérico | `SiteSelectors` tipado con vocabulario distinto | No reconoce de forma uniforme las claves `productContainer`, `productCard`, `name`, etc. creadas por el wizard. |
| Estrategias internas | `IScrapingStrategy.ExecuteAsync(object page, SiteProfile, executionId)` | No declara capacidades, precondiciones, límites, resultado diagnóstico ni tipo de contribución. |
| Estrategias de proveedor | `IProviderScraperStrategy.ScrapeAsync` y `ScrapeDirectUrlsAsync` | El contrato superior tampoco declara capacidades; Shopify no implementa la ruta directa. |
| Extensión | `SiteSelectors` TypeScript propio y payload de productos | No comparte vocabulario, XPath, runner, staging ni postproceso común. |
| Ejecución | `ScrapeExecutionContext` tipado más variables de entorno globales | El aislamiento del contexto es parcial; las URLs y opciones pueden contaminar ejecuciones concurrentes. |
| Producto bruto | `ScrapedProduct` | No registra strategy/pathway, selector, evidencia, advertencias, estado por campo ni identidad canónica. |
| Producto procesado | `ProcessedProduct` | Añade datos normalizados y confianza global, pero no provenance ni resolución de conflictos por campo. |
| Staging | Upsert por `SiteId + SkuSource` | Un SKU vacío colisiona; el último resultado reemplaza `RawData`/`AIProcessedJson` sin conservar aportes de pathways ni historial. |
| Resultado de ejecución | `ScrapeRunResult` con seis contadores y duración | El wizard debe releer staging y no recibe productos, diagnósticos, calidad, pathway, errores ni evidencias en la respuesta. |

## Matriz de pathways

| Pathway/capacidad | Entrada real | Salida | Independencia actual | Combinación actual | Hallazgo determinante |
|---|---|---|---|---|---|
| Análisis catálogo + detalle | URL de catálogo y detalle opcional | `PageAnalysisResult` | Parcial | Configura, no ejecuta | Puede recomendar `Families` sin generar los selectores mínimos requeridos por esa estrategia. |
| Descubrimiento de URLs | Perfil y página base | Pool de URLs | Baja | Sustitutiva | El runner publica discovered+learned en una variable global; Playwright detecta el pool y retorna por scraping directo antes del orquestador. |
| Direct interno | Página abierta y diccionario de selectores | Productos básicos | Media | Fallback first-success | Exige SKU y título; contiene parser propio para `DualSelector` serializado. |
| List interno | Página abierta y diccionario de selectores | Productos de tarjeta | Media | Fallback first-success | No pagina ni aplica el límite uniforme; un resultado parcial bloquea pathways posteriores. |
| Families interno | Selectores family/variant específicos | Productos de variantes | Baja | Fallback teórico | El wizard no produce su contrato y su parser no soporta las representaciones toleradas por Direct/List. |
| Shopify API | `/products.json` | Productos de primera variante/imagen | Media | Sustituye Generic | Respeta el perfil solo al cortar por página y puede exceder el máximo; direct URLs no está implementado. |
| Legacy Playwright | Perfil, heurísticas y casos especiales | Productos variables | Baja | Fallback monolítico | Mezcla Festo, Searchanise, familias, búsqueda y detalle dentro del motor; no es seleccionable ni medible por capacidad. |
| Direct URLs / learned URLs | Pool global de URLs y `SiteSelectors` | Productos de detalle | Baja | Sustituye orquestador | Usa un modelo de selectores incompatible con el wizard y contiene rutas genéricas que llaman lógica nominalmente Festo. |
| Enriquecimiento profundo | URLs candidatas de productos ya extraídos | Producto ampliado | Parcial | Merge heurístico | Es un buen punto común del runner, pero depende de `ScrapeDirectUrlsAsync`; Shopify no lo implementa y la extensión no pasa por él. |
| Galería, stock, moneda, adjuntos | Página de detalle + `SiteSelectors` | Campos enriquecidos | Media | Dentro de detalle | Reutilizable, pero solo se invoca desde la extracción profunda del motor genérico y no aparece en el preview. |
| Screenshot fallback | Contexto/variable global | Imagen/base64 o apoyo IA | Baja | Fallback implícito | No se modela como contributor con diagnóstico, coste o evidencia propia. |
| Procesamiento IA | `ScrapedProduct` | `ProcessedProduct` | Media | Común al runner | No recibe provenance por campo y la extensión usa una entrada separada fuera del runner. |
| Extensión de navegador | DOM del navegador del usuario | `ProcessedProduct` | Alta como extractor aislado | Nula con servidor | Opera y enriquece por su cuenta; el endpoint salta runner, staging y deduplicación común. |
| Análisis post-ejecución | Productos/resultado del runner | Sugerencias | Baja | Mutación automática | También corre en demo y puede alterar la configuración que se está probando; historial solo en memoria y posible esquema distinto. |

## Registro priorizado de riesgos

| ID | Prioridad | Riesgo | Probabilidad | Impacto | Evidencia actual | Condición de cierre |
|---|---|---|---|---|---|---|
| R01 | P0 | El descubrimiento denominado aditivo reemplaza la estrategia configurada mediante retorno temprano | Alta | Crítico | Pool en `SCRAPSAE_LEARNED_URLS` seguido de `ScrapeDirectUrlsAsync` | Descubridores devuelven candidatos; el plan decide contributors sin shortcuts ocultos. |
| R02 | P0 | Incompatibilidad de vocabulario y formato de selectores entre análisis, wizard, estrategias y `SiteSelectors` | Alta | Crítico | JSON de `DualSelector` dentro de strings y alias no aceptados por el motor común | Un único esquema versionado, adaptadores en frontera y validación antes de ejecutar. |
| R03 | P0 | La demo no está aislada: crea proveedor y staging, activa post-análisis y usa variables globales | Alta | Crítico | Perfil `[TEMP]`, consulta posterior a staging, env vars de proceso | Sesión demo tipada, no destructiva por defecto, sin mutación/aprendizaje y con cleanup garantizado. |
| R04 | P0 | No existe resultado canónico ni provenance para mezclar productos/campos | Alta | Crítico | `ScrapedProduct`, `ProcessedProduct`, staging y run result carecen de aportes por pathway | Envelope por candidato/campo con contributor, evidencia, confianza, timestamp y decisión de merge. |
| R05 | P0 | `Families` no puede ser configurado válidamente desde el wizard | Alta | Alto | Requiere `familyLink` y `variant*`; el wizard solo persiste siete claves básicas | Descriptor de capacidad/precondiciones y formulario generado por schema; test unitario aislado. |
| R06 | P1 | El orquestador aplica first-nonempty, no calidad ni combinación | Alta | Alto | Retorna inmediatamente con cualquier producto | Política explícita `fallback`, `augment` o `ensemble`, con umbrales y presupuesto. |
| R07 | P1 | Límites demo contradictorios y no transmitidos por contexto | Alta | Alto | 2 en `WizardTest`, 5 en temporal, 10 en preview/especificación, 120 al guardar | Un `ExecutionBudget` por request y una única constante/spec para demo. |
| R08 | P1 | Un SKU vacío o conflictivo puede sobrescribir resultados en staging | Media-Alta | Alto | Upsert por `SiteId + (SkuSource ?? "")` y reemplazo total de JSON | Identidad por cascada versionada, hash/canonical URL y conservación de observaciones. |
| R09 | P1 | La prueba puede declararse exitosa con un producto parcial | Alta | Alto | Éxito si existe al menos un producto; no hay gates de calidad | Acceptance profile con campos obligatorios, cobertura, duplicados, errores y evidencia. |
| R10 | P1 | El preview oculta datos y diagnósticos necesarios para validar la extracción | Alta | Alto | Tabla limitada; no muestra imagen, URL, faltantes, detalle ni pathway | Vista análisis/ejecución/reconciliación con datos completos y JSON treeview descargable. |
| R11 | P1 | La configuración validada puede auto-modificarse durante la demo | Media-Alta | Alto | Sugerencias ≥0.7 aplicadas automáticamente | Demo con `mutationPolicy=none`; propuestas versionadas y promoción explícita. |
| R12 | P1 | Estado global impide seguridad frente a concurrencia | Media-Alta | Alto | Env vars para URLs, login, headless y opciones | Todo estado en `ExecutionContext`; cero lectura/escritura global en runtime. |
| R13 | P1 | Shopify y extensión quedan fuera del enriquecimiento/combinación uniforme | Alta | Alto | Direct URLs Shopify no implementado; extensión no usa runner | Adaptadores al mismo contributor contract y degradación explícita por capacidades no soportadas. |
| R14 | P1 | No hay pruebas del circuito crítico ni de los contratos de composición | Alta | Alto | Cero referencias en tests a wizard, análisis, orquestador y estrategias | Pirámide contractual, fixtures de sitios, E2E de wizard y pruebas de paridad demo/producción. |
| R15 | P2 | Limpieza temporal best-effort y eliminación de duplicados no temporales en el mismo servicio | Media | Alto | Cancel async no esperado; cleanup borra por nombre | Cleanup scoped por `demoSessionId`; deduplicación separada, auditable y no destructiva. |
| R16 | P2 | `ScrapeRunResult` obliga a una segunda lectura global de staging | Alta | Medio-Alto | Solo contadores/duración | Respuesta de demo autocontenida o endpoint de sesión con snapshot consistente. |

## Diagnóstico arquitectónico

El problema central no es la cantidad de extractores, sino que **estrategia, descubrimiento, extracción, enriquecimiento, fallback y persistencia están superpuestos**. La misma palabra “strategy” se usa para el routing Shopify/Generic y para Direct/List/Families; a la vez, los fallbacks legacy y direct URLs pueden saltarse ambos planos. En consecuencia, habilitar más rutas no aumenta de manera controlada la cobertura: cambia cuál ruta reemplaza a cuál.

La solución estratégica debe separar cinco tipos de capacidad: **descubridores**, **extractores de listado**, **extractores de detalle**, **enriquecedores** y **normalizadores/validadores**. Cada contributor ha de poder ejecutarse en aislamiento, declarar sus requisitos y devolver observaciones sin persistir. Un plan de ejecución por proveedor decide cuáles contribuyen, cuáles son fallback y cuáles son ensemble. La persistencia ocurre una sola vez después de reconciliar y validar.
