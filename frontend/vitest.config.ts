import { defineConfig } from 'vitest/config';
import { fileURLToPath, URL } from 'node:url';

export default defineConfig({
  esbuild: { jsx: 'automatic' },
  resolve: { alias: { '@': fileURLToPath(new URL('./client', import.meta.url)) } },
  test: {
    environment: 'jsdom',
    environmentOptions: { jsdom: { url: 'http://127.0.0.1:5188' } },
    setupFiles: ['client/test/setup.ts'],
    include: ['client/**/*.{test,spec}.{ts,tsx}'],
    pool: 'forks',
    maxWorkers: 3,
  },
});
