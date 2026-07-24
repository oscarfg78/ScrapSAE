## Why

Actualmente, el proceso de descubrimiento y alta de proveedores depende de que el primer producto del catálogo tenga correctamente desplegada la descripción para que el análisis de la estructura sea exitoso. Solicitando una URL específica de un producto con el detalle completo, aseguramos que el sistema pueda identificar correctamente los selectores o estructura para la descripción, independientemente del primer producto listado en el catálogo.

## What Changes

- Modificación del Wizard de Descubrimiento y Alta de Proveedores (UI) para incluir un nuevo campo de entrada: `URL de Detalle de Producto`.
- Actualización de los modelos y lógica de negocio para pasar esta nueva URL al servicio de scraping durante la fase de análisis/descubrimiento.
- Ajuste en las estrategias de scraping para que, al buscar los selectores de detalle (como la descripción o características), utilicen preferentemente la URL de detalle proporcionada en lugar de depender del primer elemento del catálogo.

## Capabilities

### New Capabilities

- `provider-wizard-product-detail`: Se agrega la capacidad de proveer una URL de producto específico durante la configuración inicial del proveedor para mejorar la precisión del descubrimiento de la página de detalle.

### Modified Capabilities

- `provider-discovery`: Modificación de la configuración de descubrimiento para recibir y utilizar la URL de detalle del producto en el análisis de campos de detalle.

## Impact

- Interfaz de usuario (WPF/XAML) del `ProviderWizard`.
- ViewModels correspondientes (`ProviderWizardViewModel` y relacionados).
- Contratos de los servicios de la API que orquestan el descubrimiento.
- `ScrapingRunner` y Estrategias de Scraping (ej. `PlaywrightScrapingService`) para utilizar la nueva URL al intentar inferir los campos del detalle del producto.
