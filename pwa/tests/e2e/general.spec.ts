// Página General (ADR-022/029) y pestaña DaVinci (ADR-028) contra el simulador, en WebKit a 1080×810.
// Criterio principal: lo nuevo es solo de General (y la pestaña nueva); las demás pestañas
// conservan su contenido y comportamiento. Las acciones no muestran avisos (ADR-029).
import { expect, test, type Page } from '@playwright/test';
import { DEFAULT_DECK_V2, newButton } from '../../src/core/deck';
import { SIM_PORT } from '../../playwright.config';
import { button, expectEnabled, goPage, lastToast, openPaired, sim } from './helpers';

test.describe.configure({ mode: 'serial' });

// OBS muestra ahora las escenas reales (ADR-031): se comprueba aparte.
const OTHER_PAGES = DEFAULT_DECK_V2.pages.filter((p) => p.id !== 'general' && p.id !== 'obs');
const GENERAL_IDS = ['g-mic', 'g-volume', 'g-mute-pc', 'g-pause', 'g-app-6', 'g-app-7', 'g-app-8', 'g-app-9'];

async function simState(): Promise<{ power: string; audio: { volume: number; muted: boolean }; mic: { muted: boolean }; media: string; windows: { explorer: number }; apps: Record<string, string>; resolveCommands: string[] }> {
  return (await fetch(`http://127.0.0.1:${SIM_PORT}/__sim/state`)).json();
}

/** Ids de los botones que muestra la pestaña activa, en orden de casillas. */
async function shownIds(page: Page): Promise<string[]> {
  return page.getByTestId('deck-grid').locator('[data-testid^="btn-"]').evaluateAll((els) => els.map((e) => e.getAttribute('data-testid')!.slice(4)));
}

/** Botón (o casilla libre) de la casilla n de la página activa. */
const inSlot = (page: Page, n: number) => page.getByTestId('deck-grid').locator(':scope > *').nth(n);

async function centre(page: Page, n: number): Promise<{ x: number; y: number }> {
  const b = (await inSlot(page, n).boundingBox())!;
  return { x: b.x + b.width / 2, y: b.y + b.height / 2 };
}

/** Mantener pulsado y soltar sin mover: abre el selector de la casilla. */
async function longPress(page: Page, n: number): Promise<void> {
  const c = await centre(page, n);
  await page.mouse.move(c.x, c.y);
  await page.mouse.down();
  await page.waitForTimeout(800);
  await page.mouse.up();
}

/** Mantener pulsado y arrastrar a otra casilla. */
async function dragSlot(page: Page, from: number, to: number): Promise<void> {
  const a = await centre(page, from);
  const b = await centre(page, to);
  await page.mouse.move(a.x, a.y);
  await page.mouse.down();
  await page.waitForTimeout(800);
  for (let i = 1; i <= 8; i++) await page.mouse.move(a.x + ((b.x - a.x) * i) / 8, a.y + ((b.y - a.y) * i) / 8);
  await expect(inSlot(page, to)).toHaveAttribute('data-drop-target', 'true'); // indicación del destino
  await expect(inSlot(page, from)).toHaveAttribute('data-dragging', 'true');
  await page.mouse.up();
}

/** Las pestañas que no son General ni DaVinci muestran exactamente los botones de la versión anterior. */
async function expectOtherTabsUnchanged(page: Page): Promise<void> {
  for (const p of OTHER_PAGES) {
    await goPage(page, p.name);
    expect(await shownIds(page), p.name).toEqual(p.slots.filter((id): id is string => id !== null));
  }
  await goPage(page, 'General');
}

const noToasts = (page: Page) => expect(page.getByTestId('toast')).toHaveCount(0);

test('General: 18 casillas en la cuadrícula 6×3, «Pausar/Reanudar» en un solo botón', async ({ page }) => {
  await sim('reset', { power: 'on' });
  await openPaired(page);
  await expect(page.getByTestId('chip-service')).toContainText('Conectado');
  expect(await shownIds(page)).toEqual(GENERAL_IDS);
  await expect(page.getByTestId('deck-grid')).toHaveCSS('grid-template-columns', /^(\S+ ){5}\S+$/); // 6 columnas
  await expect(page.locator('[data-slot]')).toHaveCount(18); // las 18 casillas son configurables
  for (const n of [5, 10, 11, 12, 17]) await expect(page.getByTestId(`slot-${n}`)).toBeVisible();

  await expect(page.getByTestId('btn-g-wake')).toHaveCount(0); // sin Encender/Apagar (ADR-032)
  await expect(button(page, 'g-pause')).toContainText('Pausar');
  await expectEnabled(page, 'g-pause', true);
  await expectOtherTabsUnchanged(page);
});

test('Acciones sin avisos: micrófono, silenciar PC y pausar/reanudar alternan según el estado real', async ({ page }) => {
  await sim('reset', { power: 'on' });
  await openPaired(page);

  await button(page, 'g-mic').click();
  await expect(button(page, 'g-mic')).toContainText('Activar micrófono');
  expect((await simState()).mic.muted).toBe(true);
  await button(page, 'g-mute-pc').click();
  await expect(button(page, 'g-mute-pc')).toContainText('Activar sonido');

  const media = button(page, 'g-pause');
  await media.click();
  await expect.poll(async () => (await simState()).media).toBe('paused');
  await expect(media).toContainText('Reanudar');
  await media.click();
  await expect.poll(async () => (await simState()).media).toBe('playing');
  await expect(media).toContainText('Pausar');
  await noToasts(page);

  // Nada sonando: deshabilitado con motivo. Sin micrófono en Windows: con motivo.
  await sim('media', { value: 'none' });
  await expectEnabled(page, 'g-pause', false);
  await expect(media).toContainText('Nada reproduciéndose');
  await sim('behaviour', { micUnavailable: true });
  await expect(button(page, 'g-mic')).toContainText('Micrófono no disponible');
});

test('Volumen deslizable: arrastrar cambia el volumen del PC y el relleno lo refleja', async ({ page }) => {
  await sim('reset', { power: 'on' });
  await openPaired(page);
  const slider = page.getByRole('slider', { name: 'Volumen' });
  await expect(slider).toHaveAttribute('aria-valuenow', '40');

  const box = (await slider.boundingBox())!;
  await page.mouse.move(box.x + box.width / 2, box.y + box.height * 0.8);
  await page.mouse.down();
  for (let i = 1; i <= 6; i++) await page.mouse.move(box.x + box.width / 2, box.y + box.height * (0.8 - 0.05 * i));
  await page.mouse.up();
  await expect.poll(async () => (await simState()).audio.volume).toBe(70);
  await expect(slider).toHaveAttribute('aria-valuenow', '70');
  await expect(slider).toContainText('70 %');
  expect(await page.getByTestId('volume-fill').evaluate((e) => (e as HTMLElement).style.height)).toBe('70%');

  await slider.focus();
  await page.keyboard.press('ArrowDown');
  await expect.poll(async () => (await simState()).audio.volume).toBe(65);

  await sim('behaviour', { audioUnavailable: true });
  await expect(slider).toHaveAttribute('data-enabled', 'false');
  await expect(slider).toContainText('Audio de Windows no disponible');
});

test('Mantener y soltar: elegir qué hay en cualquier casilla (apps autorizadas o controles del PC)', async ({ page }) => {
  await sim('reset', { power: 'on' });
  await openPaired(page);
  await expect(page.getByTestId('chip-service')).toContainText('Conectado');

  // Casilla libre de la tercera fila → DaVinci Resolve
  await longPress(page, 15);
  const sheet = page.getByRole('dialog', { name: 'Casilla 16' });
  await expect(sheet.getByRole('list', { name: 'Aplicaciones autorizadas' }).getByRole('button')).toHaveCount(4);
  await sheet.getByRole('button', { name: /DaVinci Resolve/ }).click();
  await expect(inSlot(page, 15)).toContainText('Abrir DaVinci Resolve');

  // Cambiar Chrome (casilla 8) por el Explorador: mantener pulsado no abre la app.
  await longPress(page, 7);
  await page.getByRole('dialog').getByRole('button', { name: /Explorador de archivos/ }).click();
  await expect(inSlot(page, 7)).toContainText('Explorador de archivos');
  expect((await simState()).apps).toMatchObject({ chrome: 'installed' });
  expect((await simState()).windows.explorer).toBe(0);

  // Un toque normal sí la abre, sin aviso.
  await inSlot(page, 7).getByRole('button').click();
  await expect.poll(async () => (await simState()).windows.explorer).toBe(1);
  await noToasts(page);

  // Quitar un control y volver a ponerlo en otra casilla (nunca duplicado).
  await longPress(page, 0);
  await page.getByRole('button', { name: 'Dejar la casilla vacía' }).click();
  await expect(page.getByTestId('slot-0')).toBeVisible();
  await longPress(page, 17);
  const controls = page.getByRole('dialog').getByRole('list', { name: 'Controles del PC' });
  await expect(controls.getByRole('button')).toHaveCount(1);
  await controls.getByRole('button', { name: 'Silenciar micrófono' }).click();
  await expect(inSlot(page, 17)).toContainText('Silenciar micrófono');

  // Se guarda y las demás pestañas siguen igual.
  await page.reload();
  await expect(inSlot(page, 7)).toContainText('Explorador de archivos');
  await expect(inSlot(page, 17)).toContainText('Silenciar micrófono');
  await expectOtherTabsUnchanged(page);
});

test('Arrastrar y soltar: mueve botones (también a casillas libres), sin ejecutar nada, y el orden persiste', async ({ page }) => {
  await sim('reset', { power: 'on' });
  await openPaired(page);
  await expect(page.getByTestId('chip-service')).toContainText('Conectado');

  // OBS (casilla 7) a una casilla libre (18): no se abre OBS al soltar.
  await dragSlot(page, 6, 17);
  await expect(inSlot(page, 17)).toContainText('Abrir OBS');
  await expect(page.getByTestId('slot-6')).toBeVisible();
  expect((await simState()).apps.obs).toBe('installed');

  // Intercambiar dos ocupadas: Silenciar PC (3) ↔ Pausar (4).
  await dragSlot(page, 2, 3);
  await expect(inSlot(page, 2)).toContainText('Pausar');
  await expect(inSlot(page, 3)).toContainText('Silenciar PC');
  expect((await simState()).media).toBe('playing'); // soltar no ejecuta

  // Nada se pierde ni se duplica y se mantiene tras reabrir la app.
  const ids = (await shownIds(page)).sort();
  expect(ids).toEqual([...GENERAL_IDS].sort());
  await page.reload();
  await expect(inSlot(page, 17)).toContainText('Abrir OBS');
  await expect(inSlot(page, 2)).toContainText('Pausar');
  await noToasts(page);
});

test('Pestaña DaVinci: secciones arriba, fila inferior fija; solo con Resolve abierto; sin avisos', async ({ page }) => {
  await sim('reset', { power: 'on' });
  await openPaired(page);
  await goPage(page, 'DaVinci');
  // Por defecto, sección Páginas; fila fija con marcador, clips, reproducir y guardar.
  await expect(page.getByTestId('section-tile')).toContainText('Páginas');
  expect(await shownIds(page)).toEqual(['d-page-media', 'd-page-cut', 'd-page-edit', 'd-page-fusion', 'd-page-color', 'd-page-fairlight', 'd-page-deliver',
    'd-marker', 'd-prev', 'd-next', 'd-play', 'd-save']);
  await expectEnabled(page, 'd-page-color', false);
  await expect(button(page, 'd-page-color')).toContainText('DaVinci Resolve');

  await sim('openApp', { id: 'davinci' });
  await expectEnabled(page, 'd-page-color', true);
  await button(page, 'd-page-color').click();

  // Cambiar a Photo: Like / Dislike; la fila fija sigue igual.
  await page.getByTestId('section-tile').click();
  await page.getByRole('dialog').getByRole('button', { name: 'Photo' }).click();
  await expect(page.getByTestId('section-tile')).toContainText('Photo');
  expect(await shownIds(page)).toEqual(['d-like', 'd-dislike', 'd-marker', 'd-prev', 'd-next', 'd-play', 'd-save']);
  await button(page, 'd-like').click();
  await button(page, 'd-dislike').click();
  await button(page, 'd-next').click();
  await expect.poll(async () => (await simState()).resolveCommands).toEqual(['page.color', 'photo.like', 'photo.dislike', 'clip.next']);
  await noToasts(page);

  // La sección elegida se recuerda al reabrir.
  await page.reload();
  await expect(page.getByTestId('section-tile')).toContainText('Photo');
});

test('OBS: botones de las escenas reales, la activa marcada y sincronizada si se cambia en OBS', async ({ page }) => {
  await sim('reset', { power: 'on' });
  await openPaired(page);
  await goPage(page, 'OBS');
  await expect(page.getByTestId('obs-scenes-empty')).toContainText('OBS no conectado');

  await sim('openApp', { id: 'obs' });
  await expect(page.getByTestId('obs-scenes-empty')).toHaveCount(0);
  const ids = await shownIds(page);
  expect(ids.slice(0, 6)).toEqual(['stream-start', 'stream-stop', 'rec-start', 'rec-stop', 'rec-pause', 'mic-mute']);
  await expect(inSlot(page, 6)).toContainText('Juego');
  await expect(inSlot(page, 7)).toContainText('Charla');
  await expect(inSlot(page, 8)).toContainText('Pausa');
  await expect(inSlot(page, 6)).toHaveAttribute('data-active', 'true'); // escena activa

  // Cambiar desde el iPad.
  await inSlot(page, 7).click();
  await expect(inSlot(page, 7)).toHaveAttribute('data-active', 'true');
  await expect(inSlot(page, 6)).toHaveAttribute('data-active', 'false');

  // Cambiar en OBS (sin tocar el iPad) y nuevas escenas: se sincroniza solo.
  await sim('obsScene', { value: 'Pausa' });
  await expect(inSlot(page, 8)).toHaveAttribute('data-active', 'true');
  await sim('obsScenes', { value: ['Juego', 'Charla', 'Pausa', 'Cámara'] });
  await expect(inSlot(page, 9)).toContainText('Cámara');
  await noToasts(page);

  // OBS se cierra: el aviso vuelve, sin notificaciones.
  await sim('closeApp', { id: 'obs' });
  await expect(page.getByTestId('obs-scenes-empty')).toContainText('OBS no conectado');
  await noToasts(page);
});

test('Migración real en el iPad: un panel v2 guardado solo cambia en General (y se añade DaVinci) y se guarda copia', async ({ page }) => {
  await sim('reset', { power: 'off' });
  await openPaired(page);
  const v2 = structuredClone({ schemaVersion: 2, ...DEFAULT_DECK_V2 });
  v2.buttons.push(newButton({ id: 't-scene-base', name: 'Escena Base', icon: 'scene', color: 'blue', action: { type: 'obs.scene.set', params: { scene: 'base' } } }));
  v2.pages.unshift({ id: 'prueba-obs', name: 'Prueba OBS', icon: 'obs', grid: { cols: 6, rows: 3 },
    slots: ['t-scene-base', ...Array<null>(17).fill(null)] });
  await page.evaluate(async (deck) => {
    await new Promise<void>((resolve, reject) => {
      const req = indexedDB.open('pegasus-deck', 1);
      req.onupgradeneeded = () => req.result.createObjectStore('kv');
      req.onerror = () => reject(req.error);
      req.onsuccess = () => {
        const tx = req.result.transaction('kv', 'readwrite');
        tx.objectStore('kv').put(deck, 'deck');
        tx.objectStore('kv').put('general', 'activePageId');
        tx.oncomplete = () => { req.result.close(); resolve(); };
      };
    });
  }, v2);
  await page.reload();
  await expect(button(page, 'g-mic')).toBeVisible();

  expect(await shownIds(page)).toEqual(GENERAL_IDS);
  await expect(lastToast(page)).toContainText('Comprobar estado'); // aviso de la migración, no de una acción
  await goPage(page, 'Prueba OBS');
  expect(await shownIds(page)).toEqual(['t-scene-base']);
  await expectOtherTabsUnchanged(page);

  const backup = await page.evaluate(() => new Promise((resolve) => {
    const req = indexedDB.open('pegasus-deck', 1);
    req.onsuccess = () => {
      const get = req.result.transaction('kv').objectStore('kv').get('deck.backup-v2');
      get.onsuccess = () => { req.result.close(); resolve(get.result); };
    };
  }));
  expect(backup).toEqual(v2);
});

test('Apps abiertas sin bola ni borde verde; la escena activa de OBS sí se marca', async ({ page }) => {
  await sim('reset', { power: 'on' });
  await sim('openApp', { id: 'chrome' });
  await openPaired(page);
  const chrome = button(page, 'g-app-7');
  await expectEnabled(page, 'g-app-7', true);
  await expect(chrome).not.toHaveClass(/ring-ok/);
  await expect(chrome.locator('.bg-ok')).toHaveCount(0);

  await sim('openApp', { id: 'obs' });
  await goPage(page, 'OBS');
  const scene = inSlot(page, 6); // las escenas son el propio botón (sin casilla editable)
  await expect(scene).toHaveAttribute('data-active', 'true');
  await expect(scene).toHaveClass(/ring-ok/);
});

test('Triple toque en una app: dice cuántas ventanas se cierran y las cierra; un toque sigue abriendo', async ({ page }) => {
  await sim('reset', { power: 'on' });
  await openPaired(page);
  await expect(page.getByTestId('chip-service')).toContainText('Conectado');
  await sim('explorerWindows', { value: 6 });
  const explorer = button(page, 'g-app-9');
  const tripleTap = () => explorer.click({ clickCount: 3 });

  await tripleTap();
  await expect(page.getByRole('alertdialog')).toContainText('¿Cerrar las 6 ventanas de Explorador de archivos?');
  await page.getByRole('button', { name: 'Cancelar' }).click();
  expect((await simState()).windows.explorer).toBe(6);

  await tripleTap();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Cerrar ventanas' }).click();
  await expect.poll(async () => (await simState()).windows.explorer).toBe(0);
  await noToasts(page);

  await explorer.click();
  await expect.poll(async () => (await simState()).windows.explorer).toBe(1);
  await expect(page.getByRole('alertdialog')).toHaveCount(0);

  // Sin ventanas: se indica en el propio botón, sin preguntar ni avisos emergentes.
  await sim('explorerWindows', { value: 0 });
  await page.waitForTimeout(500);
  await tripleTap();
  await expect(explorer).toHaveAttribute('data-error', 'Sin ventanas abiertas');
  await expect(page.getByRole('alertdialog')).toHaveCount(0);

  // Las demás pestañas no tienen triple toque.
  await goPage(page, 'Aplicaciones');
  await sim('explorerWindows', { value: 2 });
  await button(page, 'open-explorer').click({ clickCount: 3 });
  await expect(page.getByRole('alertdialog')).toHaveCount(0);
});
