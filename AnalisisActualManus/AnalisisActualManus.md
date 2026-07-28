# AnalisisActualManus

**Estado:** análisis y estrategia pendientes de validación.  
**Restricción:** no contiene implementación ni modifica el comportamiento de ScrapSAE.

Esta carpeta integra en un único paquete autocontenido el análisis del estado actual, la estrategia de extracción de productos, la propuesta de gobernanza OpenSpec, los diagramas y la evidencia utilizada durante la auditoría.

## Lectura recomendada

| Orden | Archivo | Propósito |
|---:|---|---|
| 1 | [`estrategia_extraccion_productos.md`](estrategia_extraccion_productos.md) | Informe principal, decisiones D1–D10 y hoja de ruta. |
| 2 | [`target_architecture.png`](target_architecture.png) | Arquitectura objetivo resumida visualmente. |
| 3 | [`current_flow.png`](current_flow.png) | Flujo efectivo actual y puntos de sustitución/fallback. |
| 4 | [`01_Diagnostico/current_state_matrices.md`](01_Diagnostico/current_state_matrices.md) | Matrices de contratos, pathways y riesgos. |
| 5 | [`01_Diagnostico/requirements_baseline.md`](01_Diagnostico/requirements_baseline.md) | Línea base normativa reconstruida. |
| 6 | [`02_OpenSpec/spec_governance.md`](02_OpenSpec/spec_governance.md) | Gates y cambio paraguas OpenSpec recomendado. |

## Estructura

| Ruta | Contenido |
|---|---|
| Raíz | Informe principal, diagramas renderizados, inventario y manifiesto de integridad. |
| `01_Diagnostico/` | Evidencia consolidada, requisitos, matrices y notas detalladas. |
| `02_OpenSpec/` | Gobernanza propuesta, investigación y compendios de especificaciones. |
| `03_Diagramas_fuente/` | Fuentes Mermaid de los diagramas actual y objetivo. |
| `04_Evidencia/corpus_analizado/` | Copia aislada del corpus documental y OpenSpec examinado. |
| `04_Evidencia/lotes_de_analisis/` | Lotes comprimidos usados para el análisis temático. |

## Integridad

`INVENTARIO.txt` enumera todos los archivos integrados. `MANIFEST_SHA256.txt` contiene un hash SHA-256 por archivo para detectar cambios accidentales. Ambos se generan localmente y no implican ninguna operación Git remota.

## Control de cambios

La carpeta se incorpora únicamente al árbol de trabajo local. **No se ha realizado commit, push, publicación ni subida a GitHub.** Cualquier incorporación futura al historial deberá ser una decisión explícita del usuario.
