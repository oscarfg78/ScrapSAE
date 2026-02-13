# Guia de Despliegue ScrapSAE Worker con Flashly

## 1. Pre-requisitos
- Migracion aplicada en Supabase (`database/migration_scrapsae_karlof_FINAL.sql`).
- Endpoint de Flashly disponible: `POST /api/v1/products/sync`.
- API Key valida de Flashly.
- Variables de entorno cargadas en el entorno del Worker.

## 2. Pasos de despliegue
1. Publicar binarios del Worker (build release).
2. Actualizar configuracion (`FlashlyApi`, `SyncOptions`, `CsvExport`).
3. Reiniciar servicio del Worker.
4. Verificar logs de arranque y carga de configuracion.

## 3. Verificacion de conectividad
1. Ejecutar corrida controlada con un sitio de prueba activo.
2. Confirmar logs `flashly_sync start/success`.
3. Confirmar respuestas HTTP 2xx desde Flashly.
4. Verificar en `staging_products`:
   - `status = synced`
   - `flashly_sync_status = synced`
   - `flashly_synced_at` con timestamp reciente

## 4. Pruebas de humo post-despliegue
- Scraping de al menos un sitio.
- Procesamiento AI exitoso (`ai_processed_json` poblado).
- Sincronizacion Flashly o exportacion CSV segun `TargetSystem`.
- Registro en `sync_logs` con `operation_type = flashly_sync` o `csv_export`.

## 5. Rollback
1. Cambiar `SyncOptions.AutoSync=false` para detener salida a Flashly/CSV sin apagar scraping.
2. Si es necesario, revertir despliegue al build anterior del Worker.
3. Mantener datos en `staging_products` para reprocesar despues.
4. Documentar incidencia con hora, sitio y ultimo lote procesado.
