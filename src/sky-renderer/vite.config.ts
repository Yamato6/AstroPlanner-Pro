import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vitest/config';

const repoRoot = fileURLToPath(new URL('../..', import.meta.url));
const { version } = JSON.parse(readFileSync(new URL('package.json', import.meta.url), 'utf8')) as {
  version: string;
};

export default defineConfig(({ command }) => ({
  // Served from https://app.astroplanner/ in the app: relative asset URLs.
  base: './',
  define: {
    __APP_VERSION__: JSON.stringify(version),
    __DEV_SKY_DATA__: JSON.stringify(
      command === 'serve' ? `/@fs/${repoRoot.replaceAll('\\', '/')}data/build/sky/` : '',
    ),
  },
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    target: 'es2022',
    sourcemap: false,
    // No inline <script> or eval: everything is an external module (CSP script-src 'self').
    modulePreload: { polyfill: false },
    assetsInlineLimit: 0,
    chunkSizeWarningLimit: 1024,
  },
  server: {
    fs: { allow: [repoRoot] },
  },
  test: {
    include: ['test/**/*.test.ts'],
    environment: 'node',
  },
}));
