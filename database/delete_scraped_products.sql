-- ==============================================================================
-- SCRIPT PARA ELIMINAR TODOS LOS PRODUCTOS EXTRAÍDOS (SCRAPING)
-- Proyecto: ScrapSAE
-- ==============================================================================
-- Este script elimina todos los registros de la tabla 'staging_products'
-- donde se almacenan los productos obtenidos mediante web scraping.
-- También elimina los registros de logs asociados para mantener la
-- integridad referencial y limpiar la base de datos de manera consistente.
-- ==============================================================================

BEGIN;

-- 1. Eliminar logs de sincronización asociados a los productos
-- (Aunque 'sync_logs' tiene 'ON DELETE SET NULL', los eliminamos para no dejar basura)
DELETE FROM sync_logs WHERE product_id IS NOT NULL;

-- 2. Eliminar todos los productos extraídos (en proceso, validados, etc.)
DELETE FROM staging_products;

COMMIT;

-- Nota: Si quieres eliminar solo los productos de un proveedor específico, 
-- usa la cláusula WHERE site_id = 'UUID_DEL_PROVEEDOR'.
