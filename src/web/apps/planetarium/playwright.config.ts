import { defineConfig } from '@playwright/test';

// The production build (vite preview) against a mock host (e2e/mockHost.ts): the dev server
// injects CSS as <style> elements, which the page's CSP rightly blocks. The browser is the
// installed Microsoft Edge: the same engine as WebView2, and nothing to download.
export default defineConfig({
  testDir: 'e2e',
  outputDir: '../../test-results/planetarium',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI
    ? [['list'], ['html', { outputFolder: '../../playwright-report', open: 'never' }]]
    : 'list',
  use: {
    baseURL: 'http://127.0.0.1:4317',
    channel: 'msedge',
    viewport: { width: 1440, height: 900 },
    trace: 'retain-on-failure',
  },
  webServer: {
    command: 'npx vite build && npx vite preview --host 127.0.0.1 --port 4317 --strictPort',
    url: 'http://127.0.0.1:4317/planetarium.html',
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
  },
});
