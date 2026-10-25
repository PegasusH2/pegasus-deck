import { expect, type Page } from '@playwright/test';
import { SIM_PORT } from '../../playwright.config';

const SIM = `http://127.0.0.1:${SIM_PORT}`;

export async function sim(op: string, extra: Record<string, unknown> = {}): Promise<Record<string, unknown>> {
  const res = await fetch(`${SIM}/__sim/control`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ op, ...extra }),
  });
  expect(res.ok).toBe(true);
  return res.json() as Promise<Record<string, unknown>>;
}

/**
 * Deja el iPad emparejado sin pasar por la interfaz: pide un token al simulador,
 * lo guarda en IndexedDB y recarga. (El flujo real se prueba aparte.)
 */
export async function openPaired(page: Page): Promise<void> {
  const { deviceId, token } = await sim('issueToken', { deviceName: 'iPad pruebas' });
  await page.goto('/');
  await page.evaluate(async (device) => {
    await new Promise<void>((resolve, reject) => {
      const req = indexedDB.open('pegasus-deck', 1);
      req.onupgradeneeded = () => req.result.createObjectStore('kv');
      req.onerror = () => reject(req.error);
      req.onsuccess = () => {
        const tx = req.result.transaction('kv', 'readwrite');
        tx.objectStore('kv').put(device, 'device');
        tx.oncomplete = () => { req.result.close(); resolve(); };
        tx.onerror = () => reject(tx.error);
      };
    });
  }, { deviceId, token, deviceName: 'iPad pruebas', pairedAt: Date.now() });
  await page.reload();
}

export const button = (page: Page, id: string) => page.getByTestId(`btn-${id}`);

export async function expectEnabled(page: Page, id: string, enabled: boolean): Promise<void> {
  await expect(button(page, id)).toHaveAttribute('data-enabled', String(enabled));
}

export const lastToast = (page: Page) => page.getByTestId('toast').last();

/** Cambia de pestaña del panel. */
export async function goPage(page: Page, name: string): Promise<void> {
  await page.getByRole('navigation', { name: 'Páginas' }).getByRole('button', { name, exact: true }).click();
}

/** "Comprobar ahora" desde la hoja de estados (el botón «Comprobar estado» ya no está en General). */
export async function checkNow(page: Page): Promise<void> {
  await page.getByRole('button', { name: 'Estados del sistema' }).click();
  await page.getByRole('button', { name: 'Comprobar ahora' }).click();
  await page.keyboard.press('Escape');
}
