## MODIFIED Requirements

### Requirement: Flujo de validacion y prueba en el Wizard (Test Scrape)
El Wizard SHALL ejecutar una prueba de extraccion de productos contra la pagina web usando los selectores ingresados. Para la ejecucion de la prueba, el orquestador de scraping y el contexto de ejecucion SHALL ser instanciados de manera que los selectores XPath y CSS sean transmitidos correctamente.

#### Scenario: Prueba exitosa desde la UI del Wizard
- **WHEN** el usuario da click en Test Scrape despues de obtener sugerencias de la IA
- **THEN** la prueba se realiza exitosamente recuperando los productos y evitando el error inmediato por ausencia de variables legacy, y soportando selectores XPath en caso de estipularse.
