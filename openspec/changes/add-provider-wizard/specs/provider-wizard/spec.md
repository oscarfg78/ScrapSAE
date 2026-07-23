## ADDED Requirements

### Requirement: Iniciar wizard desde botón principal
El sistema SHALL proporcionar un botón "Agregar Proveedor" visible en la pantalla principal de ScrapSAE.Desktop que abra el wizard de creación de proveedores como ventana modal.

#### Scenario: Usuario abre el wizard
- **WHEN** el usuario hace clic en "Agregar Proveedor" en la pantalla principal
- **THEN** se abre una ventana modal `ProviderWizardView` en el Paso 1 (Ingreso de URL), con todos los campos vacíos y limpios

### Requirement: Paso 1 — Ingreso de URL
El wizard SHALL solicitar al usuario la URL base del catálogo de productos del proveedor en el Paso 1, con validación de formato antes de permitir avanzar.

#### Scenario: URL válida permite continuar
- **WHEN** el usuario ingresa una URL con esquema http o https y hace clic en "Analizar"
- **THEN** el wizard avanza al Paso 2 mostrando un indicador de carga mientras solicita el análisis al API

#### Scenario: URL inválida bloquea avance
- **WHEN** el usuario hace clic en "Analizar" con una URL vacía o con formato inválido
- **THEN** se muestra un mensaje de error de validación y el wizard no avanza

### Requirement: Paso 2 — Análisis IA de la página
El wizard SHALL llamar al endpoint `POST /api/sites/analyze` con la URL proporcionada y mostrar al usuario los resultados del análisis IA, incluyendo selectores sugeridos, campos detectados y nivel de confianza por campo.

#### Scenario: Análisis exitoso muestra resultados
- **WHEN** el API retorna un `PageAnalysisResult` exitoso
- **THEN** el wizard muestra: selectores primarios y secundarios sugeridos, estrategia recomendada, lista de campos detectados (SKU, nombre, imagen, precio, características) con indicador de confianza (Alta/Media/Baja) por cada uno

#### Scenario: Análisis falla por timeout o error
- **WHEN** el API retorna un error (timeout, página no accesible, etc.)
- **THEN** el wizard muestra un mensaje de error descriptivo y un botón "Reintentar" sin perder la URL ingresada

#### Scenario: Análisis en progreso muestra spinner
- **WHEN** el wizard está esperando la respuesta del análisis (puede tardar hasta 30s)
- **THEN** se muestra un spinner de carga con el mensaje "Analizando estructura del catálogo..." y un botón "Cancelar"

### Requirement: Paso 3 — Revisión y ajuste de configuración
El wizard SHALL mostrar la configuración propuesta por la IA en un formulario editable donde el usuario pueda ajustar los selectores, el nombre del proveedor y las estrategias de scraping antes de ejecutar el test.

#### Scenario: Usuario puede editar selectores propuestos
- **WHEN** el wizard está en el Paso 3
- **THEN** todos los campos de configuración (nombre, selectores primarios, selectores secundarios, estrategias) son editables con los valores propuestos por la IA pre-poblados

#### Scenario: Campos obligatorios validados
- **WHEN** el usuario hace clic en "Ejecutar Test" en el Paso 3
- **THEN** el sistema valida que el nombre del proveedor no esté vacío y que al menos un selector de producto esté definido; si no, muestra errores inline

### Requirement: Paso 4 — Test de scraping de validación
El wizard SHALL ejecutar un scrape de prueba con la configuración propuesta (máximo 120 productos) y mostrar los resultados en tiempo real, permitiendo al usuario validar que el scraping funciona correctamente antes de guardar.

#### Scenario: Scrape de prueba exitoso muestra preview de productos
- **WHEN** el test de scraping extrae al menos 1 producto
- **THEN** el wizard muestra una tabla con los primeros productos extraídos, indicando para cada uno los campos obtenidos (SKU, imagen, nombre, precio, características) con íconos de check/advertencia/error

#### Scenario: Scrape de prueba sin resultados
- **WHEN** el test de scraping no extrae ningún producto
- **THEN** el wizard muestra un mensaje de advertencia "No se encontraron productos. Revisa los selectores." con un botón "Volver a ajustar" que regresa al Paso 3

#### Scenario: Límite de productos en test
- **WHEN** el site tiene más de 120 productos disponibles
- **THEN** el test extrae solo los primeros 120 y muestra "Mostrando 120 de N productos encontrados"

### Requirement: Paso 5 — Confirmación y guardado
El wizard SHALL guardar el `SiteProfile` en Supabase con los valores confirmados por el usuario y mostrar una pantalla de éxito con acceso directo al proveedor recién creado.

#### Scenario: Guardado exitoso del proveedor
- **WHEN** el usuario hace clic en "Guardar Proveedor" en el Paso 5
- **THEN** el sistema persiste el `SiteProfile` con `IsActive = true`, `RequiresLogin = false`, `MaxProductsPerScrape = 120`, y retorna al usuario a la pantalla principal con el nuevo proveedor seleccionado

#### Scenario: Error al guardar
- **WHEN** el API retorna un error al intentar guardar el SiteProfile
- **THEN** el wizard muestra un mensaje de error y permite reintentar sin perder la configuración

### Requirement: Cancelación del wizard sin guardar
El wizard SHALL permitir al usuario cancelar el proceso en cualquier paso sin crear ningún proveedor persistente.

#### Scenario: Cancelación durante test — elimina site temporal
- **WHEN** el usuario cancela el wizard después de que el test de scraping creó un site temporal (prefijado `[TEMP]`)
- **THEN** el sistema elimina automáticamente el site temporal de Supabase y cierra el wizard

#### Scenario: Cancelación antes del test — no crea datos
- **WHEN** el usuario cancela el wizard en los pasos 1, 2 o 3
- **THEN** el wizard se cierra sin crear ningún registro en Supabase
