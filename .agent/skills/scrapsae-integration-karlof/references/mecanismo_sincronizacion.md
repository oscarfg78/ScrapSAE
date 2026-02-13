# Diseño del Mecanismo de Sincronización Desacoplado

## 1. Introducción

Para garantizar la flexibilidad y escalabilidad de la integración entre **ScrapSAE** y **Flashly**, se propone un mecanismo de sincronización desacoplado. Esto permitirá que Flashly pueda recibir datos de productos de diversas fuentes, no solo de ScrapSAE. Se definen dos métodos principales: un **Layout de Importación** y una **API de Productos**.

## 2. Opción 1: Layout de Importación (CSV/Excel)

Este método es ideal para cargas masivas (bulk) y procesos manuales o por lotes.

### 2.1. Estructura del Layout

Se definirá una plantilla en formato CSV o Excel con las siguientes columnas. Esta plantilla será la fuente de verdad para la importación.

| Columna | Nombre Técnico | Tipo | Requerido | Descripción |
| :--- | :--- | :--- | :--- | :--- |
| SKU Proveedor | `source_sku` | Texto | Sí | SKU o identificador único del producto en el sitio del proveedor. |
| Nombre Producto | `name` | Texto | Sí | Nombre del producto. |
| Descripción | `description` | Texto | No | Descripción detallada del producto. |
| Precio Compra | `purchase_price` | Decimal | Sí | Precio de compra extraído del sitio del proveedor. |
| Moneda | `currency` | Texto | No | Moneda del precio (ej. 'MXN', 'USD'). Default: 'MXN'. |
| Categorías | `categories` | Texto | No | Nombres de las categorías, separadas por un delimitador (ej. '|' o ';'). |
| URL Producto | `product_url` | Texto | No | URL de la página del producto en el sitio del proveedor. |
| URLs Imágenes | `image_urls` | Texto | No | URLs de las imágenes, separadas por un delimitador. |
| Proveedor | `supplier_name` | Texto | No | Nombre del proveedor. Si no se proporciona, se puede asignar uno por defecto. |
| Especificaciones | `specifications_json` | Texto (JSON) | No | Un string JSON con las especificaciones técnicas. |

### 2.2. Proceso de Implementación en Flashly

1.  **Desarrollar un Módulo de Importación:** Crear una nueva sección en el panel de administración de Flashly.
2.  **Carga de Archivo:** Permitir al administrador subir el archivo CSV/Excel.
3.  **Mapeo de Columnas:** La interfaz debería permitir mapear las columnas del archivo a los campos de la base de datos de Flashly, aunque lo ideal es que el archivo se adhiera a la plantilla estándar.
4.  **Proceso de Background:** La importación se ejecutará como un trabajo en segundo plano para no bloquear la interfaz de usuario, especialmente con archivos grandes.
5.  **Validación y Reporte:** Durante la importación, se validarán los datos de cada fila. Al finalizar, se generará un reporte indicando los productos creados, actualizados y los errores encontrados (ej. 'SKU duplicado', 'Precio inválido').
6.  **Estado Inicial:** Los productos importados se crearán con el estatus `pending_validation`.

### 2.3. Adaptación en ScrapSAE

ScrapSAE deberá ser modificado para generar este archivo CSV/Excel como una de sus salidas. El `SAEIntegrationService` sería reemplazado o complementado por un `FileOutputService` que, a partir de los datos en la tabla `staging_products`, genere el layout final.

## 3. Opción 2: API de Productos (RESTful)

Este método es ideal para sincronizaciones en tiempo real o más frecuentes y automatizadas.

### 3.1. Especificación de la API

Flashly expondrá un nuevo conjunto de endpoints en su API (manejada por Cloudflare Workers) para la gestión de productos.

**Endpoint Principal:** `POST /api/v1/products/sync`

Este endpoint aceptará una solicitud para crear o actualizar productos. Para mantener la seguridad, el acceso estará protegido por una clave de API que ScrapSAE deberá incluir en las cabeceras.

**Cabeceras de la Solicitud:**

```
Content-Type: application/json
X-API-Key: [CLAVE_SECRETA_GENERADA_POR_FLASHLY]
```

**Cuerpo de la Solicitud (Request Body):**

El cuerpo será un objeto JSON que contiene un arreglo de productos.

```json
{
  "products": [
    {
      "source_sku": "PROD-001-XYZ",
      "name": "Producto de Ejemplo 1",
      "description": "Esta es la descripción del producto.",
      "purchase_price": 99.99,
      "currency": "MXN",
      "categories": ["Categoría A", "Subcategoría B"],
      "product_url": "https://proveedor.com/prod-001",
      "image_urls": [
        "https://proveedor.com/img1.jpg",
        "https://proveedor.com/img2.jpg"
      ],
      "supplier_name": "Proveedor Principal",
      "specifications_json": "{\"voltaje\": \"110V\", \"potencia\": \"500W\"}"
    },
    {
       // ... más productos
    }
  ]
}
```

**Respuesta de la API (Response Body):**

La API procesará la solicitud de forma asíncrona y devolverá una respuesta inmediata confirmando la recepción.

```json
{
  "status": "success",
  "message": "Solicitud de sincronización recibida. Se procesarán 2 productos.",
  "job_id": "e7a4c2b0-8e1f-4a9c-8d6f-9b1a3c0e5d7f"
}
```

Opcionalmente, se puede implementar un endpoint `GET /api/v1/products/sync/status/{job_id}` para consultar el estado del trabajo de sincronización.

### 3.2. Lógica de Sincronización en Flashly

*   Al recibir la solicitud, Flashly buscará cada producto por su `source_sku` y `supplier_name`.
*   **Si el producto no existe:** Se creará un nuevo registro en la tabla `products` con el estatus `pending_validation`.
*   **Si el producto ya existe:** Se actualizará la información (nombre, descripción, precio de compra, imágenes, etc.). El estatus del producto no se modificará automáticamente para no interferir con el trabajo de un administrador.
*   **Categorías:** Si las categorías enviadas no existen, se crearán automáticamente en la tabla `categories` de Flashly.

### 3.3. Adaptación en ScrapSAE

El `SAEIntegrationService` en ScrapSAE será modificado o extendido. En lugar de conectarse a la base de datos de SAE, se implementará un cliente HTTP que:

1.  Recopile los productos desde la tabla `staging_products` que estén en estado `validated`.
2.  Construya el cuerpo de la solicitud JSON como se especificó anteriormente.
3.  Envíe la solicitud `POST` al endpoint de la API de Flashly, incluyendo la `X-API-Key`.
4.  Maneje la respuesta y registre el resultado en la tabla `sync_logs` de ScrapSAE, marcando los productos como `synced` si la API devuelve éxito.

## 4. Conclusión y Recomendación

Ambos mecanismos tienen sus ventajas. Se recomienda **implementar ambos**:

*   La **API de Productos** debe ser la vía principal para la integración automatizada con ScrapSAE, ya que ofrece un acoplamiento más limpio y capacidad de tiempo real.
*   El **Layout de Importación** debe desarrollarse como una herramienta de administración para cargas masivas, migraciones iniciales o para ser usado por personal no técnico.

Este enfoque dual proporciona la máxima flexibilidad para la gestión de productos en Flashly.
