# Mejorar la simulación del Wizard y Extracción Dual (CSS/XPath)

## 1. Problema actual
Aunque hemos solucionado el bug de extracción en memoria (el problema del cast de los selectores), el proceso *real* seguía sin encontrar productos porque el archivo ScrapSAE.Infrastructure.dll estaba bloqueado por el sistema, impidiendo que la compilación actualizara los binarios de ejecución. 

Además, necesitamos mayor garantía de que el proceso real será exitoso. El usuario necesita ver una simulación (Demo Mode) en el Wizard que sea 100% fiel al proceso real y que incluya múltiples extracciones (1 listado + 5 productos detallados), y que la IA proponga tanto selectores CSS como XPath para maximizar la resiliencia, en lugar de elegir sólo uno.

## 2. Solución Propuesta

### A. Demo Mode y Extracción Múltiple en el Wizard
- Al ejecutar el "Test Scrape" (Paso 4) en el Wizard, aumentaremos el límite temporal a 5 productos (actualmente extrae 2 o a veces sólo 1).
- Mostraremos la información extraída de los 5 productos en una interfaz de simulación "Demo Mode" dentro del Wizard para garantizar que la calidad de los datos (precio, sku, características, imagen) es óptima antes de guardar.
- Re-usaremos exactamente la misma lógica de RunScrapingAsync y PlaywrightScrapingService (ya lo hacemos, pero ahora explicitaremos que el comportamiento del backend debe ser idéntico, y el worker solo cambiará el límite MaxProductsPerScrape).

### B. Extracción Simultánea de CSS y XPath
- Modificaremos el Prompt de GPT (OpenAIProcessorService.cs) para que devuelva un objeto estructurado para cada campo, el cual contenga **tanto el CSS óptimo como el XPath óptimo**.
- El SelectorAnalysisRequest y el DTO de respuesta deberán soportar esta estructura dual (CssSelector y XPathSelector).
- En el orquestador (StrategyOrchestrator) y las estrategias (ListExtractionStrategy, DirectExtractionStrategy), al buscar un selector, se intentará usar primero el CSS y si falla o está vacío, se usará el XPath como un *fallback* automático. Esto garantiza máxima resiliencia sin que el usuario tenga que "elegir" uno manualmente.

## 3. Impacto en Componentes

- **OpenAIProcessorService.cs**: Actualización de Prompt y JSON Schema para devolver pares de { "css": "...", "xpath": "..." }.
- **SiteProfile / WizardConfig**: La estructura en base de datos (Selectors JSONB) guardará las preferencias como strings crudos o adaptaremos el parser para leer estas sub-propiedades. (Para mantener retrocompatibilidad y simplicidad, el Wizard puede guardar el mejor de los dos, o guardar un objeto y el GetSelector se encarga de probar ambos).
- **ProviderWizardViewModel**: Mostrar hasta 5 productos en la tabla del Preview. UI de "Modo Demo".
- **Estrategias (ListExtractionStrategy / DirectExtractionStrategy)**: Lógica de Fallback (CSS -> XPath) implementada robustamente en GetSelector.
