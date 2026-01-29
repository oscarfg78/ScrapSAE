-- Migration: Add missing columns to staging_products
-- Run this in the Supabase SQL Editor

BEGIN;

-- 1. Add source_url if missing
ALTER TABLE IF EXISTS staging_products 
    ADD COLUMN IF NOT EXISTS source_url TEXT;

-- 2. Add brand if missing (useful for filtering)
ALTER TABLE IF EXISTS staging_products 
    ADD COLUMN IF NOT EXISTS brand VARCHAR(100);

-- 3. Add category if missing
ALTER TABLE IF EXISTS staging_products 
    ADD COLUMN IF NOT EXISTS category VARCHAR(200);

-- Update comments
COMMENT ON COLUMN staging_products.source_url IS 'URL original del producto';
COMMENT ON COLUMN staging_products.brand IS 'Marca extraida del producto';
COMMENT ON COLUMN staging_products.category IS 'Categoria original del proveedor';

COMMIT;
