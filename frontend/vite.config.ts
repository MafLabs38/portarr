/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react';
import { fileURLToPath, URL } from 'node:url';
import { defineConfig } from 'vite';

const src = fileURLToPath(new URL('./src', import.meta.url));

// Absolute imports from src (DESIGN.md §1.3): every top-level folder becomes an alias.
const aliases = [
  '__mocks__',
  'App',
  'Apps',
  'Assistant',
  'Chat',
  'Components',
  'Data',
  'Helpers',
  'Infrastructure',
  'Maintenance',
  'Styles',
  'System',
  'Utilities',
  'Workloads',
  'typings',
].map((folder) => ({
  find: new RegExp(`^${folder}(/|$)`),
  replacement: `${src}/${folder}$1`,
}));

export default defineConfig({
  root: fileURLToPath(new URL('.', import.meta.url)),
  plugins: [react()],
  resolve: { alias: aliases },
  css: {
    modules: { localsConvention: 'camelCaseOnly' },
  },
  server: {
    port: 5173,
    // PORTARR_API: where the backend runs (e.g. a dotnet watch on another port).
    // /mcp: the MCP endpoint (V5), so the address shown on the Assistant page also works in development.
    // /openapi and /docs: the OpenAPI document and its interactive reference (F-API-01), served by the backend.
    proxy: {
      '/api': process.env.PORTARR_API ?? 'http://localhost:8484',
      '/mcp': process.env.PORTARR_API ?? 'http://localhost:8484',
      '/openapi': process.env.PORTARR_API ?? 'http://localhost:8484',
      '/docs': process.env.PORTARR_API ?? 'http://localhost:8484',
    },
  },
  build: {
    outDir: fileURLToPath(new URL('../_output/UI', import.meta.url)),
    emptyOutDir: true,
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/setupTests.ts'],
    css: { modules: { classNameStrategy: 'non-scoped' } },
    coverage: {
      provider: 'v8',
      reporter: ['text', 'lcov'],
      reportsDirectory: '../coverage/frontend',
      include: ['src/**/*.{ts,tsx}'],
      exclude: ['src/**/__tests__/**', 'src/__mocks__/**', 'src/index.tsx', 'src/setupTests.ts'],
    },
  },
});
