## 1. Modificación de Modelos y ViewModels

- [x] 1.1 Agregar propiedad `ProductDetailUrl` en `DiscoveryConfig` (y/o contratos que utiliza la API de Scraping).
- [x] 1.2 Agregar propiedad observable `ProductDetailUrl` en `ProviderWizardViewModel`.

## 2. Actualización de Interfaz de Usuario

- [x] 2.1 Agregar `TextBox` para la entrada de "Product Detail URL" en el paso 1 del `ProviderWizard` (archivo XAML).
- [x] 2.2 Asegurar que el campo es opcional, vinculándolo a la propiedad en el ViewModel.

## 3. Lógica de Scraping y Descubrimiento

- [x] 3.1 Actualizar `PageAnalysisService.AnalyzeAsync` y su contrato para recibir y pasar la URL al análisis con GPT.
- [x] 3.2 Modificar la lógica interna en `PageAnalysisService` para evaluar si `ProductDetailUrl` tiene valor. Si lo tiene, navegar a esa URL para extraer la estructura de detalle (HTML) y enviarlo al prompt junto con el HTML del catálogo, ignorando el primer producto del catálogo.
- [x] 3.3 Validar que el fallback se mantenga (navegar al primer producto o usar solo el catálogo si no se proporciona `ProductDetailUrl`).

## 4. Pruebas y Validación

- [x] 4.1 Ejecutar el proyecto (Desktop y Api).
- [x] 4.2 Probar con una URL de catálogo y una `ProductDetailUrl` válida.
- [x] 4.3 Verificar que el resultado de selectores en el paso 2 corresponda a la información del producto proporcionado.
- [x] 4.4 Verificar la creación del perfil con ambas URLs.electores correctamente.
- [x] 4.5 Ejecutar un descubrimiento dejando la URL de detalle en blanco para asegurar el funcionamiento original.
