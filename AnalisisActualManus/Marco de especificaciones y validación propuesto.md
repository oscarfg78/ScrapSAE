# Marco de especificaciones y validación propuesto

**Estado:** estrategia de auditoría; no aplicada al repositorio.

## Decisión de framework

Se recomienda **mantener OpenSpec**. Ya está integrado, dispone de CLI funcional, los cambios existentes usan el schema `spec-driven` y el modo de exploración local prohíbe implementar durante el análisis. Introducir Spec Kit, Kiro u otro framework duplicaría fuentes de verdad y no resolvería los defectos reales: requisitos superpuestos, contratos incompatibles y ausencia de evidencia ejecutable.

La instalación actual es OpenSpec **1.5.0**. No se condicionará el plan a funciones exclusivas de 1.6.0. Una actualización futura puede evaluarse como cambio aislado, pero no debe mezclarse con la estabilización funcional.

## Condición de partida

| Indicador | Estado observado | Consecuencia |
|---|---|---|
| Cambios OpenSpec activos | 10 | Existe concurrencia y solapamiento de alcance. |
| Validación estricta | 11 de 14 elementos válidos | El corpus no puede utilizarse todavía como gate de implementación. |
| Cambio inválido | `enhance-wizard-simulation` | No contiene deltas parseables. |
| Specs base inválidas | `project-organization`, `readme-documentation` | Carecen de `## Purpose`. |
| Configuración de proyecto | `spec-driven`, sin contexto/reglas de dominio suficientes | El formato existe, pero no obliga a trazabilidad ni evidencia. |
| Estado de tareas | Varias casillas marcadas como completas | Se consideran afirmaciones hasta ser respaldadas por pruebas y código. |

> `openspec validate --all --strict` es un control estructural necesario, pero no demuestra coherencia semántica, cobertura de pruebas ni paridad entre demo y producción.

## Cambio paraguas recomendado

Tras la aprobación de esta estrategia se debe abrir **un único cambio paraguas**, con nombre provisional `stabilize-provider-extraction-pipeline`. No debe contener código en su primera iteración. Su propósito será reconciliar el corpus vigente y constituir la única autoridad normativa para la estabilización.

| Artefacto | Contenido obligatorio antes de implementar |
|---|---|
| `proposal.md` | Problema, alcance, no objetivos, cambios activos absorbidos, cambios explícitamente no absorbidos, compatibilidad y rollback. |
| `specs/**/spec.md` | Requisitos normativos con escenarios positivos, degradados, de error, concurrencia y cancelación. |
| `design.md` | Contratos canónicos, modelo de ejecución, estados, políticas de composición, fronteras transaccionales y decisiones con alternativas descartadas. |
| `tasks.md` | Trabajo vertical por capacidades, cada tarea ligada a requisito, prueba y evidencia; nunca por capas técnicas aisladas. |
| Matriz de trazabilidad adjunta | Requisito → escenario → contrato → componente → prueba → evidencia → estado. |

Los diez cambios activos no deben fusionarse de forma mecánica. Cada requisito se clasificará como **conservar, modificar, reemplazar, descartar o fuera de alcance**. Ningún cambio incompleto o inválido debe archivarse como si estuviera implementado; primero se documentará su supersesión y se preservará la trazabilidad histórica.

## Capacidades normativas del cambio paraguas

| Capability OpenSpec | Responsabilidad normativa |
|---|---|
| `provider-onboarding-analysis` | Entrada del wizard, análisis catálogo/detalle, plataforma, descubrimiento de URL representativa y edición humana. |
| `selector-contract` | Schema versionado para CSS/XPath, selectores secundarios, atributos, cardinalidad, alcance y validación. |
| `extraction-contributors` | Descriptores y contrato común para descubridores, listados, detalles, enriquecedores y normalizadores. |
| `execution-planning` | Plan explícito por proveedor; modos `fallback`, `augment` y `ensemble`; presupuesto, cancelación y aislamiento. |
| `demo-session` | Ejecución no destructiva desde el wizard, límite único de 10, snapshot autocontenido y cero aprendizaje implícito. |
| `product-observation-and-reconciliation` | Observaciones con provenance por campo, identidad canónica, conflictos, fusión, calidad y descartes. |
| `execution-result-and-preview` | Resultado canónico mostrado al usuario con productos, diagnósticos, cobertura, evidencias y errores por contributor. |
| `persistence-boundary` | Persistencia única tras reconciliar/validar, idempotencia, versionado y separación de datos demo. |
| `pathway-adapters` | Adaptación explícita de Generic, Direct, List, Families, Shopify, legacy y extensión sin dependencias mutuas. |
| `parity-observability-and-testing` | Paridad demo/producción, eventos, métricas, contract tests, fixtures y criterios de promoción. |

## Reglas que deben incorporarse al flujo OpenSpec

Sin crear todavía un schema personalizado, `openspec/config.yaml` puede endurecerse después de la aprobación con reglas por artefacto. Si dichas reglas resultan insuficientes, se podrá clonar `spec-driven` y añadir un artefacto de revisión; esto es opcional y posterior, no un prerrequisito.

| Artefacto | Regla propuesta |
|---|---|
| Propuesta | Declarar fuentes OpenSpec/docs reconciliadas, alcance negativo, migración, compatibilidad y rollback. |
| Specs | Asignar ID estable a cada requisito; incluir escenarios de éxito, no aplicable, vacío, error recuperable, error fatal, timeout y cancelación cuando corresponda. |
| Diseño | Incluir contratos tipados, ownership del estado, política de efectos laterales, invariantes, diagrama de secuencia y tabla de decisiones. |
| Tareas | Cada tarea debe citar requisito y prueba; ninguna tarea de implementación puede preceder al contract test que define su aceptación. |

## Gates de aprobación

| Gate | Evidencia exigida | Autoridad de aprobación | Permite |
|---|---|---|---|
| G0 — Baseline | Los 14 elementos existentes pasan validación estricta o existe disposición explícita y trazable para cada inválido. | Responsable técnico | Reconciliar artefactos, no código. |
| G1 — Alcance | Propuesta paraguas y tabla de supersesión aprobadas. | Usuario | Redactar specs y diseño. |
| G2 — Contratos | Specs completas, schema de selectores, contributor contract, execution plan y resultado demo revisados. | Usuario + responsable técnico | Crear plan de pruebas y tareas. |
| G3 — Ejecutabilidad | Matriz requisito/prueba completa; fixtures y criterios de calidad definidos; cero contradicciones abiertas P0. | Responsable técnico/QA | Solicitar autorización de implementación. |
| G4 — Autorización | Aprobación explícita del usuario sobre diseño y roadmap. | Usuario | Comenzar código, por incrementos verticales. |
| G5 — Incremento | Contract tests, integración y evidencia de un slice pasan; no hay regresión en otros pathways. | CI + revisión humana | Promover el siguiente slice. |
| G6 — Paridad | Demo y producción generan resultados equivalentes con mismo perfil/snapshot; strict validate y verify pasan. | Usuario + QA | Archivar el cambio y habilitar producción. |

## Matriz de trazabilidad obligatoria

Cada fila debe responder a una afirmación verificable. Un ejemplo de formato es:

| Requirement ID | Escenario | Contrato | Contributor/capa | Prueba | Evidencia esperada | Estado |
|---|---|---|---|---|---|---|
| `DEMO-001` | Mismo perfil y snapshot | `ExtractionExecutionRequest` | Wizard → runner | E2E de paridad | Hash de observaciones y diff por campo | Pendiente |

Los estados válidos serán **especificado, diseñado, probado en contrato, implementado, verificado y aceptado**. “Tarea completada” no sustituye ninguno de estos estados.

## Criterio de preparación para implementar

La implementación solo estará lista cuando: OpenSpec valide en estricto; el usuario haya aceptado el cambio paraguas; cada pathway tenga capacidades, precondiciones y degradación definidas; la demo sea normativa y no destructiva; se hayan fijado identidad y reglas de merge; todos los P0 tengan condición de cierre; la matriz de pruebas incluya aislamiento, composición y paridad; y no queden decisiones arquitectónicas abiertas que alteren contratos públicos.

## Resultado de esta fase

OpenSpec es **posible y recomendable**, pero debe pasar de colección de cambios a sistema de decisión. No se recomienda implantar otro framework. La siguiente fase debe diseñar la arquitectura objetivo y una hoja de ruta que haga cumplir estos gates; ningún artefacto del repositorio ni código será alterado antes de la validación expresa del usuario.
