# Configuracion ScrapSAE -> Flashly (Karlof)

## appsettings.json
Agregar/validar en `src/ScrapSAE.Worker/appsettings.json`:

```json
"FlashlyApi": {
  "BaseUrl": "https://api.flashly.com",
  "ApiKey": "[CLAVE_SECRETA_PROPORCIONADA_POR_FLASHLY]"
},
"SyncOptions": {
  "TargetSystem": "Flashly",
  "AutoSync": true,
  "BatchSize": 50,
  "RetryAttempts": 3,
  "RetryDelaySeconds": 5
},
"CsvExport": {
  "OutputDirectory": "C:\\ScrapSAE\\Exports",
  "FileNamePattern": "products_export_{0:yyyyMMdd_HHmmss}.csv"
}
```

## Significado de opciones
- `FlashlyApi.BaseUrl`: URL base del API de Flashly.
- `FlashlyApi.ApiKey`: valor enviado en header `X-API-Key`.
- `SyncOptions.TargetSystem`: `Flashly` o `CSV` (tambien soporta `Both` para mantener compatibilidad de flujo).
- `SyncOptions.AutoSync`: si `true`, al terminar scraping por sitio dispara sincronizacion/exportacion.
- `SyncOptions.BatchSize`: tamano de lote por request al endpoint `/api/v1/products/sync`.
- `SyncOptions.RetryAttempts` y `RetryDelaySeconds`: reintentos con backoff exponencial para errores temporales.
- `CsvExport.*`: carpeta y patron de nombre de archivo de exportacion.

## Variables de entorno recomendadas
En produccion no guardar secretos en archivo.

- `FlashlyApi__ApiKey`
- `FlashlyApi__BaseUrl`
- `Supabase__Url`
- `Supabase__ServiceKey`
- `OpenAI__ApiKey`

## API Key de Flashly
1. Solicitar la clave al administrador de Flashly.
2. Registrar el valor en secreto seguro (Azure Key Vault, Secret Manager, etc.).
3. Verificar que el worker envia header `X-API-Key` en cada request.

## Logging
Se usa Serilog en `Program.cs`:
- Consola
- Archivo rotativo diario: `logs/scrapsae_worker-.log`

Eventos esperados:
- Inicio y fin de scraping por sitio.
- Inicio y resultado de `flashly_sync`.
- Inicio y resultado de `csv_export`.
- Errores por SKU reportados por Flashly.
