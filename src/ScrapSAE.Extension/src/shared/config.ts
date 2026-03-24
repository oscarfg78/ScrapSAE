// ============================================================
// ScrapSAE Extension - Configuration
// ============================================================

export const CONFIG = {
  /** URL base del backend ScrapSAE.Api */
  API_BASE_URL: 'https://api.scrapsae.com',

  /** URL de la landing page */
  WEB_URL: 'https://scrapsae.com',

  /** Versión de la extensión */
  VERSION: '1.0.0',

  /** Tiempos de simulación humana (ms) */
  HUMAN_SIM: {
    MIN_DELAY: 1000,
    MAX_DELAY: 3500,
    SCROLL_STEP_MIN: 100,
    SCROLL_STEP_MAX: 400,
    SCROLL_PAUSE_MIN: 300,
    SCROLL_PAUSE_MAX: 1200,
    MOUSE_MOVE_STEPS: 25,
    MOUSE_MOVE_DURATION: 600,
  },

  /** Límites de scraping */
  SCRAPING: {
    BATCH_SIZE: 10,
    MAX_RETRIES: 3,
    TIMEOUT_MS: 60000,
  },

  /** Claves de almacenamiento */
  STORAGE_KEYS: {
    SESSION: 'scrapsae_session',
    SETTINGS: 'scrapsae_settings',
    LAST_LAYOUT: 'scrapsae_last_layout',
    DAILY_COUNT: 'scrapsae_daily_count',
    DAILY_DATE: 'scrapsae_daily_date',
  },
} as const;

/**
 * Lee la configuración del API URL desde storage (permite override).
 */
export async function getApiBaseUrl(): Promise<string> {
  try {
    const stored = await chrome.storage.local.get('apiBaseUrl');
    return stored.apiBaseUrl ?? CONFIG.API_BASE_URL;
  } catch {
    return CONFIG.API_BASE_URL;
  }
}
