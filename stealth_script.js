// Script de evasión stealth para Playwright
// Este script se inyecta antes de que se cargue cualquier página

(() => {
    'use strict';

    // 1. Eliminar navigator.webdriver
    Object.defineProperty(navigator, 'webdriver', {
        get: () => undefined,
        configurable: true
    });

    // 2. Simular window.chrome
    if (!window.chrome) {
        window.chrome = {
            runtime: {},
            loadTimes: function() {},
            csi: function() {},
            app: {}
        };
    }

    // 3. Modificar navigator.permissions
    const originalQuery = window.navigator.permissions.query;
    window.navigator.permissions.query = (parameters) => (
        parameters.name === 'notifications' ?
            Promise.resolve({ state: Notification.permission }) :
            originalQuery(parameters)
    );

    // 4. Simular plugins
    Object.defineProperty(navigator, 'plugins', {
        get: () => [
            {
                0: {type: "application/x-google-chrome-pdf", suffixes: "pdf", description: "Portable Document Format"},
                description: "Portable Document Format",
                filename: "internal-pdf-viewer",
                length: 1,
                name: "Chrome PDF Plugin"
            },
            {
                0: {type: "application/pdf", suffixes: "pdf", description: "Portable Document Format"},
                description: "Portable Document Format",
                filename: "mhjfbmdgcfjbbpaeojofohoefgiehjai",
                length: 1,
                name: "Chrome PDF Viewer"
            },
            {
                0: {type: "application/x-nacl", suffixes: "", description: "Native Client Executable"},
                1: {type: "application/x-pnacl", suffixes: "", description: "Portable Native Client Executable"},
                description: "",
                filename: "internal-nacl-plugin",
                length: 2,
                name: "Native Client"
            }
        ],
        configurable: true
    });

    // 5. Simular mimeTypes
    Object.defineProperty(navigator, 'mimeTypes', {
        get: () => [
            {type: "application/pdf", suffixes: "pdf", description: "Portable Document Format", enabledPlugin: {name: "Chrome PDF Plugin"}},
            {type: "application/x-google-chrome-pdf", suffixes: "pdf", description: "Portable Document Format", enabledPlugin: {name: "Chrome PDF Plugin"}},
            {type: "application/x-nacl", suffixes: "", description: "Native Client Executable", enabledPlugin: {name: "Native Client"}},
            {type: "application/x-pnacl", suffixes: "", description: "Portable Native Client Executable", enabledPlugin: {name: "Native Client"}}
        ],
        configurable: true
    });

    // 6. Modificar languages
    Object.defineProperty(navigator, 'languages', {
        get: () => ['es-MX', 'es', 'en-US', 'en'],
        configurable: true
    });

    // 7. Añadir ruido a Canvas para evitar fingerprinting
    const originalToDataURL = HTMLCanvasElement.prototype.toDataURL;
    HTMLCanvasElement.prototype.toDataURL = function(type) {
        if (type === 'image/png' && this.width > 0 && this.height > 0) {
            const context = this.getContext('2d');
            const imageData = context.getImageData(0, 0, this.width, this.height);
            for (let i = 0; i < imageData.data.length; i += 4) {
                // Añadir ruido mínimo aleatorio
                imageData.data[i] = imageData.data[i] + Math.floor(Math.random() * 3) - 1;
                imageData.data[i + 1] = imageData.data[i + 1] + Math.floor(Math.random() * 3) - 1;
                imageData.data[i + 2] = imageData.data[i + 2] + Math.floor(Math.random() * 3) - 1;
            }
            context.putImageData(imageData, 0, 0);
        }
        return originalToDataURL.apply(this, arguments);
    };

    // 8. Modificar WebGL para evitar fingerprinting
    const getParameter = WebGLRenderingContext.prototype.getParameter;
    WebGLRenderingContext.prototype.getParameter = function(parameter) {
        if (parameter === 37445) {
            return 'Intel Inc.';
        }
        if (parameter === 37446) {
            return 'Intel Iris OpenGL Engine';
        }
        return getParameter.apply(this, arguments);
    };

    // 9. Ocultar automation en Chrome
    if (window.navigator.chrome) {
        window.navigator.chrome.runtime = {
            connect: function() {},
            sendMessage: function() {}
        };
    }

    // 10. Modificar la propiedad outerHeight/outerWidth para que sea consistente
    Object.defineProperty(window, 'outerWidth', {
        get: () => window.innerWidth,
        configurable: true
    });
    Object.defineProperty(window, 'outerHeight', {
        get: () => window.innerHeight + 85, // Simular barra de navegación
        configurable: true
    });

    // 11. Simular notificaciones
    const originalNotification = window.Notification;
    Object.defineProperty(window, 'Notification', {
        get: () => {
            const NotificationProxy = function(title, options) {
                return originalNotification.call(this, title, options);
            };
            NotificationProxy.permission = 'default';
            NotificationProxy.requestPermission = originalNotification.requestPermission;
            return NotificationProxy;
        },
        configurable: true
    });

    // 12. Modificar la propiedad de conexión
    Object.defineProperty(navigator, 'connection', {
        get: () => ({
            effectiveType: '4g',
            rtt: 50,
            downlink: 10,
            saveData: false
        }),
        configurable: true
    });

    // 13. Ocultar propiedades de automatización de Playwright
    delete window.__playwright;
    delete window.__pw_manual;
    delete window.__PW_inspect;

    console.log('🥷 Stealth mode activated');
})();
