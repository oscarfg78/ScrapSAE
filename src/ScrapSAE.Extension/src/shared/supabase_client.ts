// ============================================================
// ScrapSAE Extension - Supabase Client
// Gestiona autenticación y acceso a datos del usuario
// ============================================================

import { createClient, SupabaseClient, Session, User } from '@supabase/supabase-js';
import type { UserProfile, UserLayout, ExecutionRecord, ColumnMapping, SiteSelectors } from './types';

// Configuración: se lee de chrome.storage.local o se usa el valor por defecto
const SUPABASE_URL = '__SUPABASE_URL__'; // Se reemplaza en build o se configura en runtime
const SUPABASE_ANON_KEY = '__SUPABASE_ANON_KEY__';

let supabaseInstance: SupabaseClient | null = null;

/**
 * Obtiene o crea la instancia de Supabase.
 * Lee la configuración de chrome.storage.local si está disponible.
 */
export async function getSupabase(): Promise<SupabaseClient> {
  if (supabaseInstance) return supabaseInstance;

  let url = SUPABASE_URL;
  let key = SUPABASE_ANON_KEY;

  try {
    const stored = await chrome.storage.local.get(['supabaseUrl', 'supabaseAnonKey']);
    if (stored.supabaseUrl) url = stored.supabaseUrl;
    if (stored.supabaseAnonKey) key = stored.supabaseAnonKey;
  } catch {
    // Fuera del contexto de la extensión (ej. tests)
  }

  supabaseInstance = createClient(url, key, {
    auth: {
      autoRefreshToken: true,
      persistSession: true,
      storage: {
        getItem: async (key: string) => {
          const result = await chrome.storage.local.get(key);
          return result[key] ?? null;
        },
        setItem: async (key: string, value: string) => {
          await chrome.storage.local.set({ [key]: value });
        },
        removeItem: async (key: string) => {
          await chrome.storage.local.remove(key);
        },
      },
    },
  });

  return supabaseInstance;
}

// ============================================================
// Autenticación
// ============================================================

export async function signInWithEmail(email: string, password: string): Promise<{ user: User | null; error: string | null }> {
  const supabase = await getSupabase();
  const { data, error } = await supabase.auth.signInWithPassword({ email, password });
  if (error) return { user: null, error: error.message };
  return { user: data.user, error: null };
}

export async function signUpWithEmail(email: string, password: string): Promise<{ user: User | null; error: string | null }> {
  const supabase = await getSupabase();
  const { data, error } = await supabase.auth.signUp({ email, password });
  if (error) return { user: null, error: error.message };
  return { user: data.user, error: null };
}

export async function signInWithGoogle(): Promise<void> {
  const supabase = await getSupabase();
  const redirectUrl = chrome.identity?.getRedirectURL?.() ?? 'https://scrapsae.com/auth/callback';
  const { data, error } = await supabase.auth.signInWithOAuth({
    provider: 'google',
    options: { redirectTo: redirectUrl },
  });
  if (error) throw new Error(error.message);
  if (data.url) {
    chrome.tabs.create({ url: data.url });
  }
}

export async function signOut(): Promise<void> {
  const supabase = await getSupabase();
  await supabase.auth.signOut();
  supabaseInstance = null;
}

export async function getSession(): Promise<Session | null> {
  const supabase = await getSupabase();
  const { data } = await supabase.auth.getSession();
  return data.session;
}

export async function getUser(): Promise<User | null> {
  const session = await getSession();
  return session?.user ?? null;
}

export async function getAccessToken(): Promise<string | null> {
  const session = await getSession();
  return session?.access_token ?? null;
}

// ============================================================
// Perfil de Usuario
// ============================================================

export async function getUserProfile(): Promise<UserProfile | null> {
  const supabase = await getSupabase();
  const user = await getUser();
  if (!user) return null;

  const { data, error } = await supabase
    .from('user_profiles')
    .select('*')
    .eq('id', user.id)
    .single();

  if (error) {
    console.error('[ScrapSAE] Error fetching user profile:', error.message);
    return null;
  }
  return data as UserProfile;
}

export async function ensureUserProfile(): Promise<UserProfile> {
  const supabase = await getSupabase();
  const user = await getUser();
  if (!user) throw new Error('No authenticated user');

  const existing = await getUserProfile();
  if (existing) return existing;

  const newProfile: Partial<UserProfile> = {
    id: user.id,
    email: user.email ?? '',
    subscriptionStatus: 'free',
    planType: 'free',
  };

  const { data, error } = await supabase
    .from('user_profiles')
    .upsert(newProfile)
    .select()
    .single();

  if (error) throw new Error(error.message);
  return data as UserProfile;
}

// ============================================================
// Layouts
// ============================================================

export async function getLayouts(): Promise<UserLayout[]> {
  const supabase = await getSupabase();
  const user = await getUser();
  if (!user) return [];

  const { data, error } = await supabase
    .from('user_layouts')
    .select('*')
    .eq('user_id', user.id)
    .order('created_at', { ascending: false });

  if (error) {
    console.error('[ScrapSAE] Error fetching layouts:', error.message);
    return [];
  }

  return (data ?? []).map(mapLayoutFromDb);
}

export async function saveLayout(layout: Partial<UserLayout>): Promise<UserLayout> {
  const supabase = await getSupabase();
  const user = await getUser();
  if (!user) throw new Error('No authenticated user');

  const record = {
    id: layout.id ?? undefined,
    user_id: user.id,
    name: layout.name ?? 'Nuevo Layout',
    selectors: layout.selectors ?? {},
    column_mapping: layout.columnMapping ?? [],
    is_default: layout.isDefault ?? false,
    updated_at: new Date().toISOString(),
  };

  const { data, error } = await supabase
    .from('user_layouts')
    .upsert(record)
    .select()
    .single();

  if (error) throw new Error(error.message);
  return mapLayoutFromDb(data);
}

export async function deleteLayout(layoutId: string): Promise<void> {
  const supabase = await getSupabase();
  const { error } = await supabase
    .from('user_layouts')
    .delete()
    .eq('id', layoutId);

  if (error) throw new Error(error.message);
}

function mapLayoutFromDb(row: Record<string, unknown>): UserLayout {
  return {
    id: row.id as string,
    userId: row.user_id as string,
    name: row.name as string,
    selectors: row.selectors as SiteSelectors,
    columnMapping: row.column_mapping as ColumnMapping[],
    isDefault: row.is_default as boolean,
    createdAt: row.created_at as string,
    updatedAt: row.updated_at as string,
  };
}

// ============================================================
// Historial de Ejecuciones
// ============================================================

export async function getExecutionHistory(limit = 50): Promise<ExecutionRecord[]> {
  const supabase = await getSupabase();
  const user = await getUser();
  if (!user) return [];

  const { data, error } = await supabase
    .from('extension_executions')
    .select('*')
    .eq('user_id', user.id)
    .order('created_at', { ascending: false })
    .limit(limit);

  if (error) {
    console.error('[ScrapSAE] Error fetching execution history:', error.message);
    return [];
  }

  return (data ?? []).map((row: Record<string, unknown>) => ({
    id: row.id as string,
    userId: row.user_id as string,
    layoutId: row.layout_id as string | undefined,
    layoutName: row.layout_name as string | undefined,
    sourceUrl: row.source_url as string,
    productsFound: row.products_found as number,
    productsExported: row.products_exported as number,
    status: row.status as ExecutionRecord['status'],
    errorMessage: row.error_message as string | undefined,
    durationMs: row.duration_ms as number | undefined,
    createdAt: row.created_at as string,
  }));
}

export async function saveExecution(record: Partial<ExecutionRecord>): Promise<void> {
  const supabase = await getSupabase();
  const user = await getUser();
  if (!user) return;

  const { error } = await supabase.from('extension_executions').upsert({
    id: record.id,
    user_id: user.id,
    layout_id: record.layoutId,
    layout_name: record.layoutName,
    source_url: record.sourceUrl,
    products_found: record.productsFound ?? 0,
    products_exported: record.productsExported ?? 0,
    status: record.status ?? 'running',
    error_message: record.errorMessage,
    duration_ms: record.durationMs,
    updated_at: new Date().toISOString(),
  });

  if (error) console.error('[ScrapSAE] Error saving execution:', error.message);
}
