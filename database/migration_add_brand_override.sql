ALTER TABLE config_sites
    ADD COLUMN IF NOT EXISTS brand_override VARCHAR(255);

COMMENT ON COLUMN config_sites.brand_override IS 'Valor específico para reemplazar la marca (brand) extraída en este proveedor al exportar a Flashly';
