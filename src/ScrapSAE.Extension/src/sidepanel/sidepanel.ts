// ============================================================
// ScrapSAE Extension - SidePanel Controller
// Gestión de Layouts, Historial y Configuración
// ============================================================

import type {
  UserLayout,
  SiteSelectors,
  ColumnMapping,
  ExecutionRecord,
  ProcessedProduct,
} from '../shared/types';
import {
  getLayouts,
  saveLayout,
  deleteLayout,
  getExecutionHistory,
} from '../shared/supabase_client';

// ============================================================
// DOM Helpers
// ============================================================

const $ = (id: string) => document.getElementById(id)!;
const $$ = (sel: string) => document.querySelectorAll(sel);

// ============================================================
// Tab Navigation
// ============================================================

$$('.tab').forEach((tab) => {
  tab.addEventListener('click', () => {
    $$('.tab').forEach((t) => t.classList.remove('active'));
    $$('.tab-content').forEach((c) => c.classList.add('hidden'));
    tab.classList.add('active');
    const tabId = tab.getAttribute('data-tab')!;
    $(`tab-${tabId}`).classList.remove('hidden');

    // Cargar datos al cambiar de pestaña
    if (tabId === 'history') loadHistory();
    if (tabId === 'layouts') loadLayouts();
  });
});

// ============================================================
// Layouts Tab
// ============================================================

let layouts: UserLayout[] = [];
let editingLayoutId: string | null = null;

async function loadLayouts(): Promise<void> {
  layouts = await getLayouts();
  renderLayoutsList();
}

function renderLayoutsList(): void {
  const container = $('layouts-list');

  if (layouts.length === 0) {
    container.innerHTML = `
      <div class="empty-state">
        <div class="icon">&#128196;</div>
        <p>No tienes layouts configurados</p>
        <p class="text-sm text-muted">Crea uno para comenzar a extraer datos</p>
      </div>
    `;
    return;
  }

  container.innerHTML = layouts
    .map(
      (layout) => `
    <div class="layout-card" data-id="${layout.id}">
      <div class="layout-info">
        <span class="layout-name">${escapeHtml(layout.name)}</span>
        <span class="layout-meta">
          ${layout.columnMapping?.length ?? 0} columnas &middot;
          ${new Date(layout.updatedAt).toLocaleDateString('es-MX')}
        </span>
      </div>
      ${layout.isDefault ? '<span class="layout-default">Default</span>' : ''}
    </div>
  `
    )
    .join('');

  // Click handler para editar
  container.querySelectorAll('.layout-card').forEach((card) => {
    card.addEventListener('click', () => {
      const id = card.getAttribute('data-id')!;
      const layout = layouts.find((l) => l.id === id);
      if (layout) openEditor(layout);
    });
  });
}

// ============================================================
// Layout Editor
// ============================================================

function openEditor(layout?: UserLayout): void {
  $('layout-editor').classList.remove('hidden');
  editingLayoutId = layout?.id ?? null;

  // Poblar campos
  ($('layout-name') as HTMLInputElement).value = layout?.name ?? '';
  ($('layout-mode') as HTMLSelectElement).value = layout?.selectors?.scrapingMode ?? 'traditional';

  // Selectores
  const sel = layout?.selectors ?? ({} as Partial<SiteSelectors>);
  ($('sel-productList') as HTMLInputElement).value = sel.productListSelector ?? '';
  ($('sel-productCardClass') as HTMLInputElement).value = sel.productCardClassPrefix ?? '';
  ($('sel-productLink') as HTMLInputElement).value = sel.productLinkSelector ?? '';
  ($('sel-nextPage') as HTMLInputElement).value = sel.nextPageSelector ?? '';
  ($('sel-title') as HTMLInputElement).value = sel.titleSelector ?? '';
  ($('sel-price') as HTMLInputElement).value = sel.priceSelector ?? '';
  ($('sel-sku') as HTMLInputElement).value = sel.skuSelector ?? '';
  ($('sel-description') as HTMLInputElement).value = sel.descriptionSelector ?? '';
  ($('sel-image') as HTMLInputElement).value = sel.imageSelector ?? '';
  ($('sel-brand') as HTMLInputElement).value = sel.brandSelector ?? '';
  ($('sel-category') as HTMLInputElement).value = sel.categorySelector ?? '';
  ($('sel-stock') as HTMLInputElement).value = sel.stockSelector ?? '';

  // Infinite scroll toggle
  const toggle = $('toggle-infinite-scroll');
  const isInfinite = sel.usesInfiniteScroll ?? false;
  toggle.setAttribute('data-active', String(isInfinite));
  toggle.classList.toggle('active', isInfinite);

  // Column mapping
  renderColumnMapping(layout?.columnMapping ?? getDefaultColumns());

  // Mostrar botón eliminar solo si estamos editando
  $('btn-delete-layout').classList.toggle('hidden', !editingLayoutId);
}

function closeEditor(): void {
  $('layout-editor').classList.add('hidden');
  editingLayoutId = null;
}

function collectSelectors(): SiteSelectors {
  return {
    productListSelector: ($('sel-productList') as HTMLInputElement).value || undefined,
    productCardClassPrefix: ($('sel-productCardClass') as HTMLInputElement).value || undefined,
    productLinkSelector: ($('sel-productLink') as HTMLInputElement).value || undefined,
    nextPageSelector: ($('sel-nextPage') as HTMLInputElement).value || undefined,
    titleSelector: ($('sel-title') as HTMLInputElement).value || undefined,
    priceSelector: ($('sel-price') as HTMLInputElement).value || undefined,
    skuSelector: ($('sel-sku') as HTMLInputElement).value || undefined,
    descriptionSelector: ($('sel-description') as HTMLInputElement).value || undefined,
    imageSelector: ($('sel-image') as HTMLInputElement).value || undefined,
    brandSelector: ($('sel-brand') as HTMLInputElement).value || undefined,
    categorySelector: ($('sel-category') as HTMLInputElement).value || undefined,
    stockSelector: ($('sel-stock') as HTMLInputElement).value || undefined,
    usesInfiniteScroll: $('toggle-infinite-scroll').getAttribute('data-active') === 'true',
    maxPages: 10,
    scrapingMode: ($('layout-mode') as HTMLSelectElement).value as 'traditional' | 'families',
    categorySearchTerms: [],
  };
}

function collectColumnMapping(): ColumnMapping[] {
  const rows = $$('#column-mapping-list .column-row');
  const mapping: ColumnMapping[] = [];

  rows.forEach((row) => {
    const field = (row.querySelector('.col-field') as HTMLSelectElement)?.value;
    const header = (row.querySelector('.col-header') as HTMLInputElement)?.value;
    if (field && header) {
      mapping.push({ field, header, width: 20, enabled: true });
    }
  });

  return mapping;
}

async function handleSaveLayout(): Promise<void> {
  const name = ($('layout-name') as HTMLInputElement).value.trim();
  if (!name) {
    alert('Ingresa un nombre para el layout.');
    return;
  }

  const layout: Partial<UserLayout> = {
    id: editingLayoutId ?? undefined,
    name,
    selectors: collectSelectors(),
    columnMapping: collectColumnMapping(),
    isDefault: false,
  };

  try {
    await saveLayout(layout);
    closeEditor();
    await loadLayouts();
  } catch (error) {
    alert('Error al guardar: ' + (error instanceof Error ? error.message : String(error)));
  }
}

async function handleDeleteLayout(): Promise<void> {
  if (!editingLayoutId) return;
  if (!confirm('¿Eliminar este layout?')) return;

  try {
    await deleteLayout(editingLayoutId);
    closeEditor();
    await loadLayouts();
  } catch (error) {
    alert('Error al eliminar: ' + (error instanceof Error ? error.message : String(error)));
  }
}

// ============================================================
// Column Mapping UI
// ============================================================

const AVAILABLE_FIELDS: { value: string; label: string }[] = [
  { value: 'sku', label: 'SKU' },
  { value: 'name', label: 'Nombre' },
  { value: 'brand', label: 'Marca' },
  { value: 'model', label: 'Modelo' },
  { value: 'description', label: 'Descripción' },
  { value: 'price', label: 'Precio' },
  { value: 'currency', label: 'Moneda' },
  { value: 'stock', label: 'Stock' },
  { value: 'suggestedCategory', label: 'Categoría' },
  { value: 'features', label: 'Características' },
  { value: 'specifications', label: 'Especificaciones' },
  { value: 'images', label: 'Imágenes' },
  { value: 'lineCode', label: 'Código de Línea' },
];

function getDefaultColumns(): ColumnMapping[] {
  return [
    { field: 'sku', header: 'SKU', width: 15, enabled: true },
    { field: 'name', header: 'Nombre', width: 40, enabled: true },
    { field: 'price', header: 'Precio', width: 12, enabled: true },
    { field: 'brand', header: 'Marca', width: 15, enabled: true },
    { field: 'description', header: 'Descripción', width: 50, enabled: true },
  ];
}

function renderColumnMapping(columns: ColumnMapping[]): void {
  const container = $('column-mapping-list');
  container.innerHTML = '';

  columns.forEach((col, index) => {
    container.appendChild(createColumnRow(col, index));
  });
}

function createColumnRow(col: ColumnMapping, index: number): HTMLElement {
  const row = document.createElement('div');
  row.className = 'column-row';

  const fieldSelect = document.createElement('select');
  fieldSelect.className = 'form-select col-field';
  fieldSelect.innerHTML = AVAILABLE_FIELDS.map(
    (f) => `<option value="${f.value}" ${f.value === col.field ? 'selected' : ''}>${f.label}</option>`
  ).join('');

  const headerInput = document.createElement('input');
  headerInput.type = 'text';
  headerInput.className = 'form-input col-header';
  headerInput.value = col.header;
  headerInput.placeholder = 'Encabezado Excel';

  const removeBtn = document.createElement('button');
  removeBtn.className = 'btn-remove';
  removeBtn.innerHTML = '&times;';
  removeBtn.addEventListener('click', () => row.remove());

  row.appendChild(fieldSelect);
  row.appendChild(headerInput);
  row.appendChild(removeBtn);

  return row;
}

function addColumnRow(): void {
  const container = $('column-mapping-list');
  const index = container.children.length;
  const newCol: ColumnMapping = {
    field: 'sku',
    header: 'Nueva Columna',
    width: 20,
    enabled: true,
  };
  container.appendChild(createColumnRow(newCol, index));
}

// ============================================================
// History Tab
// ============================================================

async function loadHistory(): Promise<void> {
  const history = await getExecutionHistory();
  renderHistory(history);
}

function renderHistory(records: ExecutionRecord[]): void {
  const container = $('history-list');

  if (records.length === 0) {
    container.innerHTML = `
      <div class="empty-state">
        <div class="icon">&#128203;</div>
        <p>No hay ejecuciones registradas</p>
      </div>
    `;
    return;
  }

  container.innerHTML = records
    .map(
      (record) => `
    <div class="history-item">
      <div class="history-url">${escapeHtml(record.sourceUrl)}</div>
      <div class="history-meta">
        <span class="badge badge-${record.status}">${record.status}</span>
        <span>${record.productsFound} productos</span>
        <span>${record.durationMs ? (record.durationMs / 1000).toFixed(1) + 's' : '-'}</span>
        <span>${new Date(record.createdAt).toLocaleString('es-MX')}</span>
      </div>
      ${record.errorMessage ? `<div class="text-sm" style="color: var(--danger); margin-top: 4px;">${escapeHtml(record.errorMessage)}</div>` : ''}
    </div>
  `
    )
    .join('');
}

// ============================================================
// Settings Tab
// ============================================================

async function loadSettings(): Promise<void> {
  const stored = await chrome.storage.local.get(['apiBaseUrl', 'supabaseUrl', 'supabaseAnonKey']);
  ($('settings-api-url') as HTMLInputElement).value = stored.apiBaseUrl ?? '';
  ($('settings-supabase-url') as HTMLInputElement).value = stored.supabaseUrl ?? '';
  ($('settings-supabase-key') as HTMLInputElement).value = stored.supabaseAnonKey ?? '';
}

async function saveSettings(): Promise<void> {
  const apiUrl = ($('settings-api-url') as HTMLInputElement).value.trim();
  const supabaseUrl = ($('settings-supabase-url') as HTMLInputElement).value.trim();
  const supabaseKey = ($('settings-supabase-key') as HTMLInputElement).value.trim();

  await chrome.storage.local.set({
    apiBaseUrl: apiUrl || undefined,
    supabaseUrl: supabaseUrl || undefined,
    supabaseAnonKey: supabaseKey || undefined,
  });

  alert('Ajustes guardados. Recarga la extensión para aplicar cambios.');
}

// ============================================================
// Utilities
// ============================================================

function escapeHtml(text: string): string {
  const div = document.createElement('div');
  div.textContent = text;
  return div.innerHTML;
}

// ============================================================
// Event Listeners
// ============================================================

$('btn-new-layout').addEventListener('click', () => openEditor());
$('btn-close-editor').addEventListener('click', closeEditor);
$('btn-save-layout').addEventListener('click', handleSaveLayout);
$('btn-delete-layout').addEventListener('click', handleDeleteLayout);
$('btn-add-column').addEventListener('click', addColumnRow);
$('btn-save-settings').addEventListener('click', saveSettings);

$('toggle-infinite-scroll').addEventListener('click', () => {
  const toggle = $('toggle-infinite-scroll');
  const isActive = toggle.getAttribute('data-active') === 'true';
  toggle.setAttribute('data-active', String(!isActive));
  toggle.classList.toggle('active');
});

// ============================================================
// Init
// ============================================================

loadLayouts();
loadSettings();
