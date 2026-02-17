-- Migration: add is_apartado flag to staging_products
ALTER TABLE IF EXISTS public.staging_products
    ADD COLUMN IF NOT EXISTS is_apartado BOOLEAN NOT NULL DEFAULT FALSE;

COMMENT ON COLUMN public.staging_products.is_apartado IS
    'Marca para apartar el registro y revisarlo despues';

CREATE INDEX IF NOT EXISTS idx_staging_is_apartado
    ON public.staging_products(is_apartado);
