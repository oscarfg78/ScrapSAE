// ============================================================
// ScrapSAE Extension - Shared Types
// Replicados fielmente desde ScrapSAE.Core/DTOs/DTOs.cs
// ============================================================

// --- Scraping DTOs ---

export interface ProductAttachment {
  fileName: string;
  fileUrl: string;
  fileType?: string;
  fileSizeBytes?: number;
}

export interface ScrapedProduct {
  skuSource?: string;
  title?: string;
  description?: string;
  rawHtml?: string;
  screenshotBase64?: string;
  imageUrl?: string;
  imageUrls: string[];
  price?: number;
  category?: string;
  brand?: string;
  sourceUrl?: string;
  attributes: Record<string, string>;
  navigationUrls: string[];
  scrapedAt: string;
  aiEnriched: boolean;
  attachments: ProductAttachment[];
}

export interface ProcessedProduct {
  sku?: string;
  name: string;
  brand?: string;
  model?: string;
  description: string;
  features: string[];
  specifications: Record<string, string>;
  suggestedCategory?: string;
  categories: string[];
  lineCode?: string;
  price?: number;
  currency?: string;
  stock?: number;
  images: string[];
  attachments: ProductAttachment[];
  confidenceScore?: number;
  originalRawData?: string;
}

// --- Selector / Layout Configuration ---

export interface SiteSelectors {
  productListSelector?: string;
  productListClassPrefix?: string;
  productCardClassPrefix?: string;
  productLinkSelector?: string;
  categoryLandingUrl?: string;
  categoryLinkSelector?: string;
  categoryNameSelector?: string;
  categorySearchTerms: string[];
  searchInputSelector?: string;
  searchButtonSelector?: string;
  titleSelector?: string;
  priceSelector?: string;
  descriptionSelector?: string;
  imageSelector?: string;
  skuSelector?: string;
  categorySelector?: string;
  brandSelector?: string;
  nextPageSelector?: string;
  detailButtonText?: string;
  detailButtonClassPrefix?: string;
  variantTableSelector?: string;
  variantRowSelector?: string;
  variantSkuLinkSelector?: string;
  detailSkuSelector?: string;
  detailPriceSelector?: string;
  usesInfiniteScroll: boolean;
  maxPages: number;
  scrapingMode?: 'traditional' | 'families';
  productFamilyLinkSelector?: string;
  productFamilyLinkText?: string;
  categoryUrls?: string[];
  variantDetailLinkSelector?: string;
  detailTitleSelector?: string;
  detailDescriptionSelector?: string;
  detailImageSelector?: string;
  imageGallerySelector?: string;
  imageGalleryItemSelector?: string;
  attachmentLinkSelector?: string;
  stockSelector?: string;
}

// --- Excel Column Mapping ---

export interface ColumnMapping {
  field: keyof ProcessedProduct | string;
  header: string;
  width?: number;
  enabled: boolean;
}

// --- User Layout ---

export interface UserLayout {
  id: string;
  userId: string;
  name: string;
  selectors: SiteSelectors;
  columnMapping: ColumnMapping[];
  isDefault: boolean;
  createdAt: string;
  updatedAt: string;
}

// --- Execution History ---

export interface ExecutionRecord {
  id: string;
  userId: string;
  layoutId?: string;
  layoutName?: string;
  sourceUrl: string;
  productsFound: number;
  productsExported: number;
  status: 'running' | 'completed' | 'failed' | 'cancelled';
  errorMessage?: string;
  durationMs?: number;
  createdAt: string;
}

// --- User Profile & Subscription ---

export interface UserProfile {
  id: string;
  email: string;
  stripeCustomerId?: string;
  subscriptionStatus: 'free' | 'pro' | 'enterprise';
  planType: 'free' | 'pro' | 'enterprise';
  createdAt: string;
  updatedAt: string;
}

// --- Plan Limits ---

export const PLAN_LIMITS = {
  free: {
    maxExtractionsPerDay: 50,
    maxLayouts: 1,
    maxPagesPerScrape: 3,
    aiProcessing: false,
    exportFormats: ['xlsx'],
  },
  pro: {
    maxExtractionsPerDay: 1000,
    maxLayouts: 20,
    maxPagesPerScrape: 50,
    aiProcessing: true,
    exportFormats: ['xlsx', 'csv', 'json'],
  },
  enterprise: {
    maxExtractionsPerDay: -1, // unlimited
    maxLayouts: -1,
    maxPagesPerScrape: -1,
    aiProcessing: true,
    exportFormats: ['xlsx', 'csv', 'json'],
  },
} as const;

// --- Messages between Extension Components ---

export type MessageAction =
  | 'START_SCRAPING'
  | 'STOP_SCRAPING'
  | 'SCRAPING_PROGRESS'
  | 'SCRAPING_COMPLETE'
  | 'SCRAPING_ERROR'
  | 'EXTRACT_PAGE'
  | 'EXTRACTION_RESULT'
  | 'GET_AUTH_STATE'
  | 'AUTH_STATE_RESPONSE'
  | 'OPEN_SIDEPANEL';

export interface ExtensionMessage {
  action: MessageAction;
  payload?: unknown;
}

export interface ScrapingProgressPayload {
  currentPage: number;
  totalPages: number;
  productsFound: number;
  status: string;
}

export interface StartScrapingPayload {
  layoutId: string;
  selectors: SiteSelectors;
  columnMapping: ColumnMapping[];
  maxPages: number;
  useHumanSimulation: boolean;
}

// --- API Responses ---

export interface ApiResponse<T> {
  success: boolean;
  data?: T;
  errorMessage?: string;
}

export interface StripeCheckoutResponse {
  checkoutUrl: string;
  sessionId: string;
}
