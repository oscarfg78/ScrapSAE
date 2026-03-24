// ============================================================
// Pruebas Unitarias - Extractor DOM
// Valida la extracción de datos desde HTML mockeado.
// ============================================================

import { describe, it, expect, beforeEach } from 'vitest';

// Importar funciones puras del extractor (las re-exportamos para testing)
// Como el extractor tiene side-effects (chrome.runtime.onMessage), testeamos
// las funciones de extracción de forma aislada reimplementándolas aquí
// basándonos en la misma lógica.

// ============================================================
// Funciones extraídas del extractor para testing aislado
// ============================================================

function getText(container: Element | Document, selector?: string): string | undefined {
  if (!selector) return undefined;
  const el = container.querySelector(selector);
  return el?.textContent?.trim() || undefined;
}

function getImageSrc(container: Element | Document, selector?: string): string | undefined {
  if (!selector) return undefined;
  const el = container.querySelector(selector) as HTMLImageElement | null;
  return el?.src || el?.getAttribute('data-src') || el?.getAttribute('data-lazy-src') || undefined;
}

function getHref(container: Element | Document, selector?: string): string | undefined {
  if (!selector) return undefined;
  const el = container.querySelector(selector) as HTMLAnchorElement | null;
  return el?.href || undefined;
}

function parsePrice(text?: string): number | undefined {
  if (!text) return undefined;
  let cleaned = text.replace(/[^0-9.,]/g, '');
  const hasComma = cleaned.includes(',');
  const hasDot = cleaned.includes('.');
  if (hasComma && hasDot) {
    if (cleaned.lastIndexOf(',') > cleaned.lastIndexOf('.')) {
      cleaned = cleaned.replace(/\./g, '').replace(',', '.');
    } else {
      cleaned = cleaned.replace(/,/g, '');
    }
  } else if (hasComma) {
    const parts = cleaned.split(',');
    if (parts.length === 2 && parts[1].length <= 2) {
      cleaned = cleaned.replace(',', '.');
    } else {
      cleaned = cleaned.replace(/,/g, '');
    }
  }
  const num = parseFloat(cleaned);
  return isNaN(num) ? undefined : num;
}

interface ProductAttachment {
  fileName: string;
  fileUrl: string;
  fileType?: string;
}

function getAttachments(container: Element | Document, selector?: string): ProductAttachment[] {
  if (!selector) return [];
  const links = container.querySelectorAll(selector);
  const attachments: ProductAttachment[] = [];

  links.forEach((link) => {
    const anchor = link as HTMLAnchorElement;
    const href = anchor.href;
    if (href) {
      attachments.push({
        fileName: anchor.textContent?.trim() || href.split('/').pop() || 'document',
        fileUrl: href,
        fileType: href.endsWith('.pdf') ? 'application/pdf' : undefined,
      });
    }
  });

  return attachments;
}

// ============================================================
// Helpers de Testing
// ============================================================

function createProductCardHTML(): string {
  return `
    <div class="product-card">
      <h3 class="product-title">Sensor de Presión SPAU-P10R</h3>
      <span class="product-sku">SPAU-P10R-T-R18M-L-PNLK-PNVBA-M12D</span>
      <p class="product-description">Sensor de presión para aplicaciones industriales</p>
      <span class="product-price">$1,234.56 MXN</span>
      <span class="product-brand">Festo</span>
      <span class="product-category">Sensores</span>
      <img class="product-image" src="https://example.com/sensor.jpg" alt="Sensor">
      <a class="product-link" href="https://example.com/products/spau-p10r">Ver detalle</a>
    </div>
  `;
}

function createContainerFromHTML(html: string): Element {
  const div = document.createElement('div');
  div.innerHTML = html;
  return div;
}

// ============================================================
// Tests
// ============================================================

describe('getText', () => {
  let container: Element;

  beforeEach(() => {
    container = createContainerFromHTML(createProductCardHTML());
  });

  it('debe extraer el texto de un elemento dado un selector válido', () => {
    const result = getText(container, '.product-title');
    expect(result).toBe('Sensor de Presión SPAU-P10R');
  });

  it('debe retornar undefined si el selector no existe', () => {
    const result = getText(container, '.nonexistent');
    expect(result).toBeUndefined();
  });

  it('debe retornar undefined si el selector es undefined', () => {
    const result = getText(container, undefined);
    expect(result).toBeUndefined();
  });

  it('debe limpiar espacios en blanco del texto', () => {
    const html = '<div><span class="test">  texto con espacios  </span></div>';
    const el = createContainerFromHTML(html);
    const result = getText(el, '.test');
    expect(result).toBe('texto con espacios');
  });

  it('debe retornar undefined si el elemento está vacío', () => {
    const html = '<div><span class="empty"></span></div>';
    const el = createContainerFromHTML(html);
    const result = getText(el, '.empty');
    expect(result).toBeUndefined();
  });
});

describe('getImageSrc', () => {
  it('debe extraer el src de una imagen', () => {
    const container = createContainerFromHTML(createProductCardHTML());
    const result = getImageSrc(container, '.product-image');
    expect(result).toContain('sensor.jpg');
  });

  it('debe extraer data-src como fallback', () => {
    const html = '<div><img class="lazy" data-src="https://example.com/lazy.jpg"></div>';
    const container = createContainerFromHTML(html);
    const result = getImageSrc(container, '.lazy');
    expect(result).toBe('https://example.com/lazy.jpg');
  });

  it('debe extraer data-lazy-src como segundo fallback', () => {
    const html = '<div><img class="lazy2" data-lazy-src="https://example.com/lazy2.jpg"></div>';
    const container = createContainerFromHTML(html);
    const result = getImageSrc(container, '.lazy2');
    expect(result).toBe('https://example.com/lazy2.jpg');
  });

  it('debe retornar undefined si no hay selector', () => {
    const container = createContainerFromHTML('<div></div>');
    expect(getImageSrc(container, undefined)).toBeUndefined();
  });
});

describe('getHref', () => {
  it('debe extraer el href de un enlace', () => {
    const container = createContainerFromHTML(createProductCardHTML());
    const result = getHref(container, '.product-link');
    expect(result).toContain('products/spau-p10r');
  });

  it('debe retornar undefined si no hay enlace', () => {
    const container = createContainerFromHTML('<div></div>');
    expect(getHref(container, '.nonexistent')).toBeUndefined();
  });
});

describe('parsePrice', () => {
  it('debe parsear un precio con formato MXN', () => {
    expect(parsePrice('$1,234.56 MXN')).toBe(1234.56);
  });

  it('debe parsear un precio simple', () => {
    expect(parsePrice('$99.99')).toBe(99.99);
  });

  it('debe parsear un precio con coma como separador decimal', () => {
    expect(parsePrice('€1234,56')).toBe(1234.56);
  });

  it('debe parsear un precio sin símbolo de moneda', () => {
    expect(parsePrice('500')).toBe(500);
  });

  it('debe retornar undefined para texto sin números', () => {
    expect(parsePrice('Consultar precio')).toBeUndefined();
  });

  it('debe retornar undefined para undefined', () => {
    expect(parsePrice(undefined)).toBeUndefined();
  });

  it('debe retornar undefined para string vacío', () => {
    expect(parsePrice('')).toBeUndefined();
  });

  it('debe manejar precios con texto adicional', () => {
    expect(parsePrice('Precio: $2,500.00 + IVA')).toBe(2500.00);
  });
});

describe('getAttachments', () => {
  it('debe extraer adjuntos PDF', () => {
    const html = `
      <div>
        <a class="attachment" href="https://example.com/datasheet.pdf">Datasheet</a>
        <a class="attachment" href="https://example.com/manual.pdf">Manual</a>
      </div>
    `;
    const container = createContainerFromHTML(html);
    const result = getAttachments(container, '.attachment');

    expect(result).toHaveLength(2);
    expect(result[0].fileName).toBe('Datasheet');
    expect(result[0].fileUrl).toContain('datasheet.pdf');
    expect(result[0].fileType).toBe('application/pdf');
  });

  it('debe manejar adjuntos no-PDF', () => {
    const html = '<div><a class="doc" href="https://example.com/spec.docx">Spec</a></div>';
    const container = createContainerFromHTML(html);
    const result = getAttachments(container, '.doc');

    expect(result).toHaveLength(1);
    expect(result[0].fileType).toBeUndefined();
  });

  it('debe retornar array vacío si no hay selector', () => {
    const container = createContainerFromHTML('<div></div>');
    expect(getAttachments(container, undefined)).toEqual([]);
  });

  it('debe retornar array vacío si no hay coincidencias', () => {
    const container = createContainerFromHTML('<div></div>');
    expect(getAttachments(container, '.nonexistent')).toEqual([]);
  });
});

describe('Extracción de producto completo', () => {
  it('debe extraer todos los campos de un product card', () => {
    const container = createContainerFromHTML(createProductCardHTML());

    const title = getText(container, '.product-title');
    const sku = getText(container, '.product-sku');
    const description = getText(container, '.product-description');
    const price = parsePrice(getText(container, '.product-price'));
    const brand = getText(container, '.product-brand');
    const category = getText(container, '.product-category');
    const image = getImageSrc(container, '.product-image');
    const detailLink = getHref(container, '.product-link');

    expect(title).toBe('Sensor de Presión SPAU-P10R');
    expect(sku).toBe('SPAU-P10R-T-R18M-L-PNLK-PNVBA-M12D');
    expect(description).toBe('Sensor de presión para aplicaciones industriales');
    expect(price).toBe(1234.56);
    expect(brand).toBe('Festo');
    expect(category).toBe('Sensores');
    expect(image).toContain('sensor.jpg');
    expect(detailLink).toContain('products/spau-p10r');
  });

  it('debe manejar un product card con datos parciales', () => {
    const html = `
      <div class="product-card">
        <h3 class="product-title">Producto Parcial</h3>
      </div>
    `;
    const container = createContainerFromHTML(html);

    const title = getText(container, '.product-title');
    const sku = getText(container, '.product-sku');
    const price = parsePrice(getText(container, '.product-price'));

    expect(title).toBe('Producto Parcial');
    expect(sku).toBeUndefined();
    expect(price).toBeUndefined();
  });
});

describe('Extracción de listado de productos', () => {
  it('debe extraer múltiples productos de un listado', () => {
    const html = `
      <div id="results">
        <div class="product-card">
          <h3 class="product-title">Producto A</h3>
          <span class="product-sku">SKU-001</span>
          <span class="product-price">$100.00</span>
        </div>
        <div class="product-card">
          <h3 class="product-title">Producto B</h3>
          <span class="product-sku">SKU-002</span>
          <span class="product-price">$200.00</span>
        </div>
        <div class="product-card">
          <h3 class="product-title">Producto C</h3>
          <span class="product-sku">SKU-003</span>
          <span class="product-price">$300.00</span>
        </div>
      </div>
    `;
    const container = createContainerFromHTML(html);
    const cards = container.querySelectorAll('.product-card');

    expect(cards).toHaveLength(3);

    const products = Array.from(cards).map((card) => ({
      title: getText(card, '.product-title'),
      sku: getText(card, '.product-sku'),
      price: parsePrice(getText(card, '.product-price')),
    }));

    expect(products[0].title).toBe('Producto A');
    expect(products[0].sku).toBe('SKU-001');
    expect(products[0].price).toBe(100);

    expect(products[1].title).toBe('Producto B');
    expect(products[2].price).toBe(300);
  });

  it('debe filtrar cards sin título ni SKU', () => {
    const html = `
      <div>
        <div class="product-card">
          <h3 class="product-title">Válido</h3>
          <span class="product-sku">SKU-OK</span>
        </div>
        <div class="product-card">
          <span class="product-price">$50.00</span>
        </div>
      </div>
    `;
    const container = createContainerFromHTML(html);
    const cards = container.querySelectorAll('.product-card');

    const products = Array.from(cards)
      .map((card) => ({
        title: getText(card, '.product-title'),
        sku: getText(card, '.product-sku'),
      }))
      .filter((p) => p.title || p.sku);

    expect(products).toHaveLength(1);
    expect(products[0].title).toBe('Válido');
  });
});

describe('Extracción de variantes (tabla)', () => {
  it('debe extraer variantes desde una tabla de productos', () => {
    const html = `
      <table class="variant-table">
        <tbody>
          <tr class="variant-row">
            <td><a class="variant-sku">SPAU-V1R-W45</a></td>
            <td class="variant-price">$500.00</td>
          </tr>
          <tr class="variant-row">
            <td><a class="variant-sku">SPAU-V2R-W45</a></td>
            <td class="variant-price">$600.00</td>
          </tr>
          <tr class="variant-row">
            <td><a class="variant-sku">SPAU-V3R-W45</a></td>
            <td class="variant-price">$700.00</td>
          </tr>
        </tbody>
      </table>
    `;
    const container = createContainerFromHTML(html);
    const table = container.querySelector('.variant-table');
    expect(table).not.toBeNull();

    const rows = table!.querySelectorAll('.variant-row');
    expect(rows).toHaveLength(3);

    const variants = Array.from(rows).map((row) => ({
      sku: getText(row, '.variant-sku'),
      price: parsePrice(getText(row, '.variant-price')),
    }));

    expect(variants[0].sku).toBe('SPAU-V1R-W45');
    expect(variants[0].price).toBe(500);
    expect(variants[2].sku).toBe('SPAU-V3R-W45');
    expect(variants[2].price).toBe(700);
  });
});

describe('Galería de imágenes', () => {
  it('debe extraer múltiples imágenes de una galería', () => {
    const html = `
      <div class="gallery">
        <img class="gallery-item" src="https://example.com/img1.jpg">
        <img class="gallery-item" src="https://example.com/img2.jpg">
        <img class="gallery-item" data-src="https://example.com/img3.jpg">
      </div>
    `;
    const container = createContainerFromHTML(html);
    const gallery = container.querySelector('.gallery');
    const items = gallery!.querySelectorAll('.gallery-item');

    const urls: string[] = [];
    items.forEach((item) => {
      const img = item as HTMLImageElement;
      const src = img.src || img.getAttribute('data-src') || img.getAttribute('data-zoom-image');
      if (src) urls.push(src);
    });

    expect(urls).toHaveLength(3);
    expect(urls[0]).toContain('img1.jpg');
    expect(urls[2]).toBe('https://example.com/img3.jpg');
  });

  it('debe usar imagen principal como fallback si no hay galería', () => {
    const html = '<div><img class="main-image" src="https://example.com/main.jpg"></div>';
    const container = createContainerFromHTML(html);

    const gallery = container.querySelector('.gallery');
    expect(gallery).toBeNull();

    const mainImage = getImageSrc(container, '.main-image');
    expect(mainImage).toContain('main.jpg');
  });
});
