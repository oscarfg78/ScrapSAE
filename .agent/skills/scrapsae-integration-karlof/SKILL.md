---
name: scrapsae-integration-karlof
description: Implementación de la integración de ScrapSAE con Flashly para Karlof. Use este skill para desarrollar los cambios en ScrapSAE (Worker Service .NET, C#) necesarios para enviar productos extraídos a Flashly mediante API REST o exportación CSV, desacoplando la dependencia de Aspel SAE.
---

# ScrapSAE Integration - Karlof

Este skill guía la implementación de los cambios en **ScrapSAE** para la integración con **Flashly** según las reglas de negocio de Karlof.

## Contexto del Proyecto

ScrapSAE es un Worker Service de .NET diseñado para:
- **Extraer** productos de múltiples proveedores mediante web scraping (Playwright)
- **Procesar** datos con IA (OpenAI)
- **Sincronizar** con sistemas externos

**Stack Tecnológico:**
- **Backend:** .NET 8/9, C#
- **Scraping:** Playwright
- **IA:** OpenAI (GPT-4o-mini)
- **Base de Datos:** Supabase (PostgreSQL)
- **Integración Original:** ODBC con Aspel SAE

**Objetivo:** Modificar ScrapSAE para que envíe productos a Flashly en lugar de (o además de) Aspel SAE, mediante dos mecanismos: API REST y exportación CSV.

## Fases de Implementación

### Fase 1: Infraestructura - Crear Servicios de Integración

**Objetivo:** Implementar los servicios necesarios para comunicarse con Flashly.

#### 1.1. Crear FlashlySyncService

**Ubicación:** `src/ScrapSAE.Infrastructure/Services/FlashlySyncService.cs`

**Archivo de Referencia:** `templates/FlashlySyncService.cs`

**Pasos:**

1. Crear la interfaz `IFlashlySyncService` en `src/ScrapSAE.Core/Interfaces/`.
2. Implementar `FlashlySyncService` usando el template como base.
3. Adaptar la extracción de datos del `AiProcessedJson` según la estructura real del proyecto.
4. Registrar el servicio en el contenedor de dependencias (Program.cs o Startup.cs).

**Funcionalidad Clave:**

```csharp
Task<FlashlySyncResult> SyncProductsAsync(IEnumerable<StagingProduct> products);
```

Este método:
- Transforma productos de `staging_products` al formato de Flashly
- Construye el request JSON
- Envía POST a `/api/v1/products/sync` con `X-API-Key`
- Maneja la respuesta y errores
- Retorna resultado con contadores (creados, actualizados, errores)

#### 1.2. Crear CsvExportService

**Ubicación:** `src/ScrapSAE.Infrastructure/Services/CsvExportService.cs`

**Archivo de Referencia:** `templates/CsvExportService.cs`

**Pasos:**

1. Instalar el paquete NuGet `CsvHelper` si no está instalado.
2. Crear la interfaz `ICsvExportService` en `src/ScrapSAE.Core/Interfaces/`.
3. Implementar `CsvExportService` usando el template como base.
4. Registrar el servicio en el contenedor de dependencias.

**Funcionalidad Clave:**

```csharp
Task<string> ExportProductsToCsvAsync(
    IEnumerable<StagingProduct> products,
    string outputPath);
```

Este método:
- Transforma productos al formato CSV de Flashly
- Genera archivo CSV con headers según la especificación
- Retorna la ruta del archivo generado

### Fase 2: Configuración - Actualizar appsettings.json

**Objetivo:** Añadir la configuración necesaria para la integración con Flashly.

**Archivo de Referencia:** `scripts/appsettings_flashly.json`

**Pasos:**

1. Abrir `src/ScrapSAE.Worker/appsettings.json`.
2. Añadir las secciones de configuración del template.
3. Crear clases de configuración correspondientes:
   - `FlashlyApiConfig`
   - `SyncOptionsConfig`
   - `CsvExportConfig`
4. Registrar las configuraciones en Program.cs:

```csharp
services.Configure<FlashlyApiConfig>(
    configuration.GetSection("FlashlyApi"));
services.Configure<SyncOptionsConfig>(
    configuration.GetSection("SyncOptions"));
services.Configure<CsvExportConfig>(
    configuration.GetSection("CsvExport"));
```

**Configuración de HttpClient:**

```csharp
services.AddHttpClient<IFlashlySyncService, FlashlySyncService>()
    .ConfigureHttpClient((sp, client) =>
    {
        var config = sp.GetRequiredService<IOptions<FlashlyApiConfig>>().Value;
        client.BaseAddress = new Uri(config.BaseUrl);
        client.DefaultRequestHeaders.Add("X-API-Key", config.ApiKey);
    });
```

### Fase 3: Orquestador - Modificar ScrapingOrchestrator

**Objetivo:** Integrar los nuevos servicios en el flujo de trabajo principal.

**Ubicación:** `src/ScrapSAE.Worker/Services/ScrapingOrchestrator.cs`

**Pasos:**

1. Inyectar `IFlashlySyncService` y `ICsvExportService` en el constructor.
2. Modificar el método principal de orquestación para incluir la sincronización con Flashly.
3. Implementar lógica condicional basada en `SyncOptions.TargetSystem`.

**Flujo Modificado:**

```csharp
public async Task ExecuteScrapingAsync(Guid siteId)
{
    // 1. Scraping (sin cambios)
    var products = await _scrapingService.ScrapeProductsAsync(siteId);
    
    // 2. Procesamiento con IA (sin cambios)
    await _aiProcessorService.ProcessProductsAsync(products);
    
    // 3. Almacenar en staging (sin cambios)
    await _stagingService.SaveProductsAsync(products);
    
    // 4. Sincronización (NUEVO)
    var targetSystem = _syncOptions.Value.TargetSystem;
    
    if (targetSystem == "Flashly" || targetSystem == "Both")
    {
        var validatedProducts = await _stagingService
            .GetProductsByStatusAsync("validated");
            
        var syncResult = await _flashlySyncService
            .SyncProductsAsync(validatedProducts);
            
        // Registrar resultado en sync_logs
        await LogSyncResultAsync(syncResult);
        
        // Actualizar status de productos sincronizados
        if (syncResult.Success)
        {
            await _stagingService.UpdateProductsStatusAsync(
                validatedProducts.Select(p => p.Id),
                "synced");
        }
    }
    
    if (targetSystem == "SAE" || targetSystem == "Both")
    {
        // Mantener integración con SAE si es necesario
        await _saeIntegrationService.SyncToSaeAsync(products);
    }
    
    if (targetSystem == "CSV")
    {
        var outputPath = GenerateCsvPath();
        await _csvExportService.ExportProductsToCsvAsync(products, outputPath);
        _logger.LogInformation("CSV exportado a: {Path}", outputPath);
    }
}
```

### Fase 4: Base de Datos - Actualizar Esquema de Supabase

**Objetivo:** Añadir campos para rastrear la sincronización con Flashly.

**Cambios en `staging_products`:**

```sql
-- Añadir campos para rastrear sincronización con Flashly
ALTER TABLE staging_products
    ADD COLUMN IF NOT EXISTS flashly_sync_status VARCHAR(20) DEFAULT 'pending',
    ADD COLUMN IF NOT EXISTS flashly_product_id UUID,
    ADD COLUMN IF NOT EXISTS flashly_synced_at TIMESTAMPTZ;

-- Crear índice
CREATE INDEX IF NOT EXISTS idx_staging_flashly_sync_status 
    ON staging_products(flashly_sync_status);

-- Comentarios
COMMENT ON COLUMN staging_products.flashly_sync_status IS 
    'Estado de sincronización con Flashly: pending, synced, error';
COMMENT ON COLUMN staging_products.flashly_product_id IS 
    'ID del producto en Flashly (UUID)';
COMMENT ON COLUMN staging_products.flashly_synced_at IS 
    'Fecha y hora de última sincronización exitosa con Flashly';
```

**Actualizar Modelo C#:**

```csharp
public class StagingProduct
{
    // ... campos existentes ...
    
    public string FlashlySyncStatus { get; set; }
    public Guid? FlashlyProductId { get; set; }
    public DateTime? FlashlySyncedAt { get; set; }
}
```

### Fase 5: Dashboard (Opcional) - Añadir Controles

**Objetivo:** Permitir disparar sincronización manual desde el dashboard.

**Si existe un Dashboard React/Vue:**

1. Añadir botón "Sincronizar con Flashly" en la interfaz.
2. Crear endpoint en la API del Worker para disparar sincronización manual.
3. Añadir opción para generar y descargar CSV.

**Endpoint Sugerido:**

```csharp
[HttpPost("api/sync/flashly")]
public async Task<IActionResult> TriggerFlashlySyncAsync()
{
    var products = await _stagingService.GetProductsByStatusAsync("validated");
    var result = await _flashlySyncService.SyncProductsAsync(products);
    return Ok(result);
}

[HttpPost("api/export/csv")]
public async Task<IActionResult> ExportToCsvAsync()
{
    var products = await _stagingService.GetAllProductsAsync();
    var filePath = await _csvExportService.ExportProductsToCsvAsync(
        products, 
        GenerateCsvPath());
    return File(System.IO.File.ReadAllBytes(filePath), 
        "text/csv", 
        Path.GetFileName(filePath));
}
```

## Referencias Detalladas

### Mecanismo de Sincronización

Leer `references/mecanismo_sincronizacion.md` para entender:

- Especificación completa de la API de Flashly
- Estructura del layout CSV
- Formato de request y response

### Plan de Cambios Completo

Leer `references/plan_de_cambios.md` sección 3 para ver:

- Todos los cambios requeridos en ScrapSAE
- Cambios requeridos en Flashly
- Plan de implementación por fases

## Validación y Pruebas

### Pruebas Unitarias

1. **FlashlySyncService:**
   - Probar transformación de productos
   - Probar construcción del request JSON
   - Probar manejo de errores HTTP
   - Mockear HttpClient para pruebas

2. **CsvExportService:**
   - Probar generación de CSV
   - Verificar formato de columnas
   - Probar manejo de caracteres especiales

### Pruebas de Integración

1. **Sincronización con Flashly:**
   - Configurar API Key de prueba
   - Enviar productos de prueba
   - Verificar que llegan a Flashly con `status = 'pending_validation'`
   - Verificar actualización de `flashly_sync_status`

2. **Exportación CSV:**
   - Generar CSV con productos de prueba
   - Importar el CSV en Flashly manualmente
   - Verificar que los productos se crean correctamente

### Pruebas End-to-End

1. Ejecutar scraping completo de un proveedor
2. Verificar procesamiento con IA
3. Verificar sincronización automática con Flashly
4. Verificar logs en `sync_logs`
5. Verificar productos en Flashly con status correcto

## Manejo de Errores

### Estrategia de Reintentos

Implementar reintentos con backoff exponencial para errores temporales:

```csharp
var policy = Policy
    .Handle<HttpRequestException>()
    .WaitAndRetryAsync(
        _syncOptions.Value.RetryAttempts,
        retryAttempt => TimeSpan.FromSeconds(
            Math.Pow(2, retryAttempt) * _syncOptions.Value.RetryDelaySeconds));

await policy.ExecuteAsync(async () =>
{
    await _httpClient.PostAsync(endpoint, content);
});
```

### Logging

Registrar todos los eventos importantes:

```csharp
_logger.LogInformation("Iniciando sincronización con Flashly: {Count} productos", count);
_logger.LogError(ex, "Error sincronizando con Flashly");
_logger.LogWarning("Producto {Sku} no pudo sincronizarse: {Error}", sku, error);
```

## Notas Importantes

- **Compatibilidad:** Mantener la integración con SAE como opcional para no romper funcionalidad existente.
- **Configuración:** Usar `SyncOptions.TargetSystem` para controlar el destino: "Flashly", "SAE", "Both", "CSV".
- **Seguridad:** La `X-API-Key` debe almacenarse en variables de entorno o Azure Key Vault en producción.
- **Performance:** Procesar productos en lotes (batches) para evitar timeouts en grandes volúmenes.

## Entregables Finales

**IMPORTANTE:** Al completar TODAS las fases de implementación, generar:

### 1. Script de Migración Final para ScrapSAE (Supabase)

**Objetivo:** Generar el script SQL final para la base de datos de ScrapSAE.

**Proceso:**

1. Conectarse a la base de datos de Supabase de ScrapSAE.
2. Extraer los cambios realizados en `staging_products`.
3. Incluir índices y comentarios.
4. Guardar como `migration_scrapsae_karlof_FINAL.sql`.

**Formato:**

```sql
-- ============================================
-- SCRIPT FINAL DE MIGRACIÓN - ScrapSAE Karlof
-- Generado: [FECHA]
-- Descripción: Campos para sincronización con Flashly
-- ============================================

-- Paso 1: Añadir columnas a staging_products
-- ...

-- Paso 2: Crear índices
-- ...

-- Paso 3: Comentarios
-- ...
```

### 2. Documentación de Configuración

**Contenido:**

- Instrucciones para configurar `appsettings.json`
- Cómo obtener y configurar la `X-API-Key` de Flashly
- Variables de entorno requeridas
- Configuración de logging

### 3. Guía de Despliegue

**Contenido:**

- Pasos para desplegar el Worker Service actualizado
- Verificación de conectividad con Flashly
- Pruebas de humo post-despliegue
- Rollback en caso de problemas

### 4. Reporte de Pruebas

**Contenido:**

- Casos de prueba ejecutados
- Resultados (exitosos/fallidos)
- Evidencias (logs, capturas)
- Problemas encontrados y soluciones
