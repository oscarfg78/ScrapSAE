# Plan de Cambios para la Alineación de ScrapSAE y Flashly

## 1. Introducción

Este documento detalla los cambios específicos requeridos en los proyectos **Flashly** y **ScrapSAE** para implementar las nuevas reglas de negocio y el mecanismo de sincronización desacoplado. El objetivo es que ScrapSAE envíe la información de productos a Flashly, donde se gestionará su enriquecimiento y publicación.

---

## 2. Cambios Requeridos en Flashly

Flashly se convertirá en el centro de operaciones para la gestión y validación de productos. Los cambios se centran en la base de datos, el backend y el panel de administración.

### 2.1. Base de Datos (Supabase/PostgreSQL)

Se debe ejecutar un script de migración para actualizar la tabla `products` y añadir los nuevos campos que soportarán las reglas de negocio.

**Nuevos Tipos ENUM:**

```sql
-- Estatus del producto en el flujo de validación y publicación
CREATE TYPE product_status AS ENUM (
    'pending_validation', -- Recién importado, pendiente de revisión
    'validated',          -- Datos enriquecidos y correctos, listo para publicar
    'published',          -- Visible en la tienda
    'archived'            -- Descontinuado, oculto de la tienda
);

-- Métodos de entrega disponibles
CREATE TYPE delivery_method AS ENUM (
    'vendedor_paqueteria',  -- Envío por Karlof (costo desglosado)
    'proveedor_paqueteria', -- Envío por proveedor (costo incluido)
    'entrega_personal',     -- Entrega por Karlof (solo cotización)
    'envio_especializado'   -- Envío complejo (solo cotización)
);

-- Acción del botón principal en la página de producto
CREATE TYPE sale_action AS ENUM (
    'add_to_cart', -- Añadir al carrito
    'quote'        -- Solicitar cotización
);
```

**Modificaciones a la Tabla `products`:**

```sql
ALTER TABLE public.products
    -- Estatus del producto, con valor por defecto para nuevos ingresos
    ADD COLUMN IF NOT EXISTS status product_status DEFAULT 'pending_validation',

    -- Precio de compra obtenido del proveedor
    ADD COLUMN IF NOT EXISTS purchase_price DECIMAL(10, 2),

    -- Medio de entrega por defecto
    ADD COLUMN IF NOT EXISTS delivery_method delivery_method,

    -- Indicador para forzar la cotización
    ADD COLUMN IF NOT EXISTS quotable_only BOOLEAN DEFAULT FALSE,

    -- Acción del botón principal (se calculará a partir de quotable_only)
    ADD COLUMN IF NOT EXISTS sale_button_action sale_action,
    
    -- SKU del proveedor para facilitar la sincronización
    ADD COLUMN IF NOT EXISTS source_sku TEXT;

-- Añadir un índice para búsquedas por el SKU del proveedor
CREATE INDEX IF NOT EXISTS idx_products_source_sku ON public.products(source_sku);
```

### 2.2. Backend (API con Hono.js en Cloudflare Workers)

1.  **Crear API de Sincronización:**
    *   Implementar el endpoint `POST /api/v1/products/sync`.
    *   Asegurar el endpoint con una `X-API-Key` secreta.
    *   Desarrollar la lógica para procesar el arreglo de productos de forma asíncrona (ej. usando Queues de Cloudflare).
    *   La lógica debe buscar por `source_sku` y `supplier_id` para decidir si crear o actualizar un producto.
    *   Los productos nuevos se insertarán con `status = 'pending_validation'`.

2.  **Implementar Módulo de Importación:**
    *   Crear un endpoint `POST /api/v1/products/import` para manejar la subida de archivos CSV/Excel.
    *   Utilizar una librería para parsear el archivo y procesar las filas en un trabajo de fondo.
    *   Generar un reporte de resultados al finalizar.

3.  **Actualizar Lógica de Negocio:**
    *   Modificar los servicios existentes para que al actualizar un producto, se re-evalúen campos como `sale_button_action` basado en `quotable_only` y `delivery_method`.
    *   Crear la lógica para el cálculo del `sale_price` a partir del `purchase_price` y los porcentajes de ganancia (generales o por categoría).

### 2.3. Frontend (Panel de Administración con Next.js)

1.  **Módulo de Gestión de Productos:**
    *   Añadir filtros y vistas para gestionar productos según su nuevo `status`.
    *   En el formulario de edición de producto, añadir campos para gestionar `status`, `delivery_method`, `quotable_only`, y visualizar `purchase_price` y `sale_price`.

2.  **Flujo de Validación:**
    *   Crear una interfaz que permita al administrador revisar los productos con estatus `pending_validation`.
    *   En esta interfaz, el administrador podrá enriquecer los datos (asignar categorías, ajustar precios, definir el método de entrega) y finalmente cambiar el estatus a `validated` o `published`.

3.  **Módulo de Importación de Layout:**
    *   Diseñar una página donde el administrador pueda descargar la plantilla de Excel/CSV.
    *   Implementar el componente de carga de archivo que se comunique con el endpoint `POST /api/v1/products/import`.
    *   Mostrar el progreso de la importación y el reporte de resultados.

---

## 3. Cambios Requeridos en ScrapSAE

ScrapSAE debe ser adaptado para dejar de considerar a Aspel SAE como el destino principal y, en su lugar, preparar y enviar los datos a Flashly.

### 3.1. Lógica del Núcleo (Core)

1.  **Desacoplar de SAE:** El flujo principal ya no debe apuntar obligatoriamente al `SAEIntegrationService`. La sincronización con SAE se convierte en un paso opcional o posterior que se realizaría desde Flashly si fuera necesario.

2.  **Modificar el `status` en `staging_products`:** El estatus `synced` ahora significará 
que el producto fue enviado a Flashly, no a SAE.

### 3.2. Infraestructura (Infrastructure)

1.  **Crear `FlashlySyncService`:**
    *   Implementar una nueva clase `FlashlySyncService` que implemente una interfaz `IFlashlySyncService`.
    *   Este servicio contendrá la lógica para comunicarse con la API de Flashly.
    *   Tendrá un método `SyncProductsAsync(IEnumerable<Product> products)` que construirá el JSON y lo enviará al endpoint `POST /api/v1/products/sync`.
    *   La URL de la API de Flashly y la `X-API-Key` deben ser configurables en `appsettings.json`.

2.  **Crear `CsvExportService`:**
    *   Implementar una clase `CsvExportService` que genere el archivo de layout a partir de una lista de productos.
    *   Este servicio será utilizado para la opción de exportación manual.

### 3.3. Worker Service

1.  **Modificar el Orquestador (`ScrapingOrchestrator.cs`):**
    *   Después de que los productos son extraídos y procesados por la IA (obteniendo el `ai_processed_json`), en lugar de llamar al `SAEIntegrationService`, el orquestador deberá invocar al nuevo `FlashlySyncService`.
    *   La decisión de qué servicio llamar podría basarse en la configuración, permitiendo alternar entre enviar a Flashly o a un archivo CSV.

2.  **Actualizar `appsettings.json`:**
    *   Añadir una nueva sección para la configuración de la API de Flashly:

    ```json
    "FlashlyApi": {
      "BaseUrl": "https://api.flashly.com/api/v1",
      "ApiKey": "[CLAVE_SECRETA_PROPORCIONADA_POR_FLASHLY]"
    }
    ```

### 3.4. Dashboard de ScrapSAE (Opcional)

*   Se podría añadir una nueva opción en el dashboard de ScrapSAE para disparar manualmente la sincronización con Flashly o para generar y descargar el archivo de layout (CSV/Excel).
*   Actualizar los logs y reportes para reflejar que el destino de la sincronización es Flashly.

---

## 4. Plan de Implementación Sugerido

1.  **Fase 1 (Flashly - Backend):**
    *   Aplicar los cambios en la base de datos de Flashly (nuevos ENUMs y columnas).
    *   Desarrollar y desplegar la API de sincronización (`POST /api/v1/products/sync`) en Cloudflare Workers.

2.  **Fase 2 (ScrapSAE):**
    *   Implementar el `FlashlySyncService` en ScrapSAE.
    *   Modificar el worker para que utilice este nuevo servicio y envíe los datos a la API de Flashly.
    *   Realizar pruebas de integración completas, asegurando que los productos extraídos por ScrapSAE lleguen correctamente a Flashly con el estatus `pending_validation`.

3.  **Fase 3 (Flashly - Frontend):**
    *   Desarrollar la interfaz en el panel de administración de Flashly para la validación y enriquecimiento de productos.
    *   Implementar el módulo de importación de layout (CSV/Excel).

4.  **Fase 4 (Refinamiento):**
    *   Implementar la lógica de cálculo de precios de venta en Flashly.
    *   Ajustar la visualización de los productos en la tienda en línea para mostrar el botón de "Añadir al Carrito" o "Cotizar" según corresponda.
