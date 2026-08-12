## 1. Core Models & Export Filtering

- [x] 1.1 Agregar la opción `UseAI` en los modelos de configuración de scraping (`ScrapingOptions`).
- [x] 1.2 Implementar `AIEfficiencyMonitor` para rastrear si el uso de IA extrae campos adicionales respecto a los selectores base.
- [x] 1.3 Implementar el filtrado de metadatos `source_url` y `supplier name` en `FlashlyClient` y exportadores de productos.

## 2. Scraping Engine & Persistencia Inmediata

- [x] 2.1 Modificar la estrategia de extracción en `PlaywrightScrapingService` para ejecutar el guardado asíncrono en base de datos (`SaveProductAsync`) inmediatamente después de procesar cada producto.
- [x] 2.2 Emitir eventos de actualización de progreso por producto individual hacia la interfaz de usuario.

## 3. UI Desktop & Alerta Dinámica de IA

- [x] 3.1 Enlazar el checkbox "Utilizar IA" en `MainView` / `MainViewModel`.
- [x] 3.2 Conectar el evento de ineficiencia de IA en `MainViewModel` y desplegar el cuadro de diálogo de confirmación ("No es necesario que se siga usando IA").
- [x] 3.3 Permitir la desactivación en caliente del flag `UseAI` durante la ejecución activa del scraping.

## 4. Provider Wizard Contexto GPT & Mitigación Pre-Flight

- [x] 4.1 Agregar validación pre-flight HTTP (revisar 404) y sanitización/recorte de DOM HTML en `ProviderWizardViewModel` antes de llamar a OpenAI.
- [x] 4.2 Almacenar el contexto histórico de la respuesta de GPT en `ProviderWizardViewModel` para soporte de reintentos y refinamiento continuo.

## 5. Wizard Proveedor Base & Pre-Test de Selectores

- [x] 5.1 Agregar selector de "Proveedor Base" en la interfaz del `ProviderWizardView` y su binding en ViewModel.
- [x] 5.2 Implementar prueba previa de selectores del proveedor base contra el DOM de la URL objetivo sin invocar IA.
- [x] 5.3 Implementar análisis híbrido enviando selectores base + DOM sanitizado a la IA para obtener únicamente selectores/XPaths faltantes.
- [x] 5.4 Probar la clonación y validación de selectores tomando como referencia el proveedor `Festo`.
