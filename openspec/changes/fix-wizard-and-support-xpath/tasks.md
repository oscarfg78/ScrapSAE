## 1. Modificacion de IA (OpenAIProcessorService)

- [x] 1.1 Actualizar el prompt de AnalyzeSelectorsAsync para indicar que puede sugerir XPath o CSS. Si es XPath debe llevar prefijo '//' o 'xpath='.
- [x] 1.2 Revisar si es necesario algun cambio menor en el schema, aunque con el prefijo deberia bastar para los strings devueltos.

## 2. Correccion del Wizard Test (Bug Fix)

- [x] 2.1 Revisar el endpoint de ScrapingController que usa el Wizard para lanzar el test de prueba.
- [x] 2.2 Corregir la inyeccion de los selectores detectados por la IA hacia el ScrapeExecutionContext temporal de prueba para que el orquestador no aborte por falta de configuracion. (La inyección estaba bien, el problema era que ListExtractionStrategy fallaba al hacer cast de `site.Selectors` a `Dictionary<string, object>`).
- [x] 2.3 Probar el paso 4 del Wizard para confirmar que recupera productos en lugar de dar error "No se encontraron productos".

## 3. Soporte de XPath en Ejecucion (PlaywrightScrapingService)

- [x] 3.1 Comprobar ListExtractionStrategy y DirectExtractionStrategy para asegurar que pasan el string del selector tal cual a Playwright, aprovechando su resolucion automatica de selectores XPath.
