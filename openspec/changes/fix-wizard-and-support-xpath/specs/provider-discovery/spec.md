## MODIFIED Requirements

### Requirement: Sugerencia de selectores y variables de extraccion por Inteligencia Artificial
La IA (OpenAIProcessorService) SHALL sugerir el mejor selector aplicable de forma inteligente para cada campo y propiedad. Esta sugerencia SHALL distinguir y reportar selectores CSS validos y rutas XPath funcionales, pre-configurando su viabilidad segun la estructura provista.

#### Scenario: Analisis de catalogos complejos
- **WHEN** el usuario ingresa a un sitio en el que no existen identificadores de clase directos ni tags estructurados simples, requiriendo busqueda relacional
- **THEN** la Inteligencia artificial provee selectores del tipo XPath asegurandose de incluir el estandar de deteccion que asimile la API, por lo general el prefijo '//' o 'xpath='.
