-- Migration: add persistent queue tables for rescrape jobs

BEGIN;

CREATE TABLE IF NOT EXISTS public.rescrape_jobs
(
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    status VARCHAR(40) NOT NULL DEFAULT 'queued',
    requested_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    started_at TIMESTAMPTZ,
    completed_at TIMESTAMPTZ,
    total_items INT NOT NULL DEFAULT 0,
    processed_items INT NOT NULL DEFAULT 0,
    success_items INT NOT NULL DEFAULT 0,
    failed_items INT NOT NULL DEFAULT 0,
    skipped_items INT NOT NULL DEFAULT 0,
    options_json JSONB,
    summary_json JSONB,
    error_message TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS public.rescrape_job_items
(
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    job_id UUID NOT NULL REFERENCES public.rescrape_jobs(id) ON DELETE CASCADE,
    staging_product_id UUID NOT NULL REFERENCES public.staging_products(id) ON DELETE CASCADE,
    site_id UUID NOT NULL REFERENCES public.config_sites(id) ON DELETE CASCADE,
    source_url TEXT,
    status VARCHAR(40) NOT NULL DEFAULT 'pending',
    changed BOOLEAN NOT NULL DEFAULT FALSE,
    error_message TEXT,
    result_json JSONB,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS public.rescrape_job_logs
(
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    job_id UUID NOT NULL REFERENCES public.rescrape_jobs(id) ON DELETE CASCADE,
    item_id UUID REFERENCES public.rescrape_job_items(id) ON DELETE SET NULL,
    staging_product_id UUID REFERENCES public.staging_products(id) ON DELETE SET NULL,
    level VARCHAR(20) NOT NULL DEFAULT 'info',
    message TEXT NOT NULL,
    details_json JSONB,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_rescrape_jobs_status
    ON public.rescrape_jobs(status);

CREATE INDEX IF NOT EXISTS idx_rescrape_jobs_created_at
    ON public.rescrape_jobs(created_at DESC);

CREATE INDEX IF NOT EXISTS idx_rescrape_job_items_job_id
    ON public.rescrape_job_items(job_id);

CREATE INDEX IF NOT EXISTS idx_rescrape_job_items_status
    ON public.rescrape_job_items(status);

CREATE INDEX IF NOT EXISTS idx_rescrape_job_items_created_at
    ON public.rescrape_job_items(created_at DESC);

CREATE INDEX IF NOT EXISTS idx_rescrape_job_logs_job_id
    ON public.rescrape_job_logs(job_id);

CREATE INDEX IF NOT EXISTS idx_rescrape_job_logs_created_at
    ON public.rescrape_job_logs(created_at DESC);

COMMENT ON TABLE public.rescrape_jobs IS
    'Cola persistente de ejecuciones de rescrape.';

COMMENT ON TABLE public.rescrape_job_items IS
    'Detalle por producto dentro de cada job de rescrape.';

COMMENT ON TABLE public.rescrape_job_logs IS
    'Bitácora de ejecución por job/item de rescrape.';

COMMIT;
