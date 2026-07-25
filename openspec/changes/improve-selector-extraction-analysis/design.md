## Context

El proceso actual de análisis en el Wizard (`PageAnalysisService`) descarga el HTML de la página (truncándolo por tamaño) y se lo pasa a OpenAI (usando *Structured Outputs*). El problema es que OpenAI con HTML en crudo (a menudo muy anidado o ruidoso) tiende a generar selectores frágiles, muy complejos o poco exactos (ej. usando rutas xpath súper largas que se rompen al mínimo cambio).

Adicionalmente, el Wizard está analizando la "URL de Catálogo", donde tal vez no exista toda la información del producto (por ejemplo, las características o la descripción detallada sólo existen al entrar a la "URL de Detalle").

## Goals / Non-Goals

**Goals:**
- Implementar un análisis en 2 fases en el Wizard (Catálogo y Detalle).
- Utilizar técnicas heurísticas (con `AngleSharp` o analizadores de DOM) para pre-filtrar el HTML o extraer los selectores candidatos obvios ANTES de pasárselo a OpenAI.
- Retornar selectores limpios (clases únicas, IDs, o atributos específicos).

**Non-Goals:**
- Reemplazar completamente a OpenAI por reglas manuales. GPT seguirá tomando la decisión final basada en el pre-análisis.
- Modificar el flujo base del Scraping en ejecución (Worker), este cambio se limita a mejorar cómo se *descubren* los selectores en el Wizard/API.

## Decisions

1. **Análisis en 2 fases orquestado por la API**:
   - `Phase 1: Catalog Analysis`: Analiza la URL del listado. El objetivo es identificar `productContainerSelector`, `productCardSelector` y, lo más importante, extraer un enlace representativo a un producto (`detailLink`).
   - `Phase 2: Detail Analysis`: Descarga la página de detalle del enlace encontrado (o el que el usuario haya proveído opcionalmente) y busca `sku`, `name`, `price`, `image` y `characteristics`.
   - La API (`/api/sites/analyze`) orquestará esto internamente para no complicar la UI, o devolverá un progreso. Por simplicidad, se hará secuencialmente en el mismo endpoint (toma más tiempo, pero es más robusto).

2. **Pre-análisis Heurístico en el DOM (`AngleSharp`)**:
   - En lugar de enviar todo el `body`, el `PageAnalysisService` utilizará `AngleSharp` para buscar elementos con IDs o clases semánticas (ej. `[class*='product']`, `[id*='price']`, `table`, `ul`).
   - El servicio limpiará los atributos inútiles y extraerá la jerarquía básica para que OpenAI la entienda más fácil, generando un "árbol simplificado" (DOM Skeleton).
   - OpenAI usará este DOM Skeleton para elegir el selector correcto.

3. **Uso de XPath relativos y CSS limpios**:
   - Refinaremos el System Prompt de OpenAI para forzar el uso de selectores CSS limpios y evitar XPaths absolutos `html/body/div[1]/...`. Exigiremos el uso de `.//` o `//` enfocados en atributos clave.

## Risks / Trade-offs

- **[Risk] Mayor tiempo de análisis**: Al realizar dos descargas de Playwright (Catálogo + Detalle) y dos llamadas a GPT, el análisis tomará el doble de tiempo (probablemente 40-60 segundos).
  - *Mitigación*: Mantener visible un indicador de estado claro en el UI ("Analizando Catálogo...", "Analizando Detalle...").
- **[Risk] La heurística remueve información vital**: Al limpiar el DOM, podríamos quitar la etiqueta que GPT necesitaba.
  - *Mitigación*: La heurística solo removerá `<script>`, `<style>`, `svg`, clases utilitarias de Tailwind (opcional), pero mantendrá la estructura base.
