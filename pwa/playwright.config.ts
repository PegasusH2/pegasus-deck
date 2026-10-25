import { defineConfig, devices } from '@playwright/test';

export const SIM_PORT = 8790;
export const SIM_UDP_PORT = 40090;
const APP_PORT = 4180;

export default defineConfig({
  testDir: 'tests/e2e',
  // Todas las pruebas comparten un único simulador del PC: en serie.
  workers: 1,
  fullyParallel: false,
  timeout: 60_000,
  expect: { timeout: 15_000 },
  reporter: [['list']],
  use: {
    baseURL: `http://127.0.0.1:${APP_PORT}`,
    trace: 'retain-on-failure',
  },
  projects: [
    { name: 'ipad9-webkit', use: { ...devices['iPad (gen 7) landscape'], browserName: 'webkit' } }, // 1080×810 = iPad 9.ª gen
  ],
  webServer: [
    {
      command: 'node sim/server.mjs',
      url: `http://127.0.0.1:${SIM_PORT}/__sim/state`,
      env: {
        SIM_PORT: String(SIM_PORT),
        SIM_UDP_PORT: String(SIM_UDP_PORT),
        SIM_BOOT_DELAY_MS: '1500',
        SIM_SESSION_DELAY_MS: '800',
      },
      reuseExistingServer: false,
    },
    {
      command: `npm run build && node node_modules/vite/bin/vite.js preview --port ${APP_PORT} --strictPort --host 127.0.0.1`,
      url: `http://127.0.0.1:${APP_PORT}`,
      env: { PEGASUS_SIM_URL: `http://127.0.0.1:${SIM_PORT}` },
      timeout: 180_000,
      reuseExistingServer: false,
    },
  ],
});
