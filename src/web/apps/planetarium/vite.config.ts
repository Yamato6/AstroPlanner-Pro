import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vite';

const repoRoot = fileURLToPath(new URL('../../../../', import.meta.url));
const { version } = JSON.parse(
  readFileSync(new URL('../../packages/sky-engine/package.json', import.meta.url), 'utf8'),
) as { version: string };

// No React plugin: Vite compiles TSX itself (tsconfig "jsx": "react-jsx"). Fast refresh would
// need an inline <script>, which the pages' CSP forbids.
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
    rolldownOptions: {
      // TanStack Query marks its modules "use client" (React Server Components); meaningless here.
      onLog(level, log, handler) {
        if (log.code !== 'MODULE_LEVEL_DIRECTIVE') {
          handler(level, log);
        }
      },
      // index.html: the v2 Sky tab. planetarium.html: the v3 UI (--ui=web).
      input: {
        index: fileURLToPath(new URL('index.html', import.meta.url)),
        planetarium: fileURLToPath(new URL('planetarium.html', import.meta.url)),
      },
      output: {
        // The engine (three.js, zod, sky-engine) is shared by both pages: one named chunk.
        advancedChunks: {
          groups: [{ name: 'sky-engine', test: /[\\/](sky-engine|three|zod)[\\/]/ }],
        },
      },
    },
  },
  server: {
    fs: { allow: [repoRoot] },
  },
}));
