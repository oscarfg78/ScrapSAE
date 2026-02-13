-- ============================================
-- SCRIPT FINAL DE MIGRACION - ScrapSAE Karlof
-- Generado: 2026-02-13
-- Descripcion: Campos para sincronizacion con Flashly
-- ============================================

-- Paso 1: Anadir columnas a staging_products
ALTER TABLE public.staging_products
    ADD COLUMN IF NOT EXISTS flashly_sync_status VARCHAR(20) DEFAULT 'pending',
    ADD COLUMN IF NOT EXISTS flashly_product_id UUID,
    ADD COLUMN IF NOT EXISTS flashly_synced_at TIMESTAMPTZ;

-- Paso 2: Crear indice para estado de sincronizacion
CREATE INDEX IF NOT EXISTS idx_staging_flashly_sync_status
    ON public.staging_products(flashly_sync_status);

-- Paso 3: Comentarios de columnas
COMMENT ON COLUMN public.staging_products.flashly_sync_status IS
    'Estado de sincronizacion con Flashly: pending, synced, error';

COMMENT ON COLUMN public.staging_products.flashly_product_id IS
    'ID del producto en Flashly (UUID)';

COMMENT ON COLUMN public.staging_products.flashly_synced_at IS
    'Fecha y hora de ultima sincronizacion exitosa con Flashly';
