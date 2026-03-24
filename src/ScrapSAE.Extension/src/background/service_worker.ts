// ============================================================
// ScrapSAE Extension - Background Service Worker
// Orquesta la comunicación entre Content Script, Popup,
// SidePanel, el API backend y la generación de Excel.
// ============================================================

import * as XLSX from 'xlsx';
import type {
  ExtensionMessage,
  ScrapedProduct,
  ProcessedProduct,
  ColumnMapping,
  StartScrapingPayload,
  ScrapingProgressPayload,
  UserProfile,
  ExecutionRecord,
  ApiResponse,
  PLAN_LIMITS,
} from '../shared/types';
import { getApiBaseUrl, CONFIG } from '../shared/config';
import {
  getSession,
  getAccessToken,
  getUserProfile,
  saveExecution,
  ensureUserProfile,
} from '../shared/supabase_client';

// ============================================================
// Estado Global del Service Worker
// ============================================================

interface ScrapingState {
  isRunning: boolean;
  tabId: number | null;
  executionId: string | null;
  startTime: number;
  products: ScrapedProduct[];
  progress: ScrapingProgressPayload | null;
}

const state: ScrapingState = {
  isRunning: false,
  tabId: null,
  executionId: null,
  startTime: 0,
  products: [],
  progress: null,
};

// ============================================================
// API Client
// ============================================================

async function apiCall<T>(
  endpoint: string,
  method: 'GET' | 'POST' = 'GET',
  body?: unknown
): Promise<ApiResponse<T>> {
  const baseUrl = await getApiBaseUrl();
  const token = await getAccessToken();

  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
  };
  if (token) {
    headers['Authorization'] = `Bearer ${token}`;
  }

  try {
    const response = await fetch(`${baseUrl}${endpoint}`, {
      method,
      headers,
      body: body ? JSON.stringify(body) : undefined,
    });

    if (!response.ok) {
      const errorText = await response.text();
      return { success: false, errorMessage: `HTTP ${response.status}: ${errorText}` };
    }

    const data = await response.json();
    return { success: true, data: data as T };
  } catch (error) {
    return {
      success: false,
      errorMessage: error instanceof Error ? error.message : String(error),
    };
  }
}

/**
 * Envía productos crudos al API para procesamiento con IA.
 */
async function processWithAI(products: ScrapedProduct[]): Promise<ProcessedProduct[]> {
  const result = await apiCall<ProcessedProduct[]>('/api/extension/process', 'POST', {
    products,
  });

  if (!result.success || !result.data) {
    console.warn('[ScrapSAE SW] AI processing failed, using raw data:', result.errorMessage);
    // Fallback: convertir ScrapedProduct a ProcessedProduct sin IA
    return products.map(rawToProcessed);
  }

  return result.data;
}

/**
 * Convierte un producto crudo a procesado (fallback sin IA).
 */
function rawToProcessed(raw: ScrapedProduct): ProcessedProduct {
  return {
    sku: raw.skuSource,
    name: raw.title ?? 'Sin nombre',
    brand: raw.brand,
    description: raw.description ?? '',
    features: [],
    specifications: raw.attributes ?? {},
    suggestedCategory: raw.category,
    categories: raw.category ? [raw.category] : [],
    price: raw.price,
    images: raw.imageUrls ?? [],
    attachments: raw.attachments ?? [],
  };
}

// ============================================================
// Generación de Excel con SheetJS
// ============================================================

/**
 * Genera un archivo Excel (.xlsx) a partir de los productos procesados
 * y el mapeo de columnas del layout del usuario.
 */
function generateExcel(
  products: ProcessedProduct[],
  columnMapping: ColumnMapping[],
  fileName: string
): Uint8Array {
  // Filtrar solo columnas habilitadas
  const activeColumns = columnMapping.filter((col) => col.enabled);

  // Si no hay mapeo, usar columnas por defecto
  const columns: ColumnMapping[] =
    activeColumns.length > 0
      ? activeColumns
      : getDefaultColumnMapping();

  // Crear los datos para la hoja
  const rows = products.map((product) => {
    const row: Record<string, unknown> = {};
    columns.forEach((col) => {
      const value = getProductField(product, col.field);
      row[col.header] = value;
    });
    return row;
  });

  // Crear libro y hoja
  const worksheet = XLSX.utils.json_to_sheet(rows);

  // Aplicar anchos de columna
  worksheet['!cols'] = columns.map((col) => ({
    wch: col.width ?? 20,
  }));

  const workbook = XLSX.utils.book_new();
  XLSX.utils.book_append_sheet(workbook, worksheet, 'Productos');

  // Agregar hoja de metadata
  const metaData = [
    { Campo: 'Fecha de exportación', Valor: new Date().toLocaleString('es-MX') },
    { Campo: 'Total de productos', Valor: products.length },
    { Campo: 'URL de origen', Valor: '(ver columna SourceUrl)' },
    { Campo: 'Generado por', Valor: 'ScrapSAE Extension v' + CONFIG.VERSION },
  ];
  const metaSheet = XLSX.utils.json_to_sheet(metaData);
  XLSX.utils.book_append_sheet(workbook, metaSheet, 'Info');

  // Generar el archivo como Uint8Array
  return XLSX.write(workbook, { type: 'array', bookType: 'xlsx' }) as Uint8Array;
}

/**
 * Obtiene el valor de un campo del producto (soporta campos anidados).
 */
function getProductField(product: ProcessedProduct, field: string): unknown {
  if (field in product) {
    const value = (product as Record<string, unknown>)[field];

    // Convertir arrays y objetos a string legible
    if (Array.isArray(value)) return value.join(', ');
    if (typeof value === 'object' && value !== null) {
      return Object.entries(value as Record<string, string>)
        .map(([k, v]) => `${k}: ${v}`)
        .join('; ');
    }
    return value;
  }

  // Campo personalizado en specifications
  if (product.specifications && field in product.specifications) {
    return product.specifications[field];
  }

  return '';
}

/**
 * Mapeo de columnas por defecto cuando el usuario no ha configurado uno.
 */
function getDefaultColumnMapping(): ColumnMapping[] {
  return [
    { field: 'sku', header: 'SKU', width: 15, enabled: true },
    { field: 'name', header: 'Nombre', width: 40, enabled: true },
    { field: 'brand', header: 'Marca', width: 15, enabled: true },
    { field: 'model', header: 'Modelo', width: 15, enabled: true },
    { field: 'description', header: 'Descripción', width: 50, enabled: true },
    { field: 'price', header: 'Precio', width: 12, enabled: true },
    { field: 'currency', header: 'Moneda', width: 8, enabled: true },
    { field: 'stock', header: 'Stock', width: 8, enabled: true },
    { field: 'suggestedCategory', header: 'Categoría', width: 20, enabled: true },
    { field: 'features', header: 'Características', width: 40, enabled: true },
    { field: 'specifications', header: 'Especificaciones', width: 50, enabled: true },
    { field: 'images', header: 'Imágenes', width: 60, enabled: true },
  ];
}

/**
 * Descarga el archivo Excel generado usando chrome.downloads.
 */
async function downloadExcel(data: Uint8Array, fileName: string): Promise<void> {
  const blob = new Blob([data], {
    type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  });

  // Convertir Blob a data URL
  const reader = new FileReader();
  const dataUrl = await new Promise<string>((resolve, reject) => {
    reader.onload = () => resolve(reader.result as string);
    reader.onerror = reject;
    reader.readAsDataURL(blob);
  });

  chrome.downloads.download({
    url: dataUrl,
    filename: `ScrapSAE/${fileName}.xlsx`,
    saveAs: true,
  });
}

// ============================================================
// Control de Límites del Plan
// ============================================================

async function checkPlanLimits(): Promise<{ allowed: boolean; reason?: string }> {
  const profile = await getUserProfile();
  if (!profile) {
    return { allowed: true }; // Sin perfil = modo free por defecto
  }

  const plan = profile.planType ?? 'free';
  const limits = {
    free: { maxExtractionsPerDay: 50 },
    pro: { maxExtractionsPerDay: 1000 },
    enterprise: { maxExtractionsPerDay: -1 },
  };

  const planLimits = limits[plan] ?? limits.free;

  if (planLimits.maxExtractionsPerDay === -1) {
    return { allowed: true };
  }

  // Verificar conteo diario
  const today = new Date().toISOString().split('T')[0];
  const stored = await chrome.storage.local.get([
    CONFIG.STORAGE_KEYS.DAILY_COUNT,
    CONFIG.STORAGE_KEYS.DAILY_DATE,
  ]);

  let dailyCount = 0;
  if (stored[CONFIG.STORAGE_KEYS.DAILY_DATE] === today) {
    dailyCount = stored[CONFIG.STORAGE_KEYS.DAILY_COUNT] ?? 0;
  }

  if (dailyCount >= planLimits.maxExtractionsPerDay) {
    return {
      allowed: false,
      reason: `Has alcanzado el límite de ${planLimits.maxExtractionsPerDay} extracciones diarias. Actualiza tu plan para continuar.`,
    };
  }

  return { allowed: true };
}

async function incrementDailyCount(): Promise<void> {
  const today = new Date().toISOString().split('T')[0];
  const stored = await chrome.storage.local.get([
    CONFIG.STORAGE_KEYS.DAILY_COUNT,
    CONFIG.STORAGE_KEYS.DAILY_DATE,
  ]);

  let count = 0;
  if (stored[CONFIG.STORAGE_KEYS.DAILY_DATE] === today) {
    count = stored[CONFIG.STORAGE_KEYS.DAILY_COUNT] ?? 0;
  }

  await chrome.storage.local.set({
    [CONFIG.STORAGE_KEYS.DAILY_COUNT]: count + 1,
    [CONFIG.STORAGE_KEYS.DAILY_DATE]: today,
  });
}

// ============================================================
// Flujo Principal de Scraping
// ============================================================

async function startScraping(
  tabId: number,
  payload: StartScrapingPayload
): Promise<void> {
  if (state.isRunning) {
    throw new Error('Ya hay un scraping en ejecución.');
  }

  // Verificar límites del plan
  const limits = await checkPlanLimits();
  if (!limits.allowed) {
    throw new Error(limits.reason ?? 'Límite de plan alcanzado.');
  }

  // Inicializar estado
  state.isRunning = true;
  state.tabId = tabId;
  state.executionId = crypto.randomUUID();
  state.startTime = Date.now();
  state.products = [];
  state.progress = null;

  // Registrar inicio de ejecución
  await saveExecution({
    id: state.executionId,
    sourceUrl: (await chrome.tabs.get(tabId)).url ?? '',
    layoutName: payload.layoutId,
    productsFound: 0,
    productsExported: 0,
    status: 'running',
  });

  try {
    // Inyectar el content script de extracción
    await chrome.scripting.executeScript({
      target: { tabId },
      files: ['src/content/extractor.js'],
    });

    // Enviar comando de extracción al content script
    chrome.tabs.sendMessage(tabId, {
      action: 'EXTRACT_PAGE',
      payload,
    } as ExtensionMessage);
  } catch (error) {
    state.isRunning = false;
    const errorMsg = error instanceof Error ? error.message : String(error);

    await saveExecution({
      id: state.executionId,
      status: 'failed',
      errorMessage: errorMsg,
      durationMs: Date.now() - state.startTime,
    });

    throw error;
  }
}

async function stopScraping(): Promise<void> {
  if (!state.isRunning || !state.tabId) return;

  chrome.tabs.sendMessage(state.tabId, {
    action: 'STOP_SCRAPING',
  } as ExtensionMessage);

  state.isRunning = false;

  await saveExecution({
    id: state.executionId!,
    status: 'cancelled',
    durationMs: Date.now() - state.startTime,
  });
}

/**
 * Maneja la finalización del scraping: procesa con IA y genera Excel.
 */
async function handleScrapingComplete(
  products: ScrapedProduct[],
  columnMapping: ColumnMapping[]
): Promise<void> {
  state.products = products;

  // Notificar al popup que estamos procesando
  broadcastToPopup({
    action: 'SCRAPING_PROGRESS',
    payload: {
      currentPage: 0,
      totalPages: 0,
      productsFound: products.length,
      status: 'Procesando datos con IA...',
    } as ScrapingProgressPayload,
  });

  try {
    // Procesar con IA (si el plan lo permite)
    const profile = await getUserProfile();
    let processed: ProcessedProduct[];

    if (profile?.planType !== 'free') {
      processed = await processWithAI(products);
    } else {
      processed = products.map(rawToProcessed);
    }

    // Generar Excel
    const timestamp = new Date().toISOString().replace(/[:.]/g, '-').substring(0, 19);
    const fileName = `ScrapSAE_Export_${timestamp}`;
    const excelData = generateExcel(processed, columnMapping, fileName);

    // Descargar
    await downloadExcel(excelData, fileName);

    // Incrementar conteo diario
    await incrementDailyCount();

    // Actualizar registro de ejecución
    await saveExecution({
      id: state.executionId!,
      productsFound: products.length,
      productsExported: processed.length,
      status: 'completed',
      durationMs: Date.now() - state.startTime,
    });

    // Notificar al popup
    broadcastToPopup({
      action: 'SCRAPING_COMPLETE',
      payload: {
        productsFound: products.length,
        productsExported: processed.length,
        fileName: `${fileName}.xlsx`,
      },
    });
  } catch (error) {
    const errorMsg = error instanceof Error ? error.message : String(error);

    await saveExecution({
      id: state.executionId!,
      status: 'failed',
      errorMessage: errorMsg,
      durationMs: Date.now() - state.startTime,
    });

    broadcastToPopup({
      action: 'SCRAPING_ERROR',
      payload: { error: errorMsg },
    });
  } finally {
    state.isRunning = false;
  }
}

// ============================================================
// Comunicación
// ============================================================

function broadcastToPopup(message: ExtensionMessage): void {
  chrome.runtime.sendMessage(message).catch(() => {
    // Popup puede estar cerrado, ignorar
  });
}

// Almacenar columnMapping del último scraping para usarlo en handleScrapingComplete
let lastColumnMapping: ColumnMapping[] = [];

// ============================================================
// Listener Principal de Mensajes
// ============================================================

chrome.runtime.onMessage.addListener(
  (message: ExtensionMessage, sender, sendResponse) => {
    const handleAsync = async () => {
      switch (message.action) {
        case 'START_SCRAPING': {
          const payload = message.payload as StartScrapingPayload;
          lastColumnMapping = payload.columnMapping;

          const tab = await chrome.tabs.query({ active: true, currentWindow: true });
          if (!tab[0]?.id) {
            sendResponse({ success: false, error: 'No hay pestaña activa.' });
            return;
          }

          try {
            await startScraping(tab[0].id, payload);
            sendResponse({ success: true, executionId: state.executionId });
          } catch (error) {
            sendResponse({
              success: false,
              error: error instanceof Error ? error.message : String(error),
            });
          }
          break;
        }

        case 'STOP_SCRAPING': {
          await stopScraping();
          sendResponse({ success: true });
          break;
        }

        case 'SCRAPING_PROGRESS': {
          state.progress = message.payload as ScrapingProgressPayload;
          // Reenviar al popup
          broadcastToPopup(message);
          break;
        }

        case 'SCRAPING_COMPLETE': {
          const products = message.payload as ScrapedProduct[];
          await handleScrapingComplete(products, lastColumnMapping);
          break;
        }

        case 'SCRAPING_ERROR': {
          const { error } = message.payload as { error: string };
          state.isRunning = false;

          await saveExecution({
            id: state.executionId!,
            status: 'failed',
            errorMessage: error,
            durationMs: Date.now() - state.startTime,
          });

          broadcastToPopup(message);
          break;
        }

        case 'GET_AUTH_STATE': {
          const session = await getSession();
          const profile = session ? await getUserProfile() : null;
          sendResponse({
            isAuthenticated: !!session,
            user: session?.user ?? null,
            profile,
          });
          break;
        }

        case 'OPEN_SIDEPANEL': {
          const tab = await chrome.tabs.query({ active: true, currentWindow: true });
          if (tab[0]?.id) {
            chrome.sidePanel.open({ tabId: tab[0].id });
          }
          sendResponse({ success: true });
          break;
        }

        default:
          sendResponse({ success: false, error: `Acción desconocida: ${message.action}` });
      }
    };

    handleAsync();
    return true; // Respuesta asíncrona
  }
);

// ============================================================
// Eventos de Instalación
// ============================================================

chrome.runtime.onInstalled.addListener(async (details) => {
  if (details.reason === 'install') {
    // Primera instalación: abrir página de bienvenida
    chrome.tabs.create({ url: `${CONFIG.WEB_URL}/welcome` });
  }

  // Configurar side panel
  chrome.sidePanel.setOptions({
    enabled: true,
  });
});

console.log('[ScrapSAE SW] Service Worker initialized.');
