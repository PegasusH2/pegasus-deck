// La PWA real contra el servicio .NET real y el agente de sesión real.
import { execFileSync } from 'node:child_process';
import { expect, test, type Page } from '@playwright/test';
import { SERVICE_DATA, SERVICE_EXE } from '../../playwright.service.config';
import { killSessionAgent } from './processes';

test.describe.configure({ mode: 'serial' });

/** Orden local del PC (lo que haría el usuario delante del ordenador). */
function serviceCli(...args: string[]): string {
  return execFileSync(SERVICE_EXE, [...args, `--Pegasus:DataDir=${SERVICE_DATA}`, '--Pegasus:UnprotectedDataDir=true'], { encoding: 'utf8' });
}

async function pair(page: Page): Promise<void> {
  const code = /(\d{3}) (\d{3})/.exec(serviceCli('pair'))!.slice(1).join('');
  await page.getByTestId('not-paired').getByRole('button', { name: 'Emparejar' }).click();
  await page.getByTestId('pair-code').fill(code);
  await page.getByTestId('pairing-dialog').getByRole('button', { name: 'Emparejar' }).click();
  await expect(page.getByTestId('chip-service')).toContainText('Conectado');
}

/** Panel con un botón para la app de pruebas de la lista blanca (como lo creará el editor de la fase 4). */
async function useTestDeck(page: Page): Promise<void> {
  await page.evaluate(() => new Promise<void>((resolve, reject) => {
    const button = (id: string, name: string, type: string, params: Record<string, unknown>) => ({
      id, name, icon: 'grid', color: 'blue', action: { type, params }, availability: { always: true },
      whenUnavailable: 'disable', confirm: null, timeoutMs: null, retry: { attempts: 0, delayMs: 0 },
      feedback: { success: null }, caption: 'Pruebas',
    });
    const deck = {
      schemaVersion: 2,
      pages: [{ id: 'test', name: 'Pruebas', icon: 'grid', grid: { cols: 3, rows: 2 }, slots: ['open-dummy', 'open-chrome', null, null, null, null] }],
      buttons: [button('open-dummy', 'Abrir app de pruebas', 'app.open', { appId: 'dummy' }), button('open-chrome', 'Abrir Chrome', 'app.open', { appId: 'chrome' })],
    };
    const req = indexedDB.open('pegasus-deck', 1);
    req.onsuccess = () => {
      const tx = req.result.transaction('kv', 'readwrite');
      tx.objectStore('kv').put(deck, 'deck');
      tx.objectStore('kv').put('test', 'activePageId');
      tx.oncomplete = () => { req.result.close(); resolve(); };
      tx.onerror = () => reject(tx.error);
    };
  }));
  await page.reload();
}

test('PWA ↔ servicio ↔ agente: sesión lista, app autorizada abierta y confirmada por evento', async ({ page }) => {
  await page.goto('/');
  expect(await page.evaluate(() => window.isSecureContext)).toBe(true);
  await expect(page.getByTestId('not-paired')).toContainText('Este iPad no está emparejado');
  await pair(page);

  // El agente real informa de la sesión: sin aviso de funcionamiento parcial. El agente de pruebas no
  // puede hablar con un OBS real (puerto 1, sin contraseña): «Cerrado», o «Conectando» si OBS está abierto.
  await expect(page.getByTestId('degraded-banner')).toHaveCount(0);
  await expect(page.getByTestId('chip-obs')).toContainText(/Cerrado|Conectando/);

  await useTestDeck(page);
  await expect(page.getByTestId('chip-service')).toContainText('Conectado');
  // Chrome no está en la lista blanca de pruebas: deshabilitado con motivo.
  await expect(page.getByTestId('btn-open-chrome')).toHaveAttribute('data-enabled', 'false');
  await expect(page.getByTestId('btn-open-chrome')).toContainText('no autorizada');

  // Abrir la app: aceptada → en curso → confirmada cuando el agente detecta el proceso.
  await page.getByTestId('btn-open-dummy').click();
  await expect(page.getByTestId('btn-open-dummy')).toHaveAttribute('data-active', 'true');

  await page.getByRole('button', { name: 'Estados del sistema' }).click();
  await expect(page.getByTestId('status-app-dummy')).toContainText('Lista');
  await expect(page.getByTestId('status-windows')).toContainText('Sesión iniciada');
});

test('si el agente de sesión desaparece, el servicio lo detecta y la PWA lo explica', async ({ page }) => {
  await page.goto('/');
  await pair(page);
  await expect(page.getByTestId('degraded-banner')).toHaveCount(0);

  killSessionAgent(); // como cerrar sesión en Windows
  await expect(page.getByTestId('degraded-banner')).toContainText('Sin sesión de Windows');
  await expect(page.getByTestId('chip-pc')).toContainText('Online');
});

test('revocar el iPad desde el PC lo desconecta y pide volver a emparejar', async ({ page }) => {
  await page.goto('/');
  await pair(page);

  const id = await page.evaluate(() => new Promise<string>((resolve) => {
    const req = indexedDB.open('pegasus-deck', 1);
    req.onsuccess = () => {
      const get = req.result.transaction('kv').objectStore('kv').get('device');
      get.onsuccess = () => resolve((get.result as { deviceId: string }).deviceId);
    };
  }));
  expect(serviceCli('devices', 'list')).toContain(id);
  serviceCli('devices', 'revoke', id);

  await expect(page.getByTestId('not-paired')).toContainText('ya no reconoce este iPad');
  await expect(page.getByTestId('chip-service')).toContainText('Rechazado');
});
