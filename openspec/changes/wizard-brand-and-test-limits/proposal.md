## Why

Actualmente, el asistente (Wizard) para configurar nuevos proveedores captura información básica pero no permite especificar de antemano el nombre de la marca que se asignará a los productos extraídos. Adicionalmente, durante la prueba de extracción se analizan demasiados productos (120), lo que la hace lenta, cuando solo se requieren unos pocos para verificar que los selectores funcionan. Sin embargo, al guardar el perfil, el número predeterminado de productos a extraer por lote debe configurarse en 120. Esta mejora agilizará la configuración y las pruebas iniciales.

Por otro lado, el motor de "descubrimiento y prueba" que se utiliza en el Wizard es robusto y muy efectivo. Se requiere integrar este mismo motor en la **pantalla principal de Scraping** para mejorar el proceso de extracción regular. Esta integración debe ser retrocompatible y sumar estabilidad sin romper los perfiles de proveedores existentes, acompañándose de mejoras en la UI para brindar mayor visibilidad del estado de ejecución (por ejemplo, mostrando claramente qué fase del descubrimiento se está ejecutando).

## What Changes

- Modificación del Wizard (interfaz de usuario) para incluir un campo de captura de la marca (brand) en el paso inicial o correspondiente.
- Actualización de la lógica de prueba del Wizard para limitar el número de productos extraídos a 10 durante la simulación/test.
- Asegurar que al finalizar el Wizard y guardar el perfil del proveedor, el valor `MaxProductsPerJob` o el parámetro equivalente se establezca por defecto en 120.
- Reutilización del motor de "descubrimiento y prueba" (usado en el Wizard) en la pantalla principal de Scraping.
- Implementación de un modelo híbrido/aditivo donde el proceso de descubrimiento se suma a la lógica de scraping actual para mejorar la fiabilidad.
- Ajustes de diseño y estructura en la interfaz de la pantalla principal de Scraping para mostrar feedback visual detallado del proceso en tiempo real.

## Capabilities

### New Capabilities
- `wizard-brand-capture`: Capacidad de definir la marca asociada a un proveedor directamente desde el Wizard de configuración.
- `wizard-test-limits`: Diferenciación entre el límite de productos para la fase de prueba en el Wizard (10) y el límite para operaciones reales guardadas (120).
- `scraping-screen-discovery-integration`: Integración de la lógica de descubrimiento y test (del Wizard) en el ciclo de scraping principal, manteniendo retrocompatibilidad y mejorando el feedback visual en la UI.

### Modified Capabilities

## Impact

- Interfaz de usuario del Wizard en `ScrapSAE.Desktop`.
- Lógica de testing de scraping invocada desde el Wizard y desde la pantalla de Scraping (`PlaywrightScrapingService` / `ScrapingRunner`).
- Lógica de persistencia de perfiles (`SiteProfile`).
- Interfaz principal de Scraping (`ScrapSAE.Desktop`), específicamente el layout para reportar estados granulares (como "Descubriendo familias", "Explorando paginación", "Extrayendo productos").
