// ============================================================
// ScrapSAE Extension - DOM Extractor (Content Script)
// Se inyecta en la pestaña activa para leer el DOM según
// los selectores configurados en el layout del usuario.
// ============================================================

import type {
  SiteSelectors,
  ScrapedProduct,
  ProductAttachment,
  StartScrapingPayload,
  ScrapingProgressPayload,
  ExtensionMessage,
} from '../shared/types';
import {
  delay,
  humanClick,
  humanScrollDown,
  scrollToElement,
  infiniteScrollLoad,
  humanType,
} from './human_simulator';

// ============================================================
// Estado del Extractor
// ============================================================

let isRunning = false;
let shouldStop = false;

// ============================================================
// Utilidades de Extracción
// ============================================================

/**
 * Obtiene el texto limpio de un elemento dado un selector.
 */
function getText(container: Element | Document, selector?: string): string | undefined {
  if (!selector) return undefined;
  const el = container.querySelector(selector);
  return el?.textContent?.trim() || undefined;
}

/**
 * Obtiene el atributo src de una imagen dado un selector.
 */
function getImageSrc(container: Element | Document, selector?: string): string | undefined {
  if (!selector) return undefined;
  const el = container.querySelector(selector) as HTMLImageElement | null;
  return el?.src || el?.getAttribute('data-src') || el?.getAttribute('data-lazy-src') || undefined;
}

/**
 * Obtiene el href de un enlace dado un selector.
 */
function getHref(container: Element | Document, selector?: string): string | undefined {
  if (!selector) return undefined;
  const el = container.querySelector(selector) as HTMLAnchorElement | null;
  return el?.href || undefined;
}

/**
 * Obtiene todas las URLs de imágenes de una galería.
 */
function getImageGallery(container: Element | Document, selectors: SiteSelectors): string[] {
  const urls: string[] = [];

  if (selectors.imageGallerySelector && selectors.imageGalleryItemSelector) {
    const gallery = container.querySelector(selectors.imageGallerySelector);
    if (gallery) {
      const items = gallery.querySelectorAll(selectors.imageGalleryItemSelector);
      items.forEach((item) => {
        const img = item as HTMLImageElement;
        const src = img.src || img.getAttribute('data-src') || img.getAttribute('data-zoom-image');
        if (src) urls.push(src);
      });
    }
  }

  // Fallback: imagen principal
  if (urls.length === 0) {
    const mainImage = getImageSrc(container, selectors.imageSelector);
    if (mainImage) urls.push(mainImage);
  }

  return urls;
}

/**
 * Obtiene los archivos adjuntos (PDFs, datasheets).
 */
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

/**
 * Parsea un precio de texto a número.
 */
function parsePrice(text?: string): number | undefined {
  if (!text) return undefined;
  const cleaned = text.replace(/[^0-9.,]/g, '').replace(',', '.');
  const num = parseFloat(cleaned);
  return isNaN(num) ? undefined : num;
}

// ============================================================
// Extracción de Producto Individual
// ============================================================

/**
 * Extrae los datos de un producto desde un contenedor DOM.
 */
function extractProductFromContainer(
  container: Element | Document,
  selectors: SiteSelectors,
  sourceUrl: string
): ScrapedProduct {
  const imageUrls = getImageGallery(container, selectors);

  return {
    skuSource: getText(container, selectors.skuSelector) || getText(container, selectors.detailSkuSelector),
    title: getText(container, selectors.titleSelector) || getText(container, selectors.detailTitleSelector),
    description: getText(container, selectors.descriptionSelector) || getText(container, selectors.detailDescriptionSelector),
    rawHtml: container instanceof Element ? container.innerHTML?.substring(0, 5000) : undefined,
    imageUrl: imageUrls[0],
    imageUrls,
    price: parsePrice(getText(container, selectors.priceSelector) || getText(container, selectors.detailPriceSelector)),
    category: getText(container, selectors.categorySelector),
    brand: getText(container, selectors.brandSelector),
    sourceUrl,
    attributes: {},
    navigationUrls: [],
    scrapedAt: new Date().toISOString(),
    aiEnriched: false,
    attachments: getAttachments(container, selectors.attachmentLinkSelector),
  };
}

// ============================================================
// Extracción de Listado (Múltiples Productos)
// ============================================================

/**
 * Extrae todos los productos visibles en una página de listado.
 */
function extractProductList(selectors: SiteSelectors): ScrapedProduct[] {
  const products: ScrapedProduct[] = [];

  // Intentar por selector de lista
  let cards: NodeListOf<Element> | Element[] = [];

  if (selectors.productListSelector) {
    cards = document.querySelectorAll(selectors.productListSelector);
  }

  // Fallback: buscar por prefijo de clase
  if (cards.length === 0 && selectors.productCardClassPrefix) {
    cards = Array.from(document.querySelectorAll(`[class*="${selectors.productCardClassPrefix}"]`));
  }

  cards.forEach((card) => {
    const product = extractProductFromContainer(card, selectors, window.location.href);

    // Solo agregar si tiene al menos título o SKU
    if (product.title || product.skuSource) {
      // Obtener el enlace al detalle si existe
      const detailLink = getHref(card, selectors.productLinkSelector);
      if (detailLink) {
        product.navigationUrls.push(detailLink);
      }
      products.push(product);
    }
  });

  return products;
}

// ============================================================
// Paginación
// ============================================================

/**
 * Intenta navegar a la siguiente página de resultados.
 * Retorna true si se logró navegar.
 */
async function goToNextPage(selectors: SiteSelectors): Promise<boolean> {
  if (selectors.usesInfiniteScroll) {
    const scrollsBefore = document.body.scrollHeight;
    await infiniteScrollLoad(1);
    return document.body.scrollHeight > scrollsBefore;
  }

  if (!selectors.nextPageSelector) return false;

  const nextButton = document.querySelector(selectors.nextPageSelector);
  if (!nextButton) return false;

  // Verificar que no esté deshabilitado
  if (
    nextButton.hasAttribute('disabled') ||
    nextButton.classList.contains('disabled') ||
    nextButton.getAttribute('aria-disabled') === 'true'
  ) {
    return false;
  }

  await scrollToElement(nextButton);
  await humanClick(nextButton);
  await delay(2000, 4000); // Esperar carga de nueva página

  return true;
}

// ============================================================
// Extracción de Detalle (Navegación a Página Individual)
// ============================================================

/**
 * Extrae datos detallados de un producto navegando a su URL.
 * Se usa cuando el listado solo tiene datos parciales.
 */
async function extractProductDetail(
  url: string,
  selectors: SiteSelectors
): Promise<ScrapedProduct | null> {
  try {
    // Navegar a la URL del detalle
    const response = await fetch(url);
    const html = await response.text();

    const parser = new DOMParser();
    const doc = parser.parseFromString(html, 'text/html');

    return extractProductFromContainer(doc, selectors, url);
  } catch (error) {
    console.error(`[ScrapSAE] Error extracting detail from ${url}:`, error);
    return null;
  }
}

// ============================================================
// Variantes (Tablas de Variantes tipo Festo)
// ============================================================

/**
 * Extrae variantes de producto desde una tabla de variantes.
 */
function extractVariants(selectors: SiteSelectors): ScrapedProduct[] {
  if (!selectors.variantTableSelector || !selectors.variantRowSelector) return [];

  const table = document.querySelector(selectors.variantTableSelector);
  if (!table) return [];

  const rows = table.querySelectorAll(selectors.variantRowSelector);
  const variants: ScrapedProduct[] = [];

  rows.forEach((row) => {
    const sku = getText(row, selectors.variantSkuLinkSelector) || getText(row, selectors.detailSkuSelector);
    const price = parsePrice(getText(row, selectors.detailPriceSelector));

    if (sku) {
      variants.push({
        skuSource: sku,
        title: getText(row, selectors.titleSelector),
        price,
        sourceUrl: getHref(row, selectors.variantDetailLinkSelector) || window.location.href,
        imageUrls: [],
        attributes: {},
        navigationUrls: [],
        scrapedAt: new Date().toISOString(),
        aiEnriched: false,
        attachments: [],
      });
    }
  });

  return variants;
}

// ============================================================
// Orquestación Principal
// ============================================================

/**
 * Función principal de extracción. Se ejecuta cuando el Service Worker
 * envía el mensaje START_SCRAPING.
 */
async function runExtraction(payload: StartScrapingPayload): Promise<ScrapedProduct[]> {
  isRunning = true;
  shouldStop = false;

  const { selectors, maxPages, useHumanSimulation } = payload;
  const allProducts: ScrapedProduct[] = [];
  let currentPage = 1;

  try {
    // Scroll inicial para simular lectura
    if (useHumanSimulation) {
      await humanScrollDown(300);
      await delay();
    }

    while (currentPage <= maxPages && !shouldStop) {
      // Reportar progreso
      sendProgress({
        currentPage,
        totalPages: maxPages,
        productsFound: allProducts.length,
        status: `Extrayendo página ${currentPage}...`,
      });

      // Extraer productos del listado
      const pageProducts = extractProductList(selectors);

      // Extraer variantes si aplica
      const variants = extractVariants(selectors);
      pageProducts.push(...variants);

      allProducts.push(...pageProducts);

      // Reportar progreso actualizado
      sendProgress({
        currentPage,
        totalPages: maxPages,
        productsFound: allProducts.length,
        status: `Página ${currentPage}: ${pageProducts.length} productos encontrados`,
      });

      // Intentar ir a la siguiente página
      if (currentPage < maxPages) {
        const hasNext = await goToNextPage(selectors);
        if (!hasNext) break;
      }

      currentPage++;
    }

    // Si hay productos con URLs de detalle, extraer datos adicionales
    const productsWithDetailUrls = allProducts.filter(
      (p) => p.navigationUrls.length > 0 && (!p.description || !p.price)
    );

    if (productsWithDetailUrls.length > 0 && !shouldStop) {
      sendProgress({
        currentPage: maxPages,
        totalPages: maxPages,
        productsFound: allProducts.length,
        status: `Enriqueciendo ${productsWithDetailUrls.length} productos con datos de detalle...`,
      });

      for (const product of productsWithDetailUrls) {
        if (shouldStop) break;

        const detailUrl = product.navigationUrls[0];
        const detail = await extractProductDetail(detailUrl, selectors);

        if (detail) {
          // Merge: datos del detalle complementan los del listado
          product.description = product.description || detail.description;
          product.price = product.price ?? detail.price;
          product.imageUrls = product.imageUrls.length > 0 ? product.imageUrls : detail.imageUrls;
          product.imageUrl = product.imageUrl || detail.imageUrl;
          product.brand = product.brand || detail.brand;
          product.attachments = product.attachments.length > 0 ? product.attachments : detail.attachments;
        }

        if (useHumanSimulation) {
          await delay(1500, 3000);
        }
      }
    }

    return allProducts;
  } finally {
    isRunning = false;
  }
}

// ============================================================
// Comunicación con el Service Worker
// ============================================================

function sendProgress(progress: ScrapingProgressPayload): void {
  chrome.runtime.sendMessage({
    action: 'SCRAPING_PROGRESS',
    payload: progress,
  } as ExtensionMessage);
}

function sendComplete(products: ScrapedProduct[]): void {
  chrome.runtime.sendMessage({
    action: 'SCRAPING_COMPLETE',
    payload: products,
  } as ExtensionMessage);
}

function sendError(error: string): void {
  chrome.runtime.sendMessage({
    action: 'SCRAPING_ERROR',
    payload: { error },
  } as ExtensionMessage);
}

// ============================================================
// Listener de Mensajes
// ============================================================

chrome.runtime.onMessage.addListener((message: ExtensionMessage, _sender, sendResponse) => {
  if (message.action === 'EXTRACT_PAGE') {
    const payload = message.payload as StartScrapingPayload;

    runExtraction(payload)
      .then((products) => {
        sendComplete(products);
        sendResponse({ success: true, count: products.length });
      })
      .catch((error) => {
        const errorMsg = error instanceof Error ? error.message : String(error);
        sendError(errorMsg);
        sendResponse({ success: false, error: errorMsg });
      });

    return true; // Indica respuesta asíncrona
  }

  if (message.action === 'STOP_SCRAPING') {
    shouldStop = true;
    sendResponse({ success: true, wasRunning: isRunning });
    return false;
  }

  return false;
});

console.log('[ScrapSAE Extractor] Content script loaded and ready.');
