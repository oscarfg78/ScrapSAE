-- Migration: add exclude_from_sae flag to staging_products
-- Run in Supabase SQL Editor

BEGIN;

ALTER TABLE IF EXISTS staging_products
    ADD COLUMN IF NOT EXISTS exclude_from_sae BOOLEAN NOT NULL DEFAULT FALSE;

COMMENT ON COLUMN staging_products.exclude_from_sae IS 'Exclude from SAE sync';

UPDATE staging_products
SET exclude_from_sae = FALSE
WHERE exclude_from_sae IS NULL;

COMMIT;
