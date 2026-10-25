// E2E de integración: la PWA compilada servida por el servicio .NET REAL (HTTPS con
// certificado autofirmado de pruebas) y el agente de sesión REAL por named pipe, en
// WebKit a 1080×810. Sin simulador.
import path from 'node:path';
import { defineConfig, devices } from '@playwright/test';

export const SERVICE_PORT = 8450;
export const SERVICE_DATA = path.resolve('test-results/service-data');
const BIN = (project: string, tfm = 'net8.0-windows') => path.resolve(`../pc/src/${project}/bin/Debug/${tfm}/${project}.exe`);
export const SERVICE_EXE = BIN('PegasusDeck.Service');
// El agente usa los controles multimedia de Windows (WinRT): Windows 10 2004+ (ADR-022).
export const AGENT_EXE = BIN('PegasusDeck.Agent', 'net8.0-windows10.0.19041.0');
export const PIPE_NAME = 'PegasusDeck.E2E';
export const DUMMY_PIPE = 'PegasusDeck.E2E.Dummy';

const serviceArgs = [
  `--Pegasus:WebRoot=${path.resolve('dist')}`,
  '--Pegasus:SelfSignedCertificate=true',
  '--Pegasus:ListenAddresses:0=127.0.0.1',
  '--Pegasus:AllowedHosts:0=127.0.0.1',
  `--Pegasus:Port=${SERVICE_PORT}`,
  '--Pegasus:HeartbeatSeconds=1',
  '--Pegasus:UnprotectedDataDir=true', // datos en test-results (carpeta del usuario)
].join(' ');

export default defineConfig({
  testDir: 'tests/e2e-service',
  workers: 1,
  timeout: 60_000,
  expect: { timeout: 15_000 },
  reporter: [['list']],
  globalTeardown: './tests/e2e-service/teardown.ts',
  use: {
    baseURL: `https://127.0.0.1:${SERVICE_PORT}`,
    ignoreHTTPSErrors: true, // certificado autofirmado de pruebas; en el iPad se usa la CA local
    trace: 'retain-on-failure',
  },
  projects: [
    { name: 'ipad9-webkit', use: { ...devices['iPad (gen 7) landscape'], browserName: 'webkit' } },
  ],
  webServer: {
    command: [
      'npm run build',
      'dotnet build ../pc/src/PegasusDeck.Service -nologo -v q',
      'dotnet build ../pc/src/PegasusDeck.Agent -nologo -v q',
      'node scripts/run-service.mjs',
    ].join(' && '),
    env: {
      SERVICE_EXE, AGENT_EXE, DATA_DIR: SERVICE_DATA, PIPE_NAME, DUMMY_PIPE, SERVICE_ARGS: serviceArgs,
    },
    url: `https://127.0.0.1:${SERVICE_PORT}/api/health`,
    ignoreHTTPSErrors: true,
    timeout: 240_000,
    reuseExistingServer: false,
  },
});
