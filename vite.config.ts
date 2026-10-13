import { defineConfig, type Plugin } from 'vite';
import react from '@vitejs/plugin-react';

// Content-Security-Policy nur im Produktions-Build (Dev-Server benötigt Inline-Skripte für HMR)
const csp = (): Plugin => ({
  name: 'untplan-csp',
  apply: 'build',
  transformIndexHtml(html) {
    return html.replace(
      '<meta charset="UTF-8" />',
      `<meta charset="UTF-8" />\n    <meta http-equiv="Content-Security-Policy" content="default-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; worker-src 'self' blob:; connect-src 'self' https: http://localhost:* http://127.0.0.1:*; script-src 'self'" />`,
    );
  },
});

export default defineConfig({
  root: 'src/renderer',
  base: './',
  plugins: [react(), csp()],
  worker: { format: 'es' },
  build: { outDir: '../../dist/renderer', emptyOutDir: true, chunkSizeWarningLimit: 2000 },
  server: { port: 5173, strictPort: true },
});
