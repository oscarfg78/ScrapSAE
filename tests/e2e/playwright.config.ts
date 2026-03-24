import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
    testDir: '.',
    fullyParallel: false,
    forbidOnly: !!process.env.CI,
    retries: process.env.CI ? 2 : 0,
    workers: 1,
    reporter: [
        ['html', { outputFolder: '../../reports/e2e' }],
        ['list']
    ],
    timeout: 30000,

    use: {
        trace: 'on-first-retry',
        screenshot: 'only-on-failure',
    },

    projects: [
        {
            name: 'landing-page-desktop',
            testMatch: 'landing-page.spec.ts',
            use: {
                ...devices['Desktop Chrome'],
                baseURL: process.env.LANDING_URL || 'http://localhost:8080',
            },
        },
        {
            name: 'landing-page-mobile',
            testMatch: 'landing-page.spec.ts',
            use: {
                ...devices['iPhone 13'],
                baseURL: process.env.LANDING_URL || 'http://localhost:8080',
            },
        },
        {
            name: 'extension-chromium',
            testMatch: 'extension.spec.ts',
            use: {
                browserName: 'chromium',
            },
        },
    ],
});
