# Análisis de Impacto y Plan de Modificación: Integración ScrapSAE y Flashly

**Fecha:** 02 de febrero de 2026
**Autor:** Manus AI

## 1. Introducción

Este documento presenta un análisis de impacto y un plan de acción detallado para modificar el proyecto **ScrapSAE**. El objetivo es alinear su capacidad de extracción y procesamiento de datos con los requerimientos de la plataforma de e-commerce **Flashly**. Actualmente, ScrapSAE realiza un scraping avanzado, pero la información recolectada no está completamente homologada con el esquema de datos que Flashly necesita para registrar y mostrar productos en la tienda en línea. 

La modificación central consiste en enriquecer el modelo de datos de ScrapSAE, ajustar la lógica de scraping para capturar información adicional como múltiples imágenes, stock y archivos adjuntos, y refinar el procesamiento de IA para estructurar esta información de acuerdo con el formato esperado por la API de Flashly. Se propone la adición de un campo `specifications` de tipo `JSONB` en la base de datos de Flashly como el receptáculo principal para los detalles técnicos del producto.

## 2. Análisis de Brechas (Gap Analysis)

Se ha realizado una comparación entre el modelo de datos de destino en Flashly (expuesto por su `AdminService`) y el modelo de datos actual que ScrapSAE produce tras el procesamiento con IA (`ProcessedProduct`). La siguiente tabla resume las brechas identificadas:

| Campo Requerido por Flashly | Tipo de Dato (Flashly) | Campo Actual en ScrapSAE | Tipo de Dato (ScrapSAE) | Estado de Mapeo | Acciones Requeridas |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `name` | `string` | `Name` | `string` | ✅ Mapeo Directo | Ninguna. |
| `sku` | `string` | `Sku` | `string?` | ✅ Mapeo Directo | Ninguna. |
| `description` | `string` | `Description` | `string` | ✅ Mapeo Directo | Ninguna. |
| `price` | `number` | `Price` | `decimal?` | ✅ Mapeo Directo | Ninguna. |
| `supplier_id` | `UUID` | `Brand` | `string?` | ⚠️ Requiere Transformación | Mapear la marca (`Brand`) a un `supplier_id` en Flashly. |
| `currency` | `string` | (No explícito) | N/A | ❌ **Ausente** | Añadir campo `Currency` a `ProcessedProduct` y extraerlo. |
| `stock` | `number` | (No explícito) | N/A | ❌ **Ausente** | Añadir campo `Stock` a `ProcessedProduct` y extraerlo. |
| `is_active` | `boolean` | (No explícito) | N/A | ⚠️ Requiere Lógica | Definir lógica de negocio (ej. `stock > 0`). |
| `images` | `string[]` | `ImageUrl` | `string?` | ⚠️ Requiere Expansión | Cambiar `ImageUrl` por `List<string> Images` y capturar galería completa. |
| `categories` | `UUID[]` | `SuggestedCategory` | `string?` | ⚠️ Requiere Expansión | Cambiar a `List<string> Categories` y mapear a UUIDs de Flashly. |
| `specifications` | `JSONB` | `Specifications` | `Dictionary<string, string>` | ✅ Mapeo Directo | El diccionario se puede serializar a JSON. |
| `attachments` | `object[]` | (No explícito) | N/A | ❌ **Ausente** | Añadir `List<Attachment> Attachments` y capturar enlaces a PDFs/manuales. |

## 3. Aspectos Funcionales y Elementos a Modificar

Para cerrar las brechas identificadas, es necesario realizar modificaciones en tres áreas clave del proyecto ScrapSAE: los modelos de datos, el servicio de procesamiento de IA y la lógica de scraping.

### 3.1. Expansión del Modelo de Datos (`ScrapSAE.Core`)

El DTO `ProcessedProduct` debe ser extendido para alojar la nueva información. Este será el nuevo contrato de datos que el resto de la aplicación utilizará.

- **`DTOs.cs`:** Modificar la clase `ProcessedProduct`.
  - **Reemplazar `ImageUrl`:** `public string? ImageUrl { get; set; }` se reemplazará por `public List<string> Images { get; set; } = new();`.
  - **Añadir Stock y Moneda:** Se agregarán los campos `public int? Stock { get; set; }` y `public string? Currency { get; set; }`.
  - **Añadir Archivos Adjuntos:** Se agregará una lista `public List<ProductAttachment> Attachments { get; set; } = new();`.
  - **Añadir un nuevo DTO `ProductAttachment`:** Se creará una nueva clase para representar los archivos adjuntos con campos como `FileName`, `FileUrl`, y `FileType`.

### 3.2. Refinamiento del Procesamiento de IA (`ScrapSAE.Infrastructure`)

El corazón del enriquecimiento de datos reside en el `OpenAIProcessorService`. Se debe modificar el *prompt* del sistema y el esquema JSON esperado para que la IA sepa qué información extraer y cómo estructurarla.

- **`AI/OpenAIProcessorService.cs`:** Actualizar el método `BuildProcessedProductRequest`.
  - **Modificar el Prompt del Sistema:** Se añadirán nuevas reglas al prompt para instruir a la IA sobre la extracción de los nuevos campos:
    > "**GALERÍA DE IMÁGENES:** Extrae todas las URLs de las imágenes del producto, no solo la principal. Devuélvelas en un array."
    > "**STOCK/INVENTARIO:** Busca indicadores de stock o cantidad disponible y extrae el valor numérico."
    > "**ARCHIVOS ADJUNTOS:** Identifica enlaces a documentos PDF, fichas técnicas o manuales. Extrae la URL y el texto del enlace."
  - **Actualizar el Esquema JSON:** El esquema `schema` dentro de la petición a la API de OpenAI se modificará para reflejar la nueva estructura de `ProcessedProduct`, incluyendo `images` (array de strings), `stock` (integer), `currency` (string) y `attachments` (array de objetos).

### 3.3. Homologación de la Información

El objetivo final es que `ScrapSAE` genere un objeto `ProcessedProduct` que pueda ser fácilmente convertido al formato JSON que la API de Flashly espera. El `specifications` (JSONB) será clave para la información variable.

- **Lógica de Mapeo:** Se deberá crear una nueva clase o servicio responsable de transformar el objeto `ProcessedProduct` de ScrapSAE al JSON para Flashly.
- **Ejemplo de Mapeo para `specifications`:**
  - El `Dictionary<string, string> Specifications` de ScrapSAE se serializará directamente.
  - Datos adicionales como `Brand`, `Model`, y otras características extraídas por la IA que no tengan un campo directo en Flashly se añadirán a este objeto JSON para asegurar que no se pierda información.

## 4. Plan de Modificación Detallado

A continuación, se presenta un plan de trabajo secuencial para implementar los cambios descritos.

### Fase 1: Actualización de los Modelos de Datos (C#)

1.  **Definir `ProductAttachment` DTO:**
    - **Archivo:** `src/ScrapSAE.Core/DTOs/DTOs.cs`
    - **Acción:** Crear una nueva clase `public class ProductAttachment { public string FileName { get; set; } public string FileUrl { get; set; } }`.

2.  **Modificar `ProcessedProduct` DTO:**
    - **Archivo:** `src/ScrapSAE.Core/DTOs/DTOs.cs`
    - **Acción:**
        - Reemplazar `public string? ImageUrl` por `public List<string> Images { get; set; } = new();`.
        - Añadir `public int? Stock { get; set; }`.
        - Añadir `public string? Currency { get; set; }`.
        - Añadir `public List<ProductAttachment> Attachments { get; set; } = new();`.

### Fase 2: Adaptación del Servicio de IA

1.  **Actualizar el Esquema JSON en `OpenAIProcessorService`:**
    - **Archivo:** `src/ScrapSAE.Infrastructure/AI/OpenAIProcessorService.cs`
    - **Acción:** Modificar el objeto `schema` en el método `BuildProcessedProductRequest` para que coincida con la nueva estructura de `ProcessedProduct`, incluyendo los nuevos campos y sus tipos (`images` como array, `stock` como integer, `attachments` como array de objetos).

2.  **Ampliar el Prompt del Sistema:**
    - **Archivo:** `src/ScrapSAE.Infrastructure/AI/OpenAIProcessorService.cs`
    - **Acción:** Añadir las nuevas instrucciones de extracción (múltiples imágenes, stock, archivos adjuntos) al `systemPrompt`.

### Fase 3: Implementación de la Lógica de Scraping

1.  **Mejorar la Extracción de Datos en `PlaywrightScrapingService`:**
    - **Archivo:** `src/ScrapSAE.Infrastructure/Scraping/PlaywrightScrapingService.cs`
    - **Acción:** Dentro de la lógica de extracción de detalles del producto (ej. `ExtractFestoProductsFromDetailPageAsync` o una función genérica), añadir código Playwright para:
        - Localizar y iterar sobre todos los elementos de la galería de imágenes para construir la lista de URLs.
        - Buscar elementos en la página que contengan texto como "Stock", "Disponibilidad", "En existencia" y extraer el valor numérico asociado.
        - Buscar enlaces `<a>` que apunten a archivos `.pdf`, `.zip`, o `.docx` y extraer tanto el `href` como el texto del enlace.
    - **Nota:** La información extraída (HTML, texto) se pasará al `RawData` que consume la IA, la cual se encargará de la estructuración final.

### Fase 4: Creación del Servicio de Envío a Flashly

1.  **Crear `FlashlyIntegrationService`:**
    - **Archivo:** `src/ScrapSAE.Infrastructure/Data/FlashlyIntegrationService.cs` (nuevo archivo).
    - **Acción:**
        - Crear un método `SendProductToFlashlyAsync(ProcessedProduct product)`.
        - Dentro de este método, construir un objeto anónimo o un DTO que represente el JSON esperado por la API de Flashly.
        - Realizar el mapeo de campos: `product.Name` -> `name`, `product.Images` -> `images`, etc.
        - Combinar `product.Specifications` y otros campos no mapeados directamente en el campo `specifications`.
        - Utilizar `HttpClient` para enviar la petición POST al endpoint `/api/products` de Flashly.

2.  **Integrar el Nuevo Servicio:**
    - **Acción:** Invocar `FlashlyIntegrationService.SendProductToFlashlyAsync` después de que un producto haya sido procesado por la IA y validado, probablemente desde el `ScrapingProcessManager` o un servicio similar.

Este plan proporciona una hoja de ruta clara para evolucionar ScrapSAE, permitiendo una integración de datos rica y estructurada con Flashly, y sentando las bases para una tienda en línea con información de producto detallada y completa.
