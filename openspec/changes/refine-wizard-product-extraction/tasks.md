## 1. Actualización del ViewModel del Wizard

- [x] 1.1 Modificar `ProviderWizardViewModel.cs` para incluir la propiedad `MaxProductsTest` en el paso de Configuración/Test.
- [x] 1.2 Actualizar la UI (XAML) del wizard de pruebas para enlazar la entrada del usuario con `MaxProductsTest`.

## 2. Soporte del Modo Demo en Extracción

- [x] 2.1 Modificar parámetros de ejecución (`ExtractionExecutionRequest` u homólogo) pasados al orquestador para admitir el flag de modo `Demo` y el límite de productos (`MaxProducts`).
- [x] 2.2 Actualizar `IScrapingRunner` (y/u orquestador) para aislar la ejecución de la demo, asegurando que no se guarden datos en negocio y que retorne un reporte autocontenido.
- [x] 2.3 Actualizar el retorno del runner para la vista Test, devolviendo un conjunto de resultados simulados reales y su diagnóstico en memoria.

## 3. Extracción de Detalle y Descubrimiento

- [x] 3.1 Actualizar el pipeline de análisis para que extraiga todos los datos del producto (sin reducir el contrato de selectores duales) a partir de la URL de detalle.
- [x] 3.2 Modificar la lógica de DOM parsing en el paso de Análisis para detectar `CandidateUrls` (candidatos de catálogo) basados en proximidad de URLs a la URL del detalle.
- [x] 3.3 Permitir que el Wizard almacene los candidatos descubiertos para usarlos si el usuario lo confirma.

## 4. Integración y Pruebas del Wizard

- [x] 4.1 Conectar la ejecución del comando de Demo en el Wizard para que pase `MaxProductsTest` y `IsDemo=true` al orquestador.
- [x] 4.2 Renderizar los resultados de la demo directamente desde el reporte autocontenido devuelto, en lugar de consultar la base de datos (staging).
- [x] 4.3 Verificar el ciclo E2E en el Wizard para garantizar que no hay persistencia de negocio indeseada al realizar el test.
