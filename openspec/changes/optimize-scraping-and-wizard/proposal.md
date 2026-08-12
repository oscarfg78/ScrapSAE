## Why

Durante la extracción de datos (scraping) y configuración de nuevos proveedores en ScrapSAE, se requiere optimizar el consumo de créditos de IA, asegurar la persistencia inmediata de datos extraídos, filtrar metadatos en la exportación a tienda y potenciar el Provider Wizard reutilizando proveedores existentes como base para reducir análisis redundantes.

## What Changes

- **Optimización de recursos e Inteligencia Artificial en Scraping**:
  - Opción "Utilizar IA" configurable por el usuario antes y durante la ejecución de scrap.
  - Evaluación continua del rendimiento de IA: si el uso de IA no está aportando extracciones efectivas, el sistema lanza una alerta dialogada ("No es necesario que se siga usando IA") permitiendo al usuario desactivarla o mantenerla.
- **Persistencia Inmediata por Producto**:
  - Al terminar la extracción de cada producto individual, la información se guarda inmediatamente en la base de datos local y se notifica al usuario/UI, en lugar de esperar al final de todo el proceso.
- **Limpieza en Exportación a Tienda (Flashly)**:
  - Al exportar productos a la tienda en línea, se omiten explícitamente los metadatos `source_url` y `supplier name`.
- **Estrategia de Mitigación y Contexto GPT en Análisis de Proveedores**:
  - Guardar la respuesta estructurada de GPT para reutilizarla como contexto histórico en reintentos.
  - Manejo resiliente de fallos previos a GPT (páginas 404, insumos no encontrados, payload/DOM demasiado extenso), aplicando estrategias específicas como limpieza/recorte de DOM o reintentos con contexto previo.
- **Test de Selectores Existentes como Base en el Wizard**:
  - Reutilización de selectores de proveedores conocidos (ej. Festo) como plantilla previa.
  - Prueba rápida pre-análisis: Si la estructura HTML coincide y solo cambia la URL base, copiar directamente los selectores sin consumir llamadas a la IA.
- **Wizard Mejorado con Base Existente e IA Híbrida**:
  - Opción en el wizard para elegir un proveedor base existente.
  - Envío combinado de selectores base + DOM a la IA para inferir/comprobar únicamente los selectores o XPaths faltantes, validándolos interactivamente en la interfaz.

## Capabilities

### New Capabilities
- `scraping-resource-optimization`: Control dinámico de IA en ejecuciones de scrap, monitoreo de eficiencia con alertas, y persistencia inmediata por registro de producto.
- `provider-wizard-enhancements`: Clonación/prueba de selectores basados en proveedores existentes, mitigación de fallos en análisis GPT y generación híbrida de selectores/XPaths.

### Modified Capabilities
- `provider-wizard-product-detail`: Integración de proveedores base, refinamiento de prompts/contexto GPT y mitigación de errores de entrada en el Wizard.

## Impact

- **Scraping Engine & Worker Service**: Modificaciones en `PlaywrightScrapingService`, pipeline de extracción y eventos de guardado incremental.
- **Desktop UI (WPF)**: Nuevas opciones en `MainViewModel`, `ProviderWizardViewModel`, alertas dinámicas y cuadros de diálogo.
- **Export Integration (Flashly)**: Filtrado de campos `source_url` y `supplier name` antes del envío de datos.
