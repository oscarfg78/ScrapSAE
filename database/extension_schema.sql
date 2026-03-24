-- ============================================================
-- ScrapSAE Extension - Database Schema for Supabase
-- Ejecutar manualmente en el SQL Editor de Supabase.
-- Estas tablas complementan el esquema existente sin afectarlo.
-- ============================================================

-- ============================================================
-- 1. TABLA: user_profiles
-- Almacena el perfil del usuario, su plan y datos de Stripe.
-- ============================================================

CREATE TABLE IF NOT EXISTS public.user_profiles (
    id UUID PRIMARY KEY REFERENCES auth.users(id) ON DELETE CASCADE,
    email TEXT NOT NULL DEFAULT '',
    full_name TEXT,
    avatar_url TEXT,
    stripe_customer_id TEXT UNIQUE,
    subscription_status TEXT NOT NULL DEFAULT 'free'
        CHECK (subscription_status IN ('free', 'pro', 'enterprise')),
    plan_type TEXT NOT NULL DEFAULT 'free'
        CHECK (plan_type IN ('free', 'pro', 'enterprise')),
    stripe_subscription_id TEXT,
    plan_expires_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

COMMENT ON TABLE public.user_profiles IS 'Perfiles de usuario para la extensión de Chrome ScrapSAE';
COMMENT ON COLUMN public.user_profiles.subscription_status IS 'Estado actual de la suscripción: free, pro o enterprise';
COMMENT ON COLUMN public.user_profiles.stripe_customer_id IS 'ID del cliente en Stripe para gestión de pagos';

-- Índices
CREATE INDEX IF NOT EXISTS idx_user_profiles_email ON public.user_profiles(email);
CREATE INDEX IF NOT EXISTS idx_user_profiles_stripe_customer ON public.user_profiles(stripe_customer_id);
CREATE INDEX IF NOT EXISTS idx_user_profiles_plan ON public.user_profiles(plan_type);

-- RLS (Row Level Security)
ALTER TABLE public.user_profiles ENABLE ROW LEVEL SECURITY;

-- Política: los usuarios solo pueden ver y editar su propio perfil
CREATE POLICY "Users can view own profile"
    ON public.user_profiles FOR SELECT
    USING (auth.uid() = id);

CREATE POLICY "Users can update own profile"
    ON public.user_profiles FOR UPDATE
    USING (auth.uid() = id)
    WITH CHECK (auth.uid() = id);

CREATE POLICY "Users can insert own profile"
    ON public.user_profiles FOR INSERT
    WITH CHECK (auth.uid() = id);

-- Política para el service role (webhooks de Stripe)
CREATE POLICY "Service role full access to profiles"
    ON public.user_profiles FOR ALL
    USING (auth.role() = 'service_role');

-- ============================================================
-- 2. TABLA: user_layouts
-- Configuraciones de scraping (selectores + mapeo de columnas).
-- ============================================================

CREATE TABLE IF NOT EXISTS public.user_layouts (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    name TEXT NOT NULL DEFAULT 'Nuevo Layout',
    selectors JSONB NOT NULL DEFAULT '{}'::jsonb,
    column_mapping JSONB NOT NULL DEFAULT '[]'::jsonb,
    is_default BOOLEAN NOT NULL DEFAULT FALSE,
    target_url_pattern TEXT,
    notes TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

COMMENT ON TABLE public.user_layouts IS 'Layouts de scraping configurados por el usuario';
COMMENT ON COLUMN public.user_layouts.selectors IS 'Objeto JSON con los selectores CSS (SiteSelectors)';
COMMENT ON COLUMN public.user_layouts.column_mapping IS 'Array JSON con el mapeo de columnas para el Excel';
COMMENT ON COLUMN public.user_layouts.target_url_pattern IS 'Patrón de URL para auto-selección del layout';

-- Índices
CREATE INDEX IF NOT EXISTS idx_user_layouts_user ON public.user_layouts(user_id);
CREATE INDEX IF NOT EXISTS idx_user_layouts_default ON public.user_layouts(user_id, is_default) WHERE is_default = TRUE;

-- RLS
ALTER TABLE public.user_layouts ENABLE ROW LEVEL SECURITY;

CREATE POLICY "Users can view own layouts"
    ON public.user_layouts FOR SELECT
    USING (auth.uid() = user_id);

CREATE POLICY "Users can insert own layouts"
    ON public.user_layouts FOR INSERT
    WITH CHECK (auth.uid() = user_id);

CREATE POLICY "Users can update own layouts"
    ON public.user_layouts FOR UPDATE
    USING (auth.uid() = user_id)
    WITH CHECK (auth.uid() = user_id);

CREATE POLICY "Users can delete own layouts"
    ON public.user_layouts FOR DELETE
    USING (auth.uid() = user_id);

-- ============================================================
-- 3. TABLA: extension_executions
-- Historial de ejecuciones de scraping desde la extensión.
-- ============================================================

CREATE TABLE IF NOT EXISTS public.extension_executions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    layout_id UUID REFERENCES public.user_layouts(id) ON DELETE SET NULL,
    layout_name TEXT,
    source_url TEXT NOT NULL DEFAULT '',
    products_found INTEGER NOT NULL DEFAULT 0,
    products_exported INTEGER NOT NULL DEFAULT 0,
    status TEXT NOT NULL DEFAULT 'running'
        CHECK (status IN ('running', 'completed', 'failed', 'cancelled')),
    error_message TEXT,
    duration_ms INTEGER,
    metadata JSONB DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

COMMENT ON TABLE public.extension_executions IS 'Historial de ejecuciones de scraping desde la extensión';

-- Índices
CREATE INDEX IF NOT EXISTS idx_ext_executions_user ON public.extension_executions(user_id);
CREATE INDEX IF NOT EXISTS idx_ext_executions_status ON public.extension_executions(status);
CREATE INDEX IF NOT EXISTS idx_ext_executions_created ON public.extension_executions(created_at DESC);

-- RLS
ALTER TABLE public.extension_executions ENABLE ROW LEVEL SECURITY;

CREATE POLICY "Users can view own executions"
    ON public.extension_executions FOR SELECT
    USING (auth.uid() = user_id);

CREATE POLICY "Users can insert own executions"
    ON public.extension_executions FOR INSERT
    WITH CHECK (auth.uid() = user_id);

CREATE POLICY "Users can update own executions"
    ON public.extension_executions FOR UPDATE
    USING (auth.uid() = user_id)
    WITH CHECK (auth.uid() = user_id);

-- ============================================================
-- 4. FUNCIÓN: Crear perfil automáticamente al registrar usuario
-- ============================================================

CREATE OR REPLACE FUNCTION public.handle_new_user()
RETURNS TRIGGER
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS $$
BEGIN
    INSERT INTO public.user_profiles (id, email, full_name, avatar_url)
    VALUES (
        NEW.id,
        COALESCE(NEW.email, ''),
        COALESCE(NEW.raw_user_meta_data->>'full_name', ''),
        COALESCE(NEW.raw_user_meta_data->>'avatar_url', '')
    )
    ON CONFLICT (id) DO NOTHING;
    RETURN NEW;
END;
$$;

-- Trigger: ejecutar al crear un nuevo usuario en auth.users
DROP TRIGGER IF EXISTS on_auth_user_created ON auth.users;
CREATE TRIGGER on_auth_user_created
    AFTER INSERT ON auth.users
    FOR EACH ROW
    EXECUTE FUNCTION public.handle_new_user();

-- ============================================================
-- 5. FUNCIÓN: Actualizar updated_at automáticamente
-- ============================================================

CREATE OR REPLACE FUNCTION public.update_updated_at_column()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$;

-- Triggers de updated_at
DROP TRIGGER IF EXISTS update_user_profiles_updated_at ON public.user_profiles;
CREATE TRIGGER update_user_profiles_updated_at
    BEFORE UPDATE ON public.user_profiles
    FOR EACH ROW
    EXECUTE FUNCTION public.update_updated_at_column();

DROP TRIGGER IF EXISTS update_user_layouts_updated_at ON public.user_layouts;
CREATE TRIGGER update_user_layouts_updated_at
    BEFORE UPDATE ON public.user_layouts
    FOR EACH ROW
    EXECUTE FUNCTION public.update_updated_at_column();

DROP TRIGGER IF EXISTS update_ext_executions_updated_at ON public.extension_executions;
CREATE TRIGGER update_ext_executions_updated_at
    BEFORE UPDATE ON public.extension_executions
    FOR EACH ROW
    EXECUTE FUNCTION public.update_updated_at_column();

-- ============================================================
-- 6. FUNCIÓN: Verificar límite de layouts según plan
-- ============================================================

CREATE OR REPLACE FUNCTION public.check_layout_limit()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
DECLARE
    user_plan TEXT;
    layout_count INTEGER;
    max_layouts INTEGER;
BEGIN
    -- Obtener el plan del usuario
    SELECT plan_type INTO user_plan
    FROM public.user_profiles
    WHERE id = NEW.user_id;

    -- Definir límites por plan
    max_layouts := CASE user_plan
        WHEN 'free' THEN 1
        WHEN 'pro' THEN 20
        WHEN 'enterprise' THEN -1  -- ilimitado
        ELSE 1
    END;

    -- Si es ilimitado, permitir
    IF max_layouts = -1 THEN
        RETURN NEW;
    END IF;

    -- Contar layouts existentes
    SELECT COUNT(*) INTO layout_count
    FROM public.user_layouts
    WHERE user_id = NEW.user_id;

    -- Verificar límite (solo en INSERT, no en UPDATE)
    IF TG_OP = 'INSERT' AND layout_count >= max_layouts THEN
        RAISE EXCEPTION 'Límite de layouts alcanzado para el plan %. Máximo: %', user_plan, max_layouts;
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS check_layout_limit_trigger ON public.user_layouts;
CREATE TRIGGER check_layout_limit_trigger
    BEFORE INSERT ON public.user_layouts
    FOR EACH ROW
    EXECUTE FUNCTION public.check_layout_limit();

-- ============================================================
-- 7. FUNCIÓN: Asegurar un solo layout default por usuario
-- ============================================================

CREATE OR REPLACE FUNCTION public.ensure_single_default_layout()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW.is_default = TRUE THEN
        UPDATE public.user_layouts
        SET is_default = FALSE
        WHERE user_id = NEW.user_id
          AND id != NEW.id
          AND is_default = TRUE;
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS ensure_single_default_layout_trigger ON public.user_layouts;
CREATE TRIGGER ensure_single_default_layout_trigger
    BEFORE INSERT OR UPDATE ON public.user_layouts
    FOR EACH ROW
    EXECUTE FUNCTION public.ensure_single_default_layout();
