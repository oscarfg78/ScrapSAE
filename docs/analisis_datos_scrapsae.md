# Análisis de Estructura de Datos en ScrapSAE

## Flujo de Datos en ScrapSAE

### 1. Datos Crudos (Scraping)

**Clase:** `ScrapedProduct` (DTOs.cs)

Representa los datos extraídos directamente del sitio web durante el proceso de scraping:

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `SkuSource` | string? | SKU del producto según el proveedor |
| `Title` | string? | Título/nombre del producto |
| `Description` | string? | Descripción del producto |
| `RawHtml` | string? | HTML completo de la página del producto |
| `ScreenshotBase64` | string? | Captura de pantalla en Base64 |
| `ImageUrl` | string? | URL de la imagen principal |
| `Price` | decimal? | Precio del producto |
| `Category` | string? | Categoría del producto |
| `Brand` | string? | Marca del producto |
| `SourceUrl` | string? | URL de donde se extrajo |
| `Attributes` | Dictionary<string, string> | Atributos adicionales |
| `NavigationUrls` | List<string> | URLs relacionadas descubiertas |
| `ScrapedAt` | DateTime | Fecha y hora de extracción |
| `AiEnriched` | bool | Indica si fue enriquecido por IA |

### 2. Datos Procesados (IA)

**Clase:** `ProcessedProduct` (DTOs.cs)

Representa los datos estructurados y enriquecidos por IA:

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `Sku` | string? | SKU normalizado |
| `Name` | string | Nombre del producto |
| `Brand` | string? | Marca identificada |
| `Model` | string? | Modelo del producto |
| `Description` | string | Descripción estructurada |
| `Features` | List<string> | Lista de características |
| `Specifications` | Dictionary<string, string> | Especificaciones técnicas |
| `SuggestedCategory` | string? | Categoría sugerida |
| `LineCode` | string? | Código de línea SAE |
| `Price` | decimal? | Precio |
| `ConfidenceScore` | decimal? | Nivel de confianza de la IA |
| `OriginalRawData` | string? | Referencia a datos originales |

### 3. Datos en Staging

**Clase:** `StagingProduct` (Entities.cs)

Representa productos en estado de validación antes de enviar a SAE:

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `Id` | Guid | Identificador único |
| `SiteId` | Guid | ID del sitio proveedor |
| `SkuSource` | string? | SKU del proveedor |
| `SkuSae` | string? | SKU asignado en SAE |
| `RawData` | string? | Datos crudos en JSON |
| `AIProcessedJson` | string? | Datos procesados por IA en JSON |
| `Status` | string | Estado: pending, validated, sent, error |
| `ExcludeFromSae` | bool | Excluir del envío a SAE |
| `ValidationNotes` | string? | Notas de validación |
| `SourceUrl` | string? | URL de origen |
| `Attempts` | int | Intentos de procesamiento |
| `LastSeenAt` | DateTime? | Última vez visto en el sitio |
| `CreatedAt` | DateTime | Fecha de creación |
| `UpdatedAt` | DateTime | Fecha de actualización |

### 4. Datos para SAE

**Clase:** `ProductSAE` (SAEEntities.cs)

Estructura para enviar a Aspel SAE:

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `CVE_ART` | string | Clave del artículo (SKU) |
| `DESCR` | string? | Descripción |
| `LIN_PROD` | string? | Línea de producto |
| `EXIST` | decimal? | Existencia |
| `PREC_X_MAY` | decimal? | Precio mayoreo |
| `PREC_X_MEN` | decimal? | Precio menudeo |
| `ULT_COSTO` | decimal? | Último costo |
| `CTRL_ALM` | string? | Control almacén |
| `STATUS` | string? | Estado |
| `FCH_ULTCOM` | DateTime? | Fecha última compra |
| `FCH_ULTVTA` | DateTime? | Fecha última venta |
| `CAMPO_LIBRE1` | string? | Campo libre 1 |
| `CAMPO_LIBRE2` | string? | Campo libre 2 |
| `CAMPO_LIBRE3` | string? | Campo libre 3 |

## Procesamiento de IA (OpenAIProcessorService)

### Esquema JSON de Salida de IA

El servicio de IA procesa los datos crudos y genera un JSON estructurado con el siguiente esquema:

```json
{
  "sku": "string | null",
  "name": "string",
  "brand": "string | null",
  "model": "string | null",
  "description": "string",
  "features": ["string"],
  "specifications": {
    "key": "value"
  },
  "suggestedCategory": "string | null",
  "lineCode": "string | null",
  "price": "number | null",
  "confidenceScore": "number"
}
```

### Prompt del Sistema (Extracto)

El sistema de IA está configurado para:

1. **Identificar SKU/Part Number:** Buscar códigos de artículo (ej. VAMC-L1-CD en Festo)
2. **Identificar Marca:** Extraer la marca del producto
3. **Extraer Precio:** Capturar valores numéricos sin símbolos de moneda
4. **Sugerir Categoría:** Clasificar el producto según nombre y descripción
5. **Extraer Especificaciones:** Capturar características técnicas en formato estructurado

## Información Capturada Actualmente

### Datos Básicos
- ✅ SKU/Código de producto
- ✅ Nombre/Título
- ✅ Descripción
- ✅ Precio
- ✅ Marca
- ✅ Modelo
- ✅ Categoría

### Datos Enriquecidos
- ✅ Características (Features)
- ✅ Especificaciones técnicas (Specifications)
- ✅ HTML completo de la página
- ✅ Captura de pantalla
- ✅ URL de origen
- ✅ URLs relacionadas

### Datos de Contexto
- ✅ Fecha de extracción
- ✅ Nivel de confianza de IA
- ✅ Estado de procesamiento
- ✅ Notas de validación

## Información NO Capturada Actualmente

### Datos de E-commerce
- ❌ Stock/Inventario
- ❌ Múltiples imágenes (solo imagen principal)
- ❌ Archivos adjuntos (PDFs, manuales)
- ❌ Variantes de producto estructuradas
- ❌ Información de envío
- ❌ Garantía
- ❌ Dimensiones físicas estructuradas
- ❌ Peso

### Datos de Clasificación
- ❌ Múltiples categorías
- ❌ Tags/Etiquetas
- ❌ Atributos de búsqueda

### Datos Comerciales
- ❌ Precios por volumen
- ❌ Descuentos
- ❌ Disponibilidad por región
- ❌ Tiempo de entrega

## Observaciones

### Fortalezas del Sistema Actual

1. **Captura Exhaustiva:** El sistema captura HTML completo y screenshots, lo que permite re-procesamiento posterior
2. **Enriquecimiento IA:** La IA estructura los datos crudos en formato normalizado
3. **Flexibilidad:** El campo `Specifications` (Dictionary) permite almacenar datos técnicos variables
4. **Trazabilidad:** Se mantiene el vínculo con la URL de origen y datos crudos

### Limitaciones para E-commerce

1. **Imágenes:** Solo se captura una imagen principal, no un array de imágenes
2. **Stock:** No se captura información de inventario
3. **Archivos:** No se descargan PDFs, manuales o fichas técnicas
4. **Variantes:** Las variantes se procesan pero no se estructuran como productos relacionados
5. **Categorización:** Solo se asigna una categoría, no múltiples

### Campos que Requieren Expansión

Para integración con Flashly, se necesitan los siguientes campos adicionales:

1. **`images`** - Array de URLs de imágenes (actualmente solo `ImageUrl`)
2. **`stock`** - Cantidad en inventario (no capturado)
3. **`in_stock`** - Booleano de disponibilidad (no capturado)
4. **`currency`** - Moneda del precio (no explícito)
5. **`attachments`** - Array de archivos adjuntos (no capturado)
6. **`categories`** - Array de IDs de categorías (actualmente solo una)
7. **`weight`** - Peso del producto (no capturado)
8. **`dimensions`** - Dimensiones estructuradas (no capturado)

## Recomendaciones

### Corto Plazo

1. **Expandir `ProcessedProduct`** para incluir campos de e-commerce
2. **Modificar el prompt de IA** para extraer múltiples imágenes y datos de stock
3. **Agregar campo `currency`** explícito en lugar de asumirlo

### Mediano Plazo

1. **Implementar descarga de archivos adjuntos** (PDFs, manuales)
2. **Estructurar variantes** como productos relacionados
3. **Capturar múltiples categorías** cuando estén disponibles

### Largo Plazo

1. **Sistema de normalización de unidades** (convertir "100mm" a estructura estándar)
2. **Extracción de dimensiones y peso** desde descripciones
3. **Clasificación automática en taxonomía de Flashly**
