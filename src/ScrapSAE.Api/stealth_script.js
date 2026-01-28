// Basic Playwright Stealth Script
(() => {
    // Overwrite the `webdriver` property
    Object.defineProperty(navigator, 'webdriver', {
        get: () => false,
    });

    // Overwrite the `chrome` property
    window.chrome = {
        runtime: {},
    };

    // Overwrite the `permissions` property
    const originalQuery = window.navigator.permissions.query;
    window.navigator.permissions.query = (parameters) => (
        parameters.name === 'notifications' ?
            Promise.resolve({ state: Notification.permission }) :
            originalQuery(parameters)
    );

    // Overwrite the `plugins` property
    Object.defineProperty(navigator, 'plugins', {
        get: () => [1, 2, 3, 4, 5],
    });

    // Overwrite the `languages` property
    Object.defineProperty(navigator, 'languages', {
        get: () => ['es-MX', 'es', 'en-US', 'en'],
    });
})();
