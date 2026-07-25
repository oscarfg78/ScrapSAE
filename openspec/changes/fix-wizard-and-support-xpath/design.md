## Context
Actualmente el Wizard utiliza OpenAIProcessorService para analizar el HTML de un catalogo y sugerir selectores de productos, precios y SKUs. Sin embargo, el esquema actual esta enfocado en selectores CSS. En algunos sitios, extraer el dato es mas facil y robusto mediante XPath, pero no se esta sugiriendo nativamente. Adicionalmente, hay un bug actual en la vista previa del Wizard ("No se encontraron productos") que esta rompiendo el flujo de alta, posiblemente debido a que el motor del orquestador o la construccion del contexto no esta recibiendo los selectores en memoria correctamente.

## Goals / Non-Goals

**Goals:**
- Actualizar el schema de la IA (OpenAIProcessorService) para que pueda sugerir selectores XPath si son mas robustos o directos.
- Corregir el test de extraccion del Wizard para que funcione con el nuevo motor orquestado (StrategyOrchestrator).
- Asegurar que las estrategias de Playwright usen XPath o CSS transparentemente (aprovechando el auto-detect de Playwright para "//").

**Non-Goals:**
- No se reescribiran estrategias enteras de extraccion, solo la integracion de XPath y la resolucion del bug en el endpoint de prueba.

## Decisions

- **Modificacion del Schema y Prompt de OpenAI**: Instruiremos a la IA para que si elige XPath, inicie el string obligatoriamente con // (o xpath=), y si es CSS con sus prefijos estandar (., #). Playwright detecta esto automaticamente sin cambiar codigo base.
- **Bug Fix del Wizard Test**: Verificaremos el endpoint de API (ScrapingController / WizardController) que lanza la prueba, asegurando que popule correctamente el ScrapeExecutionContext con las estrategias y selectores generados en tiempo real antes de llamar al StrategyOrchestrator.

## Risks / Trade-offs

- **Risk**: XPath suele ser mas fragil ante rediseños menores de la pagina web.
  - **Mitigation**: El prompt de la IA se ajustara para que priorice CSS por su resiliencia, pero opte por XPath SOLAMENTE si el CSS no es suficiente (ej. hijos de elementos sin clases, td/tr de tablas, etc.).
