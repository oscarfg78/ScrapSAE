# Investigación externa sobre OpenSpec

## Fuente oficial principal

**URL:** https://github.com/Fission-AI/OpenSpec

La documentación oficial presenta OpenSpec como un flujo ligero, iterativo y apto para proyectos brownfield. El ciclo recomendado es `explore` para investigar sin implementar; `propose` para crear `proposal.md`, especificaciones con requisitos y escenarios, `design.md` y `tasks.md`; `apply` para implementar tareas; y `archive` para consolidar el cambio en las especificaciones principales. Las especificaciones se expresan en Markdown con requisitos normativos y escenarios WHEN/THEN. La fuente subraya que el usuario revisa el plan antes de escribir código y que los artefactos se pueden iterar sin fases rígidas.

## Personalización oficial

**URL:** https://github.com/Fission-AI/OpenSpec/blob/main/docs/customization.md

OpenSpec permite tres niveles: `openspec/config.yaml` para contexto y reglas por artefacto; schemas personalizados versionados bajo `openspec/schemas/`; y overrides globales. El schema estándar `spec-driven` puede clonarse y extenderse con artefactos y dependencias propios. Los schemas se validan con `openspec schema validate`. Esto permitiría añadir un gate de revisión preimplementación, pero para ScrapSAE no es necesario sustituir de inmediato el schema estándar: primero conviene consolidar los cambios activos y endurecer contexto/reglas.

## Iteración y actualización de artefactos

**URL:** https://github.com/Fission-AI/OpenSpec/issues/694

La discusión oficial confirma que los artefactos son archivos fuente editables y que el flujo no está bloqueado por fases. En la actualización de julio de 2026 se indica que `/opsx:update` revisa artefactos existentes y mantiene coherencia; `/opsx:verify` contrasta implementación y especificaciones; `/opsx:apply` vuelve a leer los artefactos actuales. Esta flexibilidad encaja con una fase previa de reconciliación de los diez cambios activos de ScrapSAE.

## Límite de validación

**URL:** https://github.com/Fission-AI/OpenSpec/issues/829

La propuesta documenta una limitación relevante: la validación de artefactos personalizados y la coherencia semántica entre artefactos no estaban plenamente gobernadas por el schema; se distinguen validación estructural por artefacto y validación semántica cruzada como trabajo separado. Para ScrapSAE, `openspec validate --strict` debe ser necesario pero no suficiente: harán falta checks de trazabilidad propios (capability → requisito → escenario → contrato → prueba) y una revisión explícita de contradicciones entre cambios activos.

## Decisión preliminar

OpenSpec es viable y ya está integrado en el repositorio; no se justifica introducir otro framework. Se mantendrá `spec-driven` y se propondrá un cambio paraguas de reconciliación antes de cualquier implementación. La estrategia incorporará controles adicionales de trazabilidad y evidencia porque la validación nativa no garantiza por sí sola la coherencia semántica entre diez cambios concurrentes ni la correspondencia con pruebas ejecutables.

## Verificación visual adicional

La página oficial de personalización confirma que `openspec/config.yaml` es el mecanismo recomendado para la mayoría de equipos y permite inyectar contexto común y reglas específicas por artefacto. La resolución del schema prioriza el flag de CLI, después `.openspec.yaml` del cambio, después la configuración del proyecto y finalmente `spec-driven`. Para ScrapSAE esto implica que los cambios activos pueden reconciliarse gradualmente sin romper compatibilidad: el change paraguas puede fijar explícitamente su schema y las reglas pueden exigir trazabilidad, contratos, pruebas y criterios de rollback antes de `apply`.

## Estado real en ScrapSAE

La instalación local usa **OpenSpec 1.5.0**. La ejecución de `openspec validate --all --strict --json` sobre el repositorio, sin modificarlo, devuelve **14 elementos: 11 válidos y 3 inválidos**. Los fallos son:

| Elemento | Tipo | Fallo |
|---|---|---|
| `enhance-wizard-simulation` | cambio | `main/spec.md` no contiene secciones delta `ADDED/MODIFIED/REMOVED/RENAMED`; por ello el cambio carece de deltas parseables. |
| `project-organization` | especificación base | Falta la sección obligatoria `## Purpose`. |
| `readme-documentation` | especificación base | Falta la sección obligatoria `## Purpose`. |

Esto confirma que OpenSpec es utilizable, pero el corpus no está actualmente en estado de gate. La estrategia debe comenzar con una fase documental de reconciliación y saneamiento, aprobada por el usuario, antes de permitir cualquier `apply`. La versión instalada tampoco incorpora todavía todas las mejoras descritas para OpenSpec 1.6.0; por tanto, la estrategia no debe depender de `/opsx:update` hasta que la actualización se decida y valide explícitamente.
