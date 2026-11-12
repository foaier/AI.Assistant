import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react-swc';
import { fileURLToPath, URL } from 'node:url';

export default defineConfig({
  plugins: [react()],
  resolve: { alias: { '@': fileURLToPath(new URL('./client', import.meta.url)) } },
  server: {
    watch: { ignored: ['**/playwright-report/**', '**/test-results/**'] },
    proxy: {
      '/api': { target: 'http://127.0.0.1:5088', changeOrigin: true, ws: true },
      '/uploads': { target: 'http://127.0.0.1:5088', changeOrigin: true },
    },
  },
});
