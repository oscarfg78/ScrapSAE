-- ============================================
-- ScrapSAE - Script de Base de Datos para Supabase
-- ============================================
-- Ejecutar este script en el SQL Editor de Supabase
-- https://supabase.com/dashboard/project/[PROJECT_ID]/sql
-- ============================================

-- ============================================
-- 1. TABLAS PRINCIPALES
-- ============================================

-- Tabla: config_sites (Configuración de proveedores)
CREATE TABLE IF NOT EXISTS config_sites (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(100) NOT NULL,
    base_url VARCHAR(500) NOT NULL,
    login_url VARCHAR(1000),
    selectors JSONB NOT NULL DEFAULT '{}',
    cron_expression VARCHAR(50),
    requires_login BOOLEAN DEFAULT FALSE,
    credentials_encrypted TEXT,
    is_active BOOLEAN DEFAULT TRUE,
    max_products_per_scrape INT DEFAULT 0,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

COMMENT ON TABLE config_sites IS 'Configuración de sitios proveedores para scraping';
COMMENT ON COLUMN config_sites.selectors IS 'JSON con selectores CSS/XPath para extracción';
COMMENT ON COLUMN config_sites.cron_expression IS 'Expresión cron para programación (ej: 0 3 * * 1)';
COMMENT ON COLUMN config_sites.credentials_encrypted IS 'Credenciales encriptadas AES-256 si requiere login';
COMMENT ON COLUMN config_sites.login_url IS 'URL de inicio de sesiÃ³n si el sitio requiere login';

-- ============================================

-- Tabla: staging_products (Productos en proceso)
CREATE TABLE IF NOT EXISTS staging_products (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    site_id UUID REFERENCES config_sites(id) ON DELETE CASCADE,
    sku_source VARCHAR(100),
    sku_sae VARCHAR(50),
    raw_data TEXT,
    ai_processed_json JSONB,
    status VARCHAR(20) DEFAULT 'pending' CHECK (status IN ('pending', 'validated', 'synced', 'error', 'discontinued')),
    validation_notes TEXT,
    attempts INT DEFAULT 0,
    last_seen_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

COMMENT ON TABLE staging_products IS 'Productos extraídos en proceso de validación';
COMMENT ON COLUMN staging_products.sku_source IS 'SKU del producto en el sitio proveedor';
COMMENT ON COLUMN staging_products.sku_sae IS 'SKU asignado/encontrado en SAE';
COMMENT ON COLUMN staging_products.raw_data IS 'HTML/texto crudo extraído del scraping';
COMMENT ON COLUMN staging_products.ai_processed_json IS 'Datos estructurados procesados por IA';
COMMENT ON COLUMN staging_products.status IS 'Estado: pending, validated, synced, error, discontinued';

-- ============================================

-- Tabla: category_mapping (Mapeo de categorías)
CREATE TABLE IF NOT EXISTS category_mapping (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    source_category VARCHAR(200),
    sae_line_code VARCHAR(10) NOT NULL,
    sae_line_name VARCHAR(100),
    auto_mapped BOOLEAN DEFAULT FALSE,
    confidence_score DECIMAL(3,2) CHECK (confidence_score >= 0 AND confidence_score <= 1),
    created_at TIMESTAMPTZ DEFAULT NOW()
);

COMMENT ON TABLE category_mapping IS 'Mapeo de categorías del proveedor a líneas de SAE';
COMMENT ON COLUMN category_mapping.source_category IS 'Nombre de categoría en el sitio proveedor';
COMMENT ON COLUMN category_mapping.sae_line_code IS 'Código de línea en SAE (CVE_LIN)';
COMMENT ON COLUMN category_mapping.auto_mapped IS 'TRUE si fue mapeado automáticamente por IA';
COMMENT ON COLUMN category_mapping.confidence_score IS 'Puntuación de confianza del mapeo (0-1)';

-- ============================================

-- Tabla: sync_logs (Logs de sincronización)
CREATE TABLE IF NOT EXISTS sync_logs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    operation_type VARCHAR(50) NOT NULL CHECK (operation_type IN ('scrape', 'ai_process', 'sae_sync', 'webhook')),
    site_id UUID REFERENCES config_sites(id) ON DELETE SET NULL,
    product_id UUID REFERENCES staging_products(id) ON DELETE SET NULL,
    status VARCHAR(20) NOT NULL CHECK (status IN ('success', 'error', 'retry')),
    message TEXT,
    details JSONB,
    duration_ms INT,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

COMMENT ON TABLE sync_logs IS 'Registro de operaciones de sincronización';
COMMENT ON COLUMN sync_logs.operation_type IS 'Tipo: scrape, ai_process, sae_sync, webhook';
COMMENT ON COLUMN sync_logs.status IS 'Resultado: success, error, retry';
COMMENT ON COLUMN sync_logs.duration_ms IS 'Duración de la operación en milisegundos';

-- ============================================

-- Tabla: execution_reports (Reportes de ejecución)
CREATE TABLE IF NOT EXISTS execution_reports (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    execution_date DATE NOT NULL,
    site_id UUID REFERENCES config_sites(id) ON DELETE SET NULL,
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

COMMENT ON TABLE execution_reports IS 'Reportes agregados de cada ejecución de scraping';

-- ============================================
-- 2. ÍNDICES PARA OPTIMIZACIÓN
-- ============================================

-- Índices para staging_products
CREATE INDEX IF NOT EXISTS idx_staging_status ON staging_products(status);
CREATE INDEX IF NOT EXISTS idx_staging_sku_source ON staging_products(sku_source);
CREATE INDEX IF NOT EXISTS idx_staging_sku_sae ON staging_products(sku_sae);
CREATE INDEX IF NOT EXISTS idx_staging_site_id ON staging_products(site_id);
CREATE INDEX IF NOT EXISTS idx_staging_created_at ON staging_products(created_at);

-- Índices para sync_logs
CREATE INDEX IF NOT EXISTS idx_logs_created_at ON sync_logs(created_at);
CREATE INDEX IF NOT EXISTS idx_logs_operation_type ON sync_logs(operation_type);
CREATE INDEX IF NOT EXISTS idx_logs_status ON sync_logs(status);
CREATE INDEX IF NOT EXISTS idx_logs_site_id ON sync_logs(site_id);

-- Índices para execution_reports
CREATE INDEX IF NOT EXISTS idx_reports_date ON execution_reports(execution_date);
CREATE INDEX IF NOT EXISTS idx_reports_site_id ON execution_reports(site_id);

-- Índice para category_mapping
CREATE INDEX IF NOT EXISTS idx_category_source ON category_mapping(source_category);
CREATE INDEX IF NOT EXISTS idx_category_sae_code ON category_mapping(sae_line_code);

-- ============================================
-- 3. ROW LEVEL SECURITY (RLS)
-- ============================================

-- Habilitar RLS en todas las tablas
ALTER TABLE config_sites ENABLE ROW LEVEL SECURITY;
ALTER TABLE staging_products ENABLE ROW LEVEL SECURITY;
ALTER TABLE category_mapping ENABLE ROW LEVEL SECURITY;
ALTER TABLE sync_logs ENABLE ROW LEVEL SECURITY;
ALTER TABLE execution_reports ENABLE ROW LEVEL SECURITY;

-- Políticas para service_role (acceso completo desde el Worker)
CREATE POLICY "Service role has full access to config_sites" 
    ON config_sites FOR ALL 
    USING (auth.role() = 'service_role');

CREATE POLICY "Service role has full access to staging_products" 
    ON staging_products FOR ALL 
    USING (auth.role() = 'service_role');

CREATE POLICY "Service role has full access to category_mapping" 
    ON category_mapping FOR ALL 
    USING (auth.role() = 'service_role');

CREATE POLICY "Service role has full access to sync_logs" 
    ON sync_logs FOR ALL 
    USING (auth.role() = 'service_role');

CREATE POLICY "Service role has full access to execution_reports" 
    ON execution_reports FOR ALL 
    USING (auth.role() = 'service_role');

-- ============================================
-- 4. FUNCIONES ÚTILES
-- ============================================

-- Función para actualizar updated_at automáticamente
CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Triggers para actualizar updated_at
CREATE TRIGGER update_config_sites_updated_at
    BEFORE UPDATE ON config_sites
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

CREATE TRIGGER update_staging_products_updated_at
    BEFORE UPDATE ON staging_products
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

-- ============================================
-- 5. DATOS INICIALES DE EJEMPLO
-- ============================================

-- Insertar sitio de ejemplo (descomentarear si lo necesitas)
/*
INSERT INTO config_sites (name, base_url, selectors, cron_expression, is_active)
VALUES (
    'Proveedor Ejemplo',
    'https://ejemplo.com/productos',
    '{
        "ProductListSelector": ".product-item",
        "TitleSelector": ".product-title",
        "PriceSelector": ".product-price",
        "SkuSelector": ".product-sku",
        "ImageSelector": ".product-image img",
        "NextPageSelector": ".pagination .next",
        "MaxPages": 5
    }',
    '0 3 * * 1',
    true
);
*/

-- ============================================
-- 6. VISTAS ÚTILES
-- ============================================

-- Vista: Resumen de productos por estado
CREATE OR REPLACE VIEW v_products_summary AS
SELECT 
    cs.name as site_name,
    sp.status,
    COUNT(*) as count,
    MAX(sp.updated_at) as last_updated
FROM staging_products sp
JOIN config_sites cs ON sp.site_id = cs.id
GROUP BY cs.name, sp.status;

-- Vista: Logs recientes con info del sitio
CREATE OR REPLACE VIEW v_recent_logs AS
SELECT 
    sl.id,
    sl.operation_type,
    sl.status,
    sl.message,
    sl.duration_ms,
    sl.created_at,
    cs.name as site_name
FROM sync_logs sl
LEFT JOIN config_sites cs ON sl.site_id = cs.id
ORDER BY sl.created_at DESC
LIMIT 100;

-- Vista: Reportes de ejecución con info del sitio
CREATE OR REPLACE VIEW v_execution_summary AS
SELECT 
    er.execution_date,
    cs.name as site_name,
    er.products_found,
    er.products_new,
    er.products_updated,
    er.products_error,
    er.ai_tokens_used,
    er.total_duration_ms / 1000.0 as duration_seconds
FROM execution_reports er
LEFT JOIN config_sites cs ON er.site_id = cs.id
ORDER BY er.execution_date DESC;

-- ============================================
-- FIN DEL SCRIPT
-- ============================================
-- Para ejecutar:
-- 1. Ve a https://supabase.com/dashboard
-- 2. Selecciona tu proyecto
-- 3. SQL Editor > New Query
-- 4. Pega y ejecuta este script
-- ============================================
