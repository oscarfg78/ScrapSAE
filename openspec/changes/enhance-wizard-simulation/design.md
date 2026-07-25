## Context

Actualmente el Wizard ayuda a configurar selectores y los prueba limitando la extracción a un número muy bajo de productos. Sin embargo, no proporciona una visualización rica de "Demo Mode", y el Worker (ScrapSAE.Worker) en el backend fallaba en producción por el problema de compilación de la DLL bloqueada. Adicionalmente, el análisis actual de GPT pide sugerir un solo selector CSS, el cual puede no ser óptimo o romper si el DOM cambia sutilmente, por lo que requerimos extraer y almacenar dualidad CSS/XPath para cada campo y hacer que la estrategia de scraping sea resiliente al intentar ambos.

## Goals / Non-Goals

**Goals:**
- Probar un límite más robusto (5 productos en total: 1 en el test base y 4 adicionales en las tarjetas detalladas o como parte de la iteración).
- Proporcionar un Demo Mode en la UI del Wizard.
- Modificar el sistema de análisis y extracción de selectores para soportar una estructura que contenga { "css", "xpath" } por selector.
- Garantizar que el Worker y la API compartan 100% la misma lógica.

**Non-Goals:**
- Alterar el formato de Exportación (CSV/Flashly).
- Rediseñar el ScrapSAE.Worker internamente.

## Decisions

**Decisión 1: Estructura del JSON en GPT**
GPT devolverá un objeto JSON para cada selector esperado: {"css": "...", "xpath": "..."}. En el backend de SiteProfile.Selectors (JSONB) esto se almacenará como string, es decir, JSON anidado, o simplemente el Wizard adaptará esto a 2 selectores, pero para no romper esquema en base de datos, mantendremos el JSON.
*Alternativa considerada*: Agregar columnas en DB, lo cual descartamos por fricción y complejidad.

**Decisión 2: Fallback Automático en GetSelector**
La función GetSelector en las estrategias (ListExtractionStrategy / DirectExtractionStrategy) intentará hacer .QuerySelectorAsync() primero con el CSS. Si no encuentra nada, usará el XPath.

## Risks / Trade-offs

- **Riesgo**: El Wizard podría tardar más tiempo procesando 5 productos durante el Test Scrape.
  **Mitigación**: 5 productos es un número razonable que permite validar selectores repetitivos sin causar timeouts extremos de Playwright.

