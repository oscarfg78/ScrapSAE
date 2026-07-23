## 1. DTOs y Modelos del Dominio (ScrapSAE.Core)

- [x] 1.1 Crear `PageAnalysisRequest` DTO con propiedad `Url` (string)
- [x] 1.2 Crear `DetectedField` DTO con propiedades `Name`, `Selector` (nullable), `Confidence` (enum: High/Medium/Low)
- [x] 1.3 Crear `StrategyRecommendation` DTO con `StrategyName` y `Priority`
- [x] 1.4 Crear `PageAnalysisResult` DTO con todos los campos definidos en la spec (`ProductContainerSelector`, `ProductCardSelector`, `SkuSelector`, `NameSelector`, `ImageSelector`, `PriceSelector`, `CharacteristicsSelector`, `SecondarySelectors`, `RecommendedStrategies`, `DetectedFields`, `AnalysisSummary`, `PageTitle`, `DetectedLanguage`, `IsProductCatalog`)
- [x] 1.5 Agregar `WizardScrapePreviewProduct` DTO para el resultado del scrape de prueba (SKU, nombre, imagen, precio, características, campos detectados)

## 2. Servicio de Análisis de Página (ScrapSAE.Infrastructure / ScrapSAE.Api)

- [x] 2.1 Crear `IPageAnalysisService` interface en `ScrapSAE.Core.Interfaces` con método `AnalyzeAsync(string url, CancellationToken)`
- [x] 2.2 Crear `PageAnalysisService` en `ScrapSAE.Infrastructure.AI` que use `IScrapingService` para obtener HTML renderizado via Playwright (modo headless, esperar `networkidle`)
- [x] 2.3 Implementar lógica de extracción y truncado del HTML del body (máximo 50,000 chars, priorizando secciones con mayor densidad de listas)
- [x] 2.4 Implementar prompt especializado para GPT que solicite structured output con el esquema `PageAnalysisResult`: detectar contenedor de lista, tarjetas de producto, selectores por campo (SKU/nombre/imagen/precio/características), nivel de confianza por campo, y estrategia recomendada
- [x] 2.5 Implementar deserialización del JSON retornado por GPT al DTO `PageAnalysisResult` con manejo robusto de errores
- [x] 2.6 Agregar timeout de 30s al proceso completo de análisis (browser + AI)
- [x] 2.7 Registrar `PageAnalysisService` en el DI de `ScrapSAE.Api/Program.cs`

## 3. Endpoint de Análisis (ScrapSAE.Api)

- [x] 3.1 Agregar endpoint `POST /api/sites/analyze` en `Program.cs` que reciba `PageAnalysisRequest` y llame a `IPageAnalysisService`
- [x] 3.2 Retornar HTTP 200 con `PageAnalysisResult` en caso de éxito
- [x] 3.3 Retornar HTTP 422 con mensaje descriptivo si la URL es inaccesible
- [x] 3.4 Retornar HTTP 408 si el análisis supera el timeout de 30s
- [x] 3.5 Agregar endpoint `DELETE /api/sites/temp` (o lógica de limpieza) que elimine `SiteProfile` con nombre prefijado `[TEMP]` y `CreatedAt` hace más de 60 minutos

## 4. Job de Limpieza de Sites Temporales (ScrapSAE.Api)

- [x] 4.1 Crear `TempSiteCleanupService` como `IHostedService` que corra cada 15 minutos
- [x] 4.2 El servicio consulta `config_sites` por registros con `name` LIKE `[TEMP]%` y `created_at < now() - 60min` y los elimina
- [x] 4.3 Registrar el hosted service en `Program.cs`

## 5. ApiClient en Desktop (ScrapSAE.Desktop)

- [ ] 5.1 Agregar método `AnalyzePageAsync(string url)` en `ApiClient.cs` que llame a `POST /api/sites/analyze`
- [ ] 5.2 Agregar método `DeleteTempSitesAsync()` que llame al endpoint de limpieza de temporales

## 6. ProviderWizardViewModel (ScrapSAE.Desktop)

- [ ] 6.1 Crear `ProviderWizardViewModel.cs` con propiedades observables para el estado del wizard: `CurrentStep` (int), `Url`, `AnalysisResult`, `WizardConfig` (editable), `ScrapePreviewProducts`, `IsBusy`, `StatusMessage`
- [ ] 6.2 Implementar comando `AnalyzeCommand` (Paso 1→2): llama `AnalyzePageAsync` y actualiza `AnalysisResult`; maneja errores y timeout
- [ ] 6.3 Implementar `PopulateConfigFromAnalysis()` que mapea `PageAnalysisResult` a los campos editables del `WizardConfig` (Paso 2→3)
- [x] 6.1 Crear `ProviderWizardViewModel.cs` con propiedades observables para el estado del wizard: `CurrentStep` (int), `Url`, `AnalysisResult`, `WizardConfig` (editable), `ScrapePreviewProducts`, `IsBusy`, `StatusMessage`
- [x] 6.2 Implementar comando `AnalyzeCommand` (Paso 1→2): llama `AnalyzePageAsync` y actualiza `AnalysisResult`; maneja errores y timeout
- [x] 6.3 Implementar `PopulateConfigFromAnalysis()` que mapea `PageAnalysisResult` a los campos editables del `WizardConfig` (Paso 2→3)
- [x] 6.4 Implementar validación del formulario de configuración (Paso 3): nombre no vacío, al menos un selector de producto definido
- [x] 6.5 Implementar comando `RunTestScrapeCommand` (Paso 3→4): crea site temporal `[TEMP] NombreProveedor`, llama `POST /api/scraping/run/{tempSiteId}`, captura resultados
- [x] 6.6 Implementar lógica del Paso 4: si 0 productos encontrados, retornar al Paso 3 con mensaje; si >0, avanzar al Paso 5 con preview
- [x] 6.7 Implementar comando `SaveProviderCommand` (Paso 5): actualiza el site temporal (quitar prefijo `[TEMP]`, poner `IsActive=true`, `RequiresLogin=false`, `MaxProductsPerScrape=120`) o crea uno nuevo si no había temporal; navega de vuelta a la pantalla principal con el site seleccionado
- [x] 6.8 Implementar comando `CancelCommand`: si hay site temporal, lo elimina; cierra la ventana sin guardar

## 7. ProviderWizardView (ScrapSAE.Desktop WPF)

- [x] 7.1 Crear `ProviderWizardView.xaml` como `Window` modal con `DataContext` = `ProviderWizardViewModel`
- [x] 7.2 Implementar indicador de pasos (breadcrumb visual 1-2-3-4-5) con el paso actual resaltado
- [x] 7.3 Implementar **Paso 1** (UI): campo de texto para URL con botón "Analizar"
- [x] 7.4 Implementar **Paso 2** (UI): spinner de carga durante análisis; al completar, mostrar tabla de campos detectados (nombre, selector sugerido, indicador confianza con color: verde=High, amarillo=Medium, rojo=Low), estrategia recomendada y resumen textual del análisis
- [x] 7.5 Implementar **Paso 3** (UI): formulario editable con TextBox para nombre del proveedor, selectores primarios y secundarios; CheckBox para estrategias habilitadas; mensajes de validación inline
- [x] 7.6 Implementar **Paso 4** (UI): spinner durante el scrape; al completar, DataGrid con preview de productos (columnas: SKU, Nombre, Imagen URL, Precio, # Características); iconos check/advertencia por campo; mensaje "Mostrando N/Max productos"
- [x] 7.7 Implementar **Paso 5** (UI): pantalla de resumen con estadísticas del test (N productos extraídos, campos con cobertura alta/media/baja), botón "Guardar Proveedor" y botón "Volver a Ajustar"
- [x] 7.8 Implementar spinner overlay global en la ventana (visible cuando `IsBusy = true`) con botón "Cancelar" que cancele el `CancellationToken`
- [x] 7.9 Manejar todos los estados de error con mensajes amigables y opciones de retry

## 8. Integración con Pantalla Principal (ScrapSAE.Desktop)

- [x] 8.1 Agregar botón "Agregar Proveedor" prominente en la sección de proveedores de `MainWindow.xaml`
- [x] 8.2 Implementar comando en `MainViewModel.cs` que instancie y abra `ProviderWizardView` como dialog modal
- [x] 8.3 Después de cerrar el wizard con éxito, recargar la lista de proveedores y seleccionar el proveedor recién creado

## 9. Pruebas de Integración

- [ ] 9.1 Probar el endpoint `POST /api/sites/analyze` con al menos 2 URLs de proveedores reales distintos y verificar que el `PageAnalysisResult` es coherente
- [ ] 9.2 Probar el flujo completo del wizard en Desktop con un proveedor de prueba, desde la URL hasta el guardado final
- [ ] 9.3 Verificar que la cancelación en Paso 4 (después del scrape temporal) elimina correctamente el site temporal de Supabase
- [ ] 9.4 Verificar que el job de limpieza de sites `[TEMP]` funciona correctamente
