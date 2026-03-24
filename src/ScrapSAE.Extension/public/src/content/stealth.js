// ============================================================
// ScrapSAE Extension - Stealth Script
// Se inyecta en el world MAIN antes de que cargue la página.
// Replica el stealth_script.js del proyecto de escritorio.
// ============================================================

(() => {
  'use strict';

  // 1. Eliminar navigator.webdriver
  Object.defineProperty(navigator, 'webdriver', {
    get: () => undefined,
    configurable: true,
  });

  // 2. Simular window.chrome (solo si no existe)
  if (!window.chrome) {
    window.chrome = {
      runtime: {},
      loadTimes: function () {},
      csi: function () {},
      app: {},
    };
  }

  // 3. Modificar navigator.permissions
  const originalQuery = window.navigator.permissions.query.bind(window.navigator.permissions);
  window.navigator.permissions.query = (parameters) =>
    parameters.name === 'notifications'
      ? Promise.resolve({ state: Notification.permission })
      : originalQuery(parameters);

  // 4. Simular plugins
  Object.defineProperty(navigator, 'plugins', {
    get: () => [
      {
        0: { type: 'application/x-google-chrome-pdf', suffixes: 'pdf', description: 'Portable Document Format' },
        description: 'Portable Document Format',
        filename: 'internal-pdf-viewer',
        length: 1,
        name: 'Chrome PDF Plugin',
      },
      {
        0: { type: 'application/pdf', suffixes: 'pdf', description: 'Portable Document Format' },
        description: 'Portable Document Format',
        filename: 'mhjfbmdgcfjbbpaeojofohoefgiehjai',
        length: 1,
        name: 'Chrome PDF Viewer',
      },
      {
        0: { type: 'application/x-nacl', suffixes: '', description: 'Native Client Executable' },
        1: { type: 'application/x-pnacl', suffixes: '', description: 'Portable Native Client Executable' },
        description: '',
        filename: 'internal-nacl-plugin',
        length: 2,
        name: 'Native Client',
      },
    ],
    configurable: true,
  });

  // 5. Simular mimeTypes
  Object.defineProperty(navigator, 'mimeTypes', {
    get: () => [
      { type: 'application/pdf', suffixes: 'pdf', description: 'Portable Document Format', enabledPlugin: navigator.plugins[1] },
      { type: 'application/x-google-chrome-pdf', suffixes: 'pdf', description: 'Portable Document Format', enabledPlugin: navigator.plugins[0] },
    ],
    configurable: true,
  });

  // 6. Simular languages
  Object.defineProperty(navigator, 'languages', {
    get: () => ['es-MX', 'es', 'en-US', 'en'],
    configurable: true,
  });

  // 7. Ocultar la detección de automatización en el User-Agent
  Object.defineProperty(navigator, 'userAgent', {
    get: () => navigator.userAgent.replace(/HeadlessChrome/g, 'Chrome'),
    configurable: true,
  });

  // 8. Simular canvas fingerprint con ruido mínimo
  const originalToDataURL = HTMLCanvasElement.prototype.toDataURL;
  HTMLCanvasElement.prototype.toDataURL = function (type) {
    if (type === 'image/png' && this.width > 16 && this.height > 16) {
      const ctx = this.getContext('2d');
      if (ctx) {
        const imageData = ctx.getImageData(0, 0, 1, 1);
        imageData.data[0] = imageData.data[0] ^ 1;
        ctx.putImageData(imageData, 0, 0);
      }
    }
    return originalToDataURL.apply(this, arguments);
  };

  // 9. Simular WebGL vendor/renderer
  const getParameter = WebGLRenderingContext.prototype.getParameter;
  WebGLRenderingContext.prototype.getParameter = function (parameter) {
    if (parameter === 37445) return 'Intel Inc.';
    if (parameter === 37446) return 'Intel Iris OpenGL Engine';
    return getParameter.call(this, parameter);
  };

  // 10. Ocultar detección de iframe
  try {
    if (window.self !== window.top) {
      Object.defineProperty(window, 'self', { get: () => window.top });
    }
  } catch {
    // Cross-origin iframe, ignorar
  }

  console.log('[ScrapSAE Stealth] Anti-detection measures applied.');
})();
