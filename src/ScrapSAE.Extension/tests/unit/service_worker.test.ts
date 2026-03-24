// ============================================================
// Pruebas Unitarias - Service Worker
// Valida generación de Excel, límites de plan y conversión
// de productos crudos a procesados.
// ============================================================

import { describe, it, expect, vi, beforeEach } from 'vitest';
import * as XLSX from 'xlsx';
import type {
  ScrapedProduct,
  ProcessedProduct,
  ColumnMapping,
} from '../../src/shared/types';
import { PLAN_LIMITS } from '../../src/shared/types';

// ============================================================
// Funciones extraídas del Service Worker para testing aislado
// ============================================================

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

function getProductField(product: ProcessedProduct, field: string): unknown {
  if (field in product) {
    const value = (product as Record<string, unknown>)[field];
    if (Array.isArray(value)) return value.join(', ');
    if (typeof value === 'object' && value !== null) {
      return Object.entries(value as Record<string, string>)
        .map(([k, v]) => `${k}: ${v}`)
        .join('; ');
    }
    return value;
  }

  if (product.specifications && field in product.specifications) {
    return product.specifications[field];
  }

  return '';
}

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

function generateExcel(
  products: ProcessedProduct[],
  columnMapping: ColumnMapping[],
  _fileName: string
): Uint8Array {
  const activeColumns = columnMapping.filter((col) => col.enabled);
  const columns: ColumnMapping[] =
    activeColumns.length > 0 ? activeColumns : getDefaultColumnMapping();

  const rows = products.map((product) => {
    const row: Record<string, unknown> = {};
    columns.forEach((col) => {
      const value = getProductField(product, col.field);
      row[col.header] = value;
    });
    return row;
  });

  const worksheet = XLSX.utils.json_to_sheet(rows);
  worksheet['!cols'] = columns.map((col) => ({ wch: col.width ?? 20 }));

  const workbook = XLSX.utils.book_new();
  XLSX.utils.book_append_sheet(workbook, worksheet, 'Productos');

  const metaData = [
    { Campo: 'Fecha de exportación', Valor: new Date().toLocaleString('es-MX') },
    { Campo: 'Total de productos', Valor: products.length },
  ];
  const metaSheet = XLSX.utils.json_to_sheet(metaData);
  XLSX.utils.book_append_sheet(workbook, metaSheet, 'Info');

  return XLSX.write(workbook, { type: 'array', bookType: 'xlsx' }) as Uint8Array;
}

// ============================================================
// Datos de Prueba
// ============================================================

function createMockScrapedProduct(overrides?: Partial<ScrapedProduct>): ScrapedProduct {
  return {
    skuSource: 'SKU-TEST-001',
    title: 'Sensor de Presión Test',
    description: 'Sensor para pruebas unitarias',
    imageUrl: 'https://example.com/img.jpg',
    imageUrls: ['https://example.com/img.jpg', 'https://example.com/img2.jpg'],
    price: 1500.50,
    category: 'Sensores',
    brand: 'Festo',
    sourceUrl: 'https://example.com/product/1',
    attributes: { voltaje: '24V', presion: '10 bar' },
    navigationUrls: [],
    scrapedAt: new Date().toISOString(),
    aiEnriched: false,
    attachments: [
      { fileName: 'datasheet.pdf', fileUrl: 'https://example.com/ds.pdf', fileType: 'application/pdf' },
    ],
    ...overrides,
  };
}

function createMockProcessedProduct(overrides?: Partial<ProcessedProduct>): ProcessedProduct {
  return {
    sku: 'SKU-TEST-001',
    name: 'Sensor de Presión Test',
    brand: 'Festo',
    model: 'SPAU-P10R',
    description: 'Sensor para pruebas unitarias',
    features: ['Alta precisión', 'Resistente al agua'],
    specifications: { voltaje: '24V', presion: '10 bar' },
    suggestedCategory: 'Sensores',
    categories: ['Sensores', 'Instrumentación'],
    price: 1500.50,
    currency: 'MXN',
    stock: 25,
    images: ['https://example.com/img.jpg'],
    attachments: [],
    ...overrides,
  };
}

// ============================================================
// Tests - rawToProcessed
// ============================================================

describe('rawToProcessed', () => {
  it('debe convertir un producto crudo completo a procesado', () => {
    const raw = createMockScrapedProduct();
    const processed = rawToProcessed(raw);

    expect(processed.sku).toBe('SKU-TEST-001');
    expect(processed.name).toBe('Sensor de Presión Test');
    expect(processed.brand).toBe('Festo');
    expect(processed.description).toBe('Sensor para pruebas unitarias');
    expect(processed.price).toBe(1500.50);
    expect(processed.suggestedCategory).toBe('Sensores');
    expect(processed.categories).toEqual(['Sensores']);
    expect(processed.images).toEqual(['https://example.com/img.jpg', 'https://example.com/img2.jpg']);
    expect(processed.attachments).toHaveLength(1);
  });

  it('debe usar "Sin nombre" si el título es undefined', () => {
    const raw = createMockScrapedProduct({ title: undefined });
    const processed = rawToProcessed(raw);
    expect(processed.name).toBe('Sin nombre');
  });

  it('debe usar string vacío si la descripción es undefined', () => {
    const raw = createMockScrapedProduct({ description: undefined });
    const processed = rawToProcessed(raw);
    expect(processed.description).toBe('');
  });

  it('debe manejar categoría undefined', () => {
    const raw = createMockScrapedProduct({ category: undefined });
    const processed = rawToProcessed(raw);
    expect(processed.suggestedCategory).toBeUndefined();
    expect(processed.categories).toEqual([]);
  });

  it('debe inicializar features como array vacío', () => {
    const raw = createMockScrapedProduct();
    const processed = rawToProcessed(raw);
    expect(processed.features).toEqual([]);
  });

  it('debe preservar los attributes como specifications', () => {
    const raw = createMockScrapedProduct({
      attributes: { voltaje: '24V', corriente: '4-20mA' },
    });
    const processed = rawToProcessed(raw);
    expect(processed.specifications).toEqual({ voltaje: '24V', corriente: '4-20mA' });
  });
});

// ============================================================
// Tests - getProductField
// ============================================================

describe('getProductField', () => {
  const product = createMockProcessedProduct();

  it('debe obtener campos simples', () => {
    expect(getProductField(product, 'sku')).toBe('SKU-TEST-001');
    expect(getProductField(product, 'name')).toBe('Sensor de Presión Test');
    expect(getProductField(product, 'price')).toBe(1500.50);
  });

  it('debe convertir arrays a string separado por comas', () => {
    const result = getProductField(product, 'features');
    expect(result).toBe('Alta precisión, Resistente al agua');
  });

  it('debe convertir objetos a string key: value', () => {
    const result = getProductField(product, 'specifications');
    expect(result).toContain('voltaje: 24V');
    expect(result).toContain('presion: 10 bar');
  });

  it('debe buscar en specifications si el campo no existe directamente', () => {
    const result = getProductField(product, 'voltaje');
    expect(result).toBe('24V');
  });

  it('debe retornar string vacío para campos inexistentes', () => {
    const result = getProductField(product, 'campoInexistente');
    expect(result).toBe('');
  });
});

// ============================================================
// Tests - getDefaultColumnMapping
// ============================================================

describe('getDefaultColumnMapping', () => {
  it('debe retornar 12 columnas por defecto', () => {
    const columns = getDefaultColumnMapping();
    expect(columns).toHaveLength(12);
  });

  it('todas las columnas deben estar habilitadas', () => {
    const columns = getDefaultColumnMapping();
    columns.forEach((col) => {
      expect(col.enabled).toBe(true);
    });
  });

  it('debe incluir las columnas esenciales', () => {
    const columns = getDefaultColumnMapping();
    const fields = columns.map((c) => c.field);
    expect(fields).toContain('sku');
    expect(fields).toContain('name');
    expect(fields).toContain('price');
    expect(fields).toContain('description');
    expect(fields).toContain('images');
  });

  it('cada columna debe tener header y width', () => {
    const columns = getDefaultColumnMapping();
    columns.forEach((col) => {
      expect(col.header).toBeTruthy();
      expect(col.width).toBeGreaterThan(0);
    });
  });
});

// ============================================================
// Tests - generateExcel
// ============================================================

describe('generateExcel', () => {
  it('debe generar un Uint8Array válido', () => {
    const products = [createMockProcessedProduct()];
    const columns = getDefaultColumnMapping();

    const result = generateExcel(products, columns, 'test');
    // SheetJS puede retornar ArrayBuffer o Uint8Array dependiendo del entorno
    expect(result).toBeDefined();
    expect(result.byteLength || result.length).toBeGreaterThan(0);
  });

  it('el Excel generado debe contener los datos correctos', () => {
    const products = [
      createMockProcessedProduct({ sku: 'SKU-A', name: 'Producto A', price: 100 }),
      createMockProcessedProduct({ sku: 'SKU-B', name: 'Producto B', price: 200 }),
    ];
    const columns = getDefaultColumnMapping();

    const excelData = generateExcel(products, columns, 'test');
    const workbook = XLSX.read(excelData, { type: 'array' });

    // Debe tener dos hojas
    expect(workbook.SheetNames).toContain('Productos');
    expect(workbook.SheetNames).toContain('Info');

    // Verificar datos de la hoja Productos
    const sheet = workbook.Sheets['Productos'];
    const data = XLSX.utils.sheet_to_json<Record<string, unknown>>(sheet);

    expect(data).toHaveLength(2);
    expect(data[0]['SKU']).toBe('SKU-A');
    expect(data[0]['Nombre']).toBe('Producto A');
    expect(data[0]['Precio']).toBe(100);
    expect(data[1]['SKU']).toBe('SKU-B');
    expect(data[1]['Nombre']).toBe('Producto B');
  });

  it('debe respetar el mapeo de columnas personalizado', () => {
    const products = [createMockProcessedProduct()];
    const customColumns: ColumnMapping[] = [
      { field: 'sku', header: 'Código', width: 20, enabled: true },
      { field: 'name', header: 'Nombre del Producto', width: 30, enabled: true },
      { field: 'price', header: 'Precio Unitario', width: 15, enabled: true },
      { field: 'brand', header: 'Fabricante', width: 15, enabled: false }, // Deshabilitada
    ];

    const excelData = generateExcel(products, customColumns, 'test');
    const workbook = XLSX.read(excelData, { type: 'array' });
    const sheet = workbook.Sheets['Productos'];
    const data = XLSX.utils.sheet_to_json<Record<string, unknown>>(sheet);

    expect(data[0]).toHaveProperty('Código');
    expect(data[0]).toHaveProperty('Nombre del Producto');
    expect(data[0]).toHaveProperty('Precio Unitario');
    // La columna deshabilitada no debe aparecer
    expect(data[0]).not.toHaveProperty('Fabricante');
  });

  it('debe usar columnas por defecto si todas están deshabilitadas', () => {
    const products = [createMockProcessedProduct()];
    const disabledColumns: ColumnMapping[] = [
      { field: 'sku', header: 'SKU', width: 15, enabled: false },
    ];

    const excelData = generateExcel(products, disabledColumns, 'test');
    const workbook = XLSX.read(excelData, { type: 'array' });
    const sheet = workbook.Sheets['Productos'];
    const data = XLSX.utils.sheet_to_json<Record<string, unknown>>(sheet);

    // Debe usar las 12 columnas por defecto
    expect(Object.keys(data[0]).length).toBe(12);
  });

  it('debe incluir la hoja de metadata', () => {
    const products = [createMockProcessedProduct()];
    const columns = getDefaultColumnMapping();

    const excelData = generateExcel(products, columns, 'test');
    const workbook = XLSX.read(excelData, { type: 'array' });

    const infoSheet = workbook.Sheets['Info'];
    const infoData = XLSX.utils.sheet_to_json<Record<string, unknown>>(infoSheet);

    expect(infoData.length).toBeGreaterThanOrEqual(2);
    // Verificar que incluye el total de productos
    const totalRow = infoData.find((r) => r['Campo'] === 'Total de productos');
    expect(totalRow).toBeDefined();
    expect(totalRow!['Valor']).toBe(1);
  });

  it('debe manejar un array vacío de productos', () => {
    const excelData = generateExcel([], getDefaultColumnMapping(), 'empty');
    const workbook = XLSX.read(excelData, { type: 'array' });
    const sheet = workbook.Sheets['Productos'];
    const data = XLSX.utils.sheet_to_json(sheet);

    expect(data).toHaveLength(0);
  });
});

// ============================================================
// Tests - Plan Limits
// ============================================================

describe('PLAN_LIMITS', () => {
  it('el plan Free debe tener límites restrictivos', () => {
    expect(PLAN_LIMITS.free.maxExtractionsPerDay).toBe(50);
    expect(PLAN_LIMITS.free.maxLayouts).toBe(1);
    expect(PLAN_LIMITS.free.maxPagesPerScrape).toBe(3);
    expect(PLAN_LIMITS.free.aiProcessing).toBe(false);
  });

  it('el plan Pro debe tener límites ampliados', () => {
    expect(PLAN_LIMITS.pro.maxExtractionsPerDay).toBe(1000);
    expect(PLAN_LIMITS.pro.maxLayouts).toBe(20);
    expect(PLAN_LIMITS.pro.maxPagesPerScrape).toBe(50);
    expect(PLAN_LIMITS.pro.aiProcessing).toBe(true);
  });

  it('el plan Enterprise debe ser ilimitado', () => {
    expect(PLAN_LIMITS.enterprise.maxExtractionsPerDay).toBe(-1);
    expect(PLAN_LIMITS.enterprise.maxLayouts).toBe(-1);
    expect(PLAN_LIMITS.enterprise.maxPagesPerScrape).toBe(-1);
    expect(PLAN_LIMITS.enterprise.aiProcessing).toBe(true);
  });

  it('la verificación de límites debe funcionar correctamente', () => {
    // Simular la lógica de checkPlanLimits
    const checkLimit = (plan: 'free' | 'pro' | 'enterprise', dailyCount: number): boolean => {
      const limits = PLAN_LIMITS[plan];
      if (limits.maxExtractionsPerDay === -1) return true;
      return dailyCount < limits.maxExtractionsPerDay;
    };

    // Free: 50 extracciones
    expect(checkLimit('free', 0)).toBe(true);
    expect(checkLimit('free', 49)).toBe(true);
    expect(checkLimit('free', 50)).toBe(false);
    expect(checkLimit('free', 100)).toBe(false);

    // Pro: 1000 extracciones
    expect(checkLimit('pro', 999)).toBe(true);
    expect(checkLimit('pro', 1000)).toBe(false);

    // Enterprise: ilimitado
    expect(checkLimit('enterprise', 0)).toBe(true);
    expect(checkLimit('enterprise', 999999)).toBe(true);
  });
});
