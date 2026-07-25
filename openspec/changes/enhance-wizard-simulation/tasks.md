## 1. Extracción Dual de IA (CSS y XPath)

- [x] 1.1 Actualizar el prompt de OpenAIProcessorService.cs para instruir a la IA que devuelva un objeto { css: "...", xpath: "..." } para cada campo en lugar de un string único.
- [x] 1.2 Actualizar el JSON Schema esperado por GPT (BuildSelectorAnalysisRequest) para que cada campo principal (productContainer, productCard, name, sku, etc.) sea un objeto con las propiedades css y xpath.

## 2. Ejecución Resiliente (Fallback)

- [x] 2.1 Refactorizar GetSelector en ListExtractionStrategy.cs para detectar si el JSON parseado tiene propiedades css y xpath (usando un DTO auxiliar o deserializando en Dictionary<string, JsonElement>).
- [x] 2.2 Modificar la extracción en ListExtractionStrategy.cs para que primero intente encontrar el elemento usando el css provisto, y si no encuentra nada o el css está vacío, intente con el xpath.
- [x] 2.3 Replicar la misma lógica de robustez de parseo JSON y fallback CSS -> XPath en DirectExtractionStrategy.cs.

## 3. Demo Mode en el Wizard

- [x] 3.1 Cambiar MaxProductsPerScrape de 2 a 5 en el método ExecuteRunTestScrapeAsync de ProviderWizardViewModel.cs para que la simulación pruebe varios elementos de la lista y del detalle.
- [x] 3.2 Añadir una etiqueta de texto en ProviderWizardView.xaml (en la pestaña de Preview) que indique explícitamente "Demo Mode: Información Simulada".
