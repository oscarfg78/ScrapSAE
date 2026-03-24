// ============================================================
// Setup global para Vitest - Mocks de Chrome Extension APIs
// ============================================================

import { vi } from 'vitest';

// Mock de chrome.runtime
const chromeRuntimeMock = {
  sendMessage: vi.fn().mockResolvedValue(undefined),
  onMessage: {
    addListener: vi.fn(),
    removeListener: vi.fn(),
  },
  onInstalled: {
    addListener: vi.fn(),
  },
  getURL: vi.fn((path: string) => `chrome-extension://mock-id/${path}`),
};

// Mock de chrome.storage
const storageData: Record<string, unknown> = {};
const chromeStorageMock = {
  local: {
    get: vi.fn((keys: string | string[]) => {
      if (typeof keys === 'string') {
        return Promise.resolve({ [keys]: storageData[keys] });
      }
      const result: Record<string, unknown> = {};
      (keys as string[]).forEach((k) => {
        result[k] = storageData[k];
      });
      return Promise.resolve(result);
    }),
    set: vi.fn((items: Record<string, unknown>) => {
      Object.assign(storageData, items);
      return Promise.resolve();
    }),
    remove: vi.fn((keys: string | string[]) => {
      const arr = typeof keys === 'string' ? [keys] : keys;
      arr.forEach((k) => delete storageData[k]);
      return Promise.resolve();
    }),
  },
};

// Mock de chrome.tabs
const chromeTabsMock = {
  query: vi.fn().mockResolvedValue([{ id: 1, url: 'https://example.com' }]),
  sendMessage: vi.fn().mockResolvedValue(undefined),
  get: vi.fn().mockResolvedValue({ id: 1, url: 'https://example.com' }),
  create: vi.fn().mockResolvedValue({ id: 2 }),
};

// Mock de chrome.scripting
const chromeScriptingMock = {
  executeScript: vi.fn().mockResolvedValue([{ result: true }]),
};

// Mock de chrome.downloads
const chromeDownloadsMock = {
  download: vi.fn((_options: unknown, callback?: () => void) => {
    if (callback) callback();
    return 1;
  }),
};

// Mock de chrome.sidePanel
const chromeSidePanelMock = {
  open: vi.fn().mockResolvedValue(undefined),
  setOptions: vi.fn().mockResolvedValue(undefined),
};

// Mock de chrome.identity
const chromeIdentityMock = {
  getRedirectURL: vi.fn(() => 'https://mock-redirect.chromiumapp.org/'),
};

// Asignar al global
const chromeMock = {
  runtime: chromeRuntimeMock,
  storage: chromeStorageMock,
  tabs: chromeTabsMock,
  scripting: chromeScriptingMock,
  downloads: chromeDownloadsMock,
  sidePanel: chromeSidePanelMock,
  identity: chromeIdentityMock,
};

// @ts-expect-error - Mock global de chrome
globalThis.chrome = chromeMock;

// Exportar para uso en tests
export { chromeMock, storageData };
