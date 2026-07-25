## ADDED Requirements

### Requirement: Soporte para selectores XPath en extraccion de productos
El sistema SHALL permitir definir y procesar expresiones XPath como selectores para ubicar elementos durante el web scraping, de forma equivalente a como se procesan los selectores CSS.

#### Scenario: Analisis de URL y ejecucion del motor XPath
- **WHEN** un usuario ingresa un selector XPath o la IA lo sugiere para un campo
- **THEN** el motor de Playwright evalua exitosamente la ruta xpath y retorna los contenidos esperados sin error de parseo.
