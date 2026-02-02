# Análisis de Estructura de Productos en Flashly

## Esquema de Base de Datos

### Tabla Principal: `products`

Campos actuales según migraciones:

| Campo | Tipo | Descripción | Requerido |
|-------|------|-------------|-----------|
| `id` | UUID | Identificador único del producto | Sí (PK) |
| `tenant_id` | UUID | Identificador del tenant/tienda | Sí (FK) |
| `name` | TEXT | Nombre del producto | Sí |
| `sku` | TEXT | Código SKU del producto | No |
| `description` | TEXT | Descripción del producto | No |
| `price` | DECIMAL(10,2) | Precio del producto | Sí |
| `currency` | TEXT | Moneda (default: 'MXN') | No |
| `stock` | INTEGER | Cantidad en inventario | No (default: 0) |
| `in_stock` | BOOLEAN | Indicador de disponibilidad | No (default: false) |
| `is_active` | BOOLEAN | Producto activo/visible | No (default: true) |
| `price_visible` | BOOLEAN | Mostrar precio al público | No (default: true) |
| `allow_direct_sale` | BOOLEAN | Permitir venta directa | No |
| `supplier_id` | UUID | Proveedor del producto | No (FK) |
| `specifications` | JSONB | Especificaciones técnicas en formato JSON | No (default: {}) |
| `images` | TEXT[] | Array de URLs de imágenes | No (default: {}) |
| `image_url` | TEXT | URL de imagen (campo legacy) | No |
| `available` | BOOLEAN | Disponibilidad (campo legacy) | No |
| `category` | TEXT | Categoría (campo legacy) | No |
| `category_id` | UUID | ID de categoría (campo legacy) | No (FK) |

### Tablas Relacionadas

#### `product_categories` (Relación N-N)

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `product_id` | UUID | ID del producto (FK) |
| `category_id` | UUID | ID de la categoría (FK) |

**Propósito:** Permite que un producto pertenezca a múltiples categorías.

#### `product_attachments` (Relación 1-N)

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `id` | UUID | Identificador único |
| `product_id` | UUID | ID del producto (FK) |
| `file_url` | TEXT | URL del archivo adjunto |
| `file_name` | TEXT | Nombre del archivo |
| `file_type` | TEXT | Tipo de archivo (pdf, docx, etc.) |
| `file_size` | INTEGER | Tamaño en bytes |
| `created_at` | TIMESTAMP | Fecha de creación |

**Propósito:** Almacenar documentación técnica, fichas de producto, manuales, etc.

## Estructura de Datos en AdminService

### Formato de Entrada (API)

El `AdminService` espera recibir productos con la siguiente estructura:

```typescript
{
  name: string,                    // Requerido
  sku?: string,
  description?: string,
  price: number,                   // Requerido
  currency?: string,               // Default: 'MXN'
  stock?: number,                  // Default: 0
  available?: boolean,             // Mapea a is_active
  is_active?: boolean,
  price_visible?: boolean,         // Default: true
  allow_direct_sale?: boolean,
  supplier_id?: string,
  specifications?: object,         // JSONB
  images?: string[],               // Array de URLs
  image_url?: string,              // Legacy, se convierte a images[0]
  categories?: string[],           // Array de category_ids
  category_id?: string,            // Legacy, se convierte a categories[0]
  files?: Array<{                  // Archivos adjuntos
    url: string,
    name: string,
    type?: string,
    size?: number
  }>
}
```

### Formato de Salida (API)

Al consultar productos, el `AdminService.getProducts()` retorna:

```typescript
{
  products: Array<{
    id: string,
    tenant_id: string,
    name: string,
    sku: string,
    description: string,
    price: number,
    currency: string,
    stock: number,
    in_stock: boolean,
    is_active: boolean,
    price_visible: boolean,
    allow_direct_sale: boolean,
    supplier_id: string,
    specifications: object,
    images: string[],
    product_categories: Array<{
      categories: {
        id: string,
        name: string,
        slug: string
      }
    }>,
    product_attachments: Array<{
      id: string,
      file_url: string,
      file_name: string,
      file_type: string,
      file_size: number,
      created_at: string
    }>,
    suppliers: {
      id: string,
      name: string
    }
  }>,
  total: number,
  page: number,
  limit: number,
  totalPages: number
}
```

## Campos Clave para E-commerce

### Campos Esenciales

1. **Identificación:**
   - `name` - Nombre del producto
   - `sku` - Código único del producto
   - `id` - UUID generado automáticamente

2. **Información Comercial:**
   - `price` - Precio de venta
   - `currency` - Moneda
   - `stock` - Cantidad disponible
   - `in_stock` - Disponibilidad booleana
   - `is_active` - Producto activo en tienda

3. **Contenido:**
   - `description` - Descripción del producto
   - `images` - Array de imágenes del producto
   - `specifications` - Especificaciones técnicas (JSONB)

4. **Clasificación:**
   - `product_categories` - Categorías múltiples
   - `supplier_id` - Proveedor

5. **Documentación:**
   - `product_attachments` - Archivos técnicos, manuales, fichas

### Campo JSONB `specifications`

Este campo es flexible y puede contener cualquier estructura JSON. Ejemplos de uso:

```json
{
  "dimensiones": {
    "largo": "100mm",
    "ancho": "50mm",
    "alto": "30mm"
  },
  "peso": "250g",
  "material": "Acero inoxidable",
  "certificaciones": ["ISO 9001", "CE"],
  "caracteristicas_tecnicas": {
    "presion_max": "10 bar",
    "temperatura_max": "80°C",
    "conexion": "G1/4"
  }
}
```

## Observaciones

1. **Campos Legacy:** Existen campos legacy (`available`, `category_id`, `image_url`, `category`) que están siendo reemplazados por versiones mejoradas (`is_active`, `product_categories`, `images`).

2. **Flexibilidad JSONB:** El campo `specifications` permite almacenar información estructurada sin necesidad de modificar el esquema de base de datos.

3. **Múltiples Imágenes:** El sistema soporta múltiples imágenes por producto a través del array `images`.

4. **Múltiples Categorías:** Un producto puede pertenecer a varias categorías mediante la tabla `product_categories`.

5. **Archivos Adjuntos:** Los documentos técnicos se almacenan en `product_attachments`, separados de las imágenes principales.
