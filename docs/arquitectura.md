# 📐 Arquitectura del Proyecto ScrapSAE

> **Versión:** 1.0  
> **Fecha:** Enero 2026

---

## 1. Resumen Ejecutivo

**ScrapSAE** es una solución de software empresarial diseñada para ejecutarse localmente en el mismo entorno donde reside **Aspel SAE**. Funciona como un **integrador inteligente** que:

1. **Extrae** información de productos de múltiples proveedores mediante web scraping
2. **Procesa y enriquece** los datos usando Inteligencia Artificial (LLMs)
3. **Sincroniza** bidireccionalmente entre el inventario local (SAE) y tiendas en línea externas

---

## 2. Estructura de Carpetas de la Solución

```
ScrapSAE/
├── 📁 src/
│   ├── 📁 ScrapSAE.Worker/              # Worker Service Principal
│   │   ├── 📁 Services/
│   │   │   ├── ScrapingOrchestrator.cs  # Orquestador multi-sitio
│   │   │   ├── SchedulerService.cs      # Planificador de tareas (Cron)
│   │   │   ├── ScrapingService.cs       # Motor de web scraping
│   │   │   ├── AIProcessorService.cs    # Procesamiento con IA
│   │   │   ├── StagingService.cs        # Gestión BD staging
│   │   │   ├── SAEIntegrationService.cs # Integración ODBC con SAE
│   │   │   ├── WebhookService.cs        # Notificaciones externas
│   │   │   └── SupabaseService.cs       # Conexión con Supabase
│   │   ├── 📁 Models/
│   │   │   ├── SiteProfile.cs           # Configuración de sitios
│   │   │   ├── Product.cs               # Modelo de producto
│   │   │   ├── CategoryMapping.cs       # Mapeo de categorías
│   │   │   └── SyncLog.cs               # Logs de sincronización
│   │   ├── 📁 Configuration/
│   │   │   └── SiteConfigurations/      # JSON de proveedores
│   │   ├── Program.cs
│   │   ├── Worker.cs
│   │   └── appsettings.json
│   │
│   ├── 📁 ScrapSAE.Core/                # Librerías Core
│   │   ├── 📁 Entities/
│   │   ├── 📁 Interfaces/
│   │   ├── 📁 DTOs/
│   │   └── 📁 Enums/
│   │
│   ├── 📁 ScrapSAE.Infrastructure/      # Infraestructura
│   │   ├── 📁 Data/
│   │   │   ├── SqliteContext.cs         # EF Core para SQLite
│   │   │   ├── SAEConnection.cs         # ODBC para SAE
│   │   │   └── SupabaseClient.cs        # Cliente Supabase
│   │   ├── 📁 Scraping/
│   │   │   └── PlaywrightBrowser.cs     # Playwright wrapper
│   │   └── 📁 AI/
│   │       └── OpenAIClient.cs          # SDK OpenAI
│   │
│   └── 📁 ScrapSAE.Dashboard/           # Frontend React
│       ├── 📁 src/
│       │   ├── 📁 components/
│       │   ├── 📁 pages/
│       │   ├── 📁 services/
│       │   └── 📁 hooks/
│       ├── package.json
│       └── vite.config.ts
│
├── 📁 tests/
│   ├── 📁 ScrapSAE.Worker.Tests/
│   ├── 📁 ScrapSAE.Core.Tests/
│   └── 📁 ScrapSAE.Infrastructure.Tests/
│
├── 📁 docs/
│   ├── arquitectura.md
│   ├── configuracion-proveedores.md
│   └── guia-instalacion.md
│
├── 📁 database/
│   ├── migrations/
│   └── scrapsae_staging.db
│
├── ScrapSAE.sln
├── .editorconfig
├── .gitignore
└── README.md
```

---

## 3. Stack Tecnológico

### 3.1 Lenguajes de Programación

| Componente | Lenguaje | Versión |
|------------|----------|---------|
| Backend (Worker Service) | **C#** | 12.0 |
| Frontend (Dashboard) | **TypeScript** | 5.x |
| Scripts de BD | **SQL** | SQLite/PostgreSQL |

### 3.2 Frameworks Principales

| Framework | Uso | Versión |
|-----------|-----|---------|
| **.NET 8/9** | Worker Service (Servicio Windows) | 8.0 LTS / 9.0 |
| **ASP.NET Core** | API interna para Dashboard | 8.0 / 9.0 |
| **Entity Framework Core** | ORM para SQLite | 8.x |
| **React** | Frontend Dashboard | 18.x |
| **Vite** | Build tool Frontend | 5.x |

---

## 4. Ensamblados y Dependencias NuGet

### 4.1 ScrapSAE.Worker

```xml
<PackageReference Include="Microsoft.Extensions.Hosting.WindowsServices" Version="8.0.0" />
<PackageReference Include="Microsoft.Playwright" Version="1.41.0" />
<PackageReference Include="OpenAI" Version="2.0.0-beta" />
<PackageReference Include="Microsoft.SemanticKernel" Version="1.x" />
<PackageReference Include="Supabase" Version="0.16.x" />
<PackageReference Include="Cronos" Version="0.8.x" />
<PackageReference Include="Polly" Version="8.x" />
<PackageReference Include="Serilog.Extensions.Hosting" Version="8.x" />
<PackageReference Include="Serilog.Sinks.File" Version="5.x" />
```

### 4.2 ScrapSAE.Infrastructure

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="8.0.x" />
<PackageReference Include="Dapper" Version="2.1.x" />
<PackageReference Include="System.Data.Odbc" Version="8.0.x" />
<PackageReference Include="AngleSharp" Version="1.1.x" />
```

### 4.3 ScrapSAE.Dashboard (npm)

```json
{
  "dependencies": {
    "react": "^18.2.0",
    "react-dom": "^18.2.0",
    "react-router-dom": "^6.x",
    "@tanstack/react-query": "^5.x",
    "axios": "^1.6.x",
    "@supabase/supabase-js": "^2.x"
  }
}
```

---

## 5. SDK de Integración con Aspel SAE

### 5.1 Método de Conexión: ODBC

Aspel SAE utiliza **Firebird** o **SQL Server** como motor de base de datos. La integración se realiza mediante **ODBC**.

```csharp
public class SAEConnectionConfig
{
    public string Driver { get; set; }        // "Firebird/InterBase(r) driver" o "SQL Server"
    public string Server { get; set; }        // "localhost" o IP del servidor
    public string Database { get; set; }      // Ruta al archivo .fdb o nombre BD
    public string User { get; set; }          // Usuario SAE
    public string Password { get; set; }      // Contraseña encriptada
    public int Port { get; set; }             // 3050 (Firebird) o 1433 (SQL Server)
}
```

### 5.2 Tablas Principales de SAE

| Tabla | Descripción | Operación |
|-------|-------------|-----------|
| `INVE01` | Inventario de productos | Lectura/Escritura |
| `CLIN01` | Líneas de producto | Lectura |
| `PROV01` | Catálogo de proveedores | Lectura |
| `ALMA01` | Almacenes | Lectura |

### 5.3 Interface de Integración

```csharp
public interface ISAEIntegrationService
{
    Task<IEnumerable<ProductSAE>> GetAllProductsAsync();
    Task<ProductSAE?> GetProductBySkuAsync(string sku);
    Task<IEnumerable<ProductLine>> GetProductLinesAsync();
    Task<bool> UpdateProductAsync(ProductUpdate product);
    Task<bool> CreateProductAsync(ProductCreate product);
    Task<bool> UpdateStockAsync(string sku, decimal quantity);
    Task<bool> UpdatePriceAsync(string sku, decimal price);
    Task<bool> TestConnectionAsync();
}
```

---

## 6. Configuración de Supabase (BD Temporal y Reportes)

### 6.1 Esquema de Base de Datos

```sql
-- Configuración de proveedores
CREATE TABLE config_sites (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(100) NOT NULL,
    base_url VARCHAR(500) NOT NULL,
    selectors JSONB NOT NULL,
    cron_expression VARCHAR(50),
    requires_login BOOLEAN DEFAULT FALSE,
    credentials_encrypted TEXT,
    is_active BOOLEAN DEFAULT TRUE,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

-- Productos en proceso (Staging)
CREATE TABLE staging_products (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    site_id UUID REFERENCES config_sites(id),
    sku_source VARCHAR(100),
    sku_sae VARCHAR(50),
    raw_data TEXT,
    ai_processed_json JSONB,
    status VARCHAR(20) DEFAULT 'pending', -- pending, validated, synced, error
    validation_notes TEXT,
    attempts INT DEFAULT 0,
    last_seen_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

-- Mapeo de categorías
CREATE TABLE category_mapping (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    source_category VARCHAR(200),
    sae_line_code VARCHAR(10) NOT NULL,
    sae_line_name VARCHAR(100),
    auto_mapped BOOLEAN DEFAULT FALSE,
    confidence_score DECIMAL(3,2),
    created_at TIMESTAMPTZ DEFAULT NOW()
);

-- Logs de sincronización
CREATE TABLE sync_logs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    operation_type VARCHAR(50) NOT NULL, -- scrape, ai_process, sae_sync, webhook
    site_id UUID REFERENCES config_sites(id),
    product_id UUID REFERENCES staging_products(id),
    status VARCHAR(20) NOT NULL, -- success, error, retry
    message TEXT,
    details JSONB,
    duration_ms INT,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

-- Reportes de ejecución
CREATE TABLE execution_reports (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    execution_date DATE NOT NULL,
    site_id UUID REFERENCES config_sites(id),
    products_found INT DEFAULT 0,
    products_new INT DEFAULT 0,
    products_updated INT DEFAULT 0,
    products_discontinued INT DEFAULT 0,
    products_error INT DEFAULT 0,
    ai_tokens_used INT DEFAULT 0,
    total_duration_ms INT,
    summary JSONB,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

-- Índices
CREATE INDEX idx_staging_status ON staging_products(status);
CREATE INDEX idx_staging_sku ON staging_products(sku_source, sku_sae);
CREATE INDEX idx_logs_date ON sync_logs(created_at);
CREATE INDEX idx_reports_date ON execution_reports(execution_date);
```

### 6.2 Configuración en appsettings.json

```json
{
  "Supabase": {
    "Url": "https://[PROJECT_REF].supabase.co",
    "AnonKey": "eyJ...",
    "ServiceKey": "eyJ..."
  },
  "SAE": {
    "ConnectionString": "Driver={Firebird/InterBase(r) driver};Database=C:\\SAE\\DATA\\SAE.FDB;Uid=SYSDBA;Pwd=masterkey;",
    "Timeout": 30
  },
  "OpenAI": {
    "ApiKey": "sk-...",
    "Model": "gpt-4o-mini",
    "MaxTokens": 2000
  }
}
```

---

## 7. Patrones de Diseño

| Patrón | Aplicación |
|--------|------------|
| **Worker Service** | Proceso en segundo plano como servicio Windows |
| **Strategy** | Diferentes estrategias de scraping por proveedor |
| **Factory** | Creación de clientes de scraping según tipo de sitio |
| **Repository** | Abstracción de acceso a datos |
| **Circuit Breaker** | Gestión de fallos en APIs externas (Polly) |
| **Retry with Backoff** | Reintentos inteligentes en operaciones de red |

---

## 8. Seguridad

- ✅ **Sin puertos entrantes**: Toda comunicación es saliente (TLS 1.2+)
- ✅ **Credenciales encriptadas**: AES-256 para contraseñas de proveedores
- ✅ **Service Key de Supabase**: Solo en el Worker, nunca en frontend
- ✅ **Variables de entorno**: Configuración sensible fuera del código
- ✅ **Logs seguros**: Sin datos sensibles en archivos de log
