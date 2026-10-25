import fs from 'node:fs';
import path from 'node:path';
import { defineConfig, type ProxyOptions } from 'vite';
import react from '@vitejs/plugin-react';
import { VitePWA } from 'vite-plugin-pwa';

// En desarrollo la PWA habla con el simulador del PC; en producción la sirve el
// propio servicio de Windows y la API está en el mismo origen.
const simUrl = process.env.PEGASUS_SIM_URL ?? 'http://127.0.0.1:8787';

const proxy: Record<string, ProxyOptions> = {
  '/api': { target: simUrl, ws: true },
  '/__sim': { target: simUrl },
};

// HTTPS opcional para probar en el iPad (ver README: certificados con mkcert).
const certFile = path.resolve('certs/pegasus-cert.pem');
const keyFile = path.resolve('certs/pegasus-key.pem');
const https = fs.existsSync(certFile) && fs.existsSync(keyFile)
  ? { cert: fs.readFileSync(certFile), key: fs.readFileSync(keyFile) }
  : undefined;

export default defineConfig({
  plugins: [
    react(),
    VitePWA({
      registerType: 'autoUpdate',
      injectRegister: 'auto',
      includeAssets: ['icon.svg', 'apple-touch-icon.png'],
      manifest: {
        name: 'Pegasus Deck',
        short_name: 'Pegasus',
        description: 'Controla tu PC, OBS y tus aplicaciones desde el iPad',
        display: 'standalone',
        orientation: 'any',
        background_color: '#050913',
        theme_color: '#050913',
        start_url: '/',
        icons: [
          { src: 'icon-192.png', sizes: '192x192', type: 'image/png' },
          { src: 'icon-512.png', sizes: '512x512', type: 'image/png' },
          { src: 'icon-512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
        ],
      },
      workbox: {
        // La app se abre desde la caché aunque el PC esté apagado; la API nunca se cachea.
        navigateFallback: '/index.html',
        navigateFallbackDenylist: [/^\/api\//, /^\/__sim/],
        globPatterns: ['**/*.{js,css,html,svg,png,webmanifest}'],
      },
    }),
  ],
  server: { host: true, port: 5173, https, proxy },
  preview: { host: true, port: 4173, https, proxy },
});
