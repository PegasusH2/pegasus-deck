// E2E del núcleo contra el simulador del PC, en WebKit a 1080×810 (iPad 9.ª gen horizontal).
// Numeración: lista de pruebas obligatorias del proyecto + casos de la fase 2.
import { expect, test } from '@playwright/test';
import { button, expectEnabled, goPage, lastToast, openPaired, sim } from './helpers';

// Página General (v3, ADR-022; sin Encender/Apagar desde ADR-032): g-mic, g-volume, g-mute-pc, g-pause, g-play y apps
// g-app-6 (OBS), g-app-7 (Chrome), g-app-8 (DaVinci), g-app-9 (Explorador). Lo de OBS se prueba en su pestaña.

test.describe.configure({ mode: 'serial' });

test('1 · PC apagado: los botones que dependen del PC están deshabilitados y explican por qué', async ({ page }) => {
  await sim('reset', { power: 'off' });
  await openPaired(page);

  await expect(page.getByTestId('chip-pc')).toContainText('Offline');
  await expect(page.getByTestId('global-status')).toHaveAttribute('data-status', 'PC_OFFLINE');
  await expect(page.getByTestId('btn-g-wake')).toHaveCount(0); // Pegasus Deck no enciende ni apaga el PC (ADR-032)
  for (const id of ['g-mic', 'g-volume', 'g-mute-pc', 'g-pause', 'g-app-6', 'g-app-7', 'g-app-8', 'g-app-9']) await expectEnabled(page, id, false);

  // Un botón bloqueado se puede tocar para ver el motivo (Playwright exige force con aria-disabled)
  await button(page, 'g-app-7').click({ force: true });
  await expect(lastToast(page)).toContainText('Falta: PC online');
  await goPage(page, 'OBS');
  await expectEnabled(page, 'rec-start', false);
});

test('2–5 · PC encendido a mano: el iPad lo detecta solo, conecta con el servicio y habilita Windows', async ({ page }) => {
  await sim('reset', { power: 'off' });
  await openPaired(page);
  await expect(page.getByTestId('chip-pc')).toContainText('Offline');
  await expectEnabled(page, 'g-app-7', false);

  // Encendido físico del PC, fuera de Pegasus Deck.
  await sim('powerOn');

  await expect(page.getByTestId('chip-pc')).toContainText('Online');
  await expect(page.getByTestId('chip-service')).toContainText('Conectado');
  await expectEnabled(page, 'g-app-7', true);
  await expectEnabled(page, 'g-mic', true);
  await expect(page.getByTestId('btn-g-wake')).toHaveCount(0);
  await expect(page.getByTestId('global-status')).toHaveAttribute('data-status', 'SERVICE_READY');
  await expect(page.getByTestId('toast')).toHaveCount(0);
  await goPage(page, 'OBS');
  await expectEnabled(page, 'rec-start', false);
});

test('6–7 · OBS: abrir habilita sus botones solo al confirmarse; al cerrarse se deshabilitan sin recargar', async ({ page }) => {
  await sim('reset', { power: 'on' });
  await openPaired(page);
  await expect(page.getByTestId('chip-obs')).toContainText('Cerrado');
  await goPage(page, 'Aplicaciones');

  // 6. Abrir OBS: aceptada → en curso → confirmada
  await button(page, 'open-obs').click();
  await expect(page.getByTestId('chip-obs')).toContainText('Conectado');
  await expect(page.getByTestId('global-status')).toHaveAttribute('data-status', 'OBS_READY');
  await expect(button(page, 'open-obs')).toHaveAttribute('data-enabled', 'false');
  await goPage(page, 'OBS');
  await expectEnabled(page, 'rec-start', true);

  await button(page, 'rec-start').click();
  await expectEnabled(page, 'rec-start', false);
  await expectEnabled(page, 'rec-pause', true);

  // 7. OBS se cierra en el PC → solo cambian los botones de OBS
  await sim('closeApp', { id: 'obs' });
  await expect(page.getByTestId('chip-obs')).toContainText('Cerrado');
  await expectEnabled(page, 'rec-start', false);
  await expectEnabled(page, 'rec-pause', false);
  await goPage(page, 'Aplicaciones');
  await expectEnabled(page, 'open-obs', true);
  await expectEnabled(page, 'open-chrome', true);
});

test('8 · Emparejamiento desde la interfaz: código incorrecto, correcto y persistencia', async ({ page }) => {
  await sim('reset', { power: 'on' });
  await page.goto('/');
  // Sin emparejar no hay panel (ni botones bloqueados): un aviso con la forma de emparejar (ADR-033).
  await expect(page.getByTestId('not-paired')).toContainText('Este iPad no está emparejado');
  await expect(page.getByTestId('deck-grid')).toHaveCount(0);
  await expect(page.locator('[data-testid^="btn-"]')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'General', exact: true })).toHaveCount(0);

  await page.getByTestId('not-paired').getByRole('button', { name: 'Emparejar' }).click();
  const { code } = await sim('pairingCode');
  const wrong = String(code) === '000000' ? '111111' : '000000';
  await page.getByTestId('pair-code').fill(wrong);
  await page.getByTestId('pairing-dialog').getByRole('button', { name: 'Emparejar' }).click();
  await expect(page.getByTestId('pairing-dialog')).toContainText('Código incorrecto');

  await page.getByTestId('pair-code').fill(String(code));
  await page.getByTestId('pairing-dialog').getByRole('button', { name: 'Emparejar' }).click();
  await expect(page.getByTestId('pairing-dialog')).toHaveCount(0);
  await expect(page.getByTestId('chip-service')).toContainText('Conectado');
  await expect(page.getByTestId('not-paired')).toHaveCount(0);
  await expectEnabled(page, 'g-app-7', true);

  await page.reload();
  await expect(page.getByTestId('chip-service')).toContainText('Conectado');
});

test('Token revocado en el PC: se detecta y se pide volver a emparejar', async ({ page }) => {
  await sim('reset', { power: 'on' });
  await openPaired(page);
  await expect(page.getByTestId('chip-service')).toContainText('Conectado');

  await sim('revokeAll');
  await expect(page.getByTestId('not-paired')).toContainText('ya no reconoce este iPad');
  await expect(page.getByTestId('chip-service')).toContainText('Rechazado');
  await expect(page.getByTestId('deck-grid')).toHaveCount(0);
  await page.getByTestId('not-paired').getByRole('button', { name: 'Emparejar' }).click();
  await expect(page.getByTestId('pairing-dialog')).toBeVisible();
});

test('PC que se apaga: los botones dependientes se deshabilitan automáticamente', async ({ page }) => {
  await sim('reset', { power: 'on' });
  await openPaired(page);
  await expectEnabled(page, 'g-app-7', true);

  await sim('powerOff');
  await expect(page.getByTestId('chip-pc')).toContainText('Offline');
  await expectEnabled(page, 'g-app-7', false);
});

test('Pérdida del canal de eventos con el PC encendido: aviso, pausa y recuperación automática', async ({ page }) => {
  await sim('reset', { power: 'on' });
  await openPaired(page);
  await expect(page.getByTestId('chip-service')).toContainText('Conectado');

  await sim('behaviour', { blockEvents: true });
  await sim('dropEvents');
  await expect(page.getByTestId('reconnect-banner')).toBeVisible();
  await expectEnabled(page, 'g-app-7', false);
  await expect(page.getByTestId('chip-pc')).toContainText('Online');

  await sim('behaviour', { blockEvents: false });
  await expect(page.getByTestId('chip-service')).toContainText('Conectado', { timeout: 25_000 });
  await expect(page.getByTestId('reconnect-banner')).toHaveCount(0);
  await expectEnabled(page, 'g-app-7', true);
});

test('Windows sin sesión y OBS con contraseña incorrecta: funcionamiento parcial explicado', async ({ page }) => {
  await sim('reset', { power: 'on' });
  await sim('session', { value: false });
  await openPaired(page);
  await expect(page.getByTestId('degraded-banner')).toContainText('Sin sesión de Windows');
  await expectEnabled(page, 'g-app-7', false);

  await sim('session', { value: true });
  await expect(page.getByTestId('degraded-banner')).toHaveCount(0);
  await expectEnabled(page, 'g-app-7', true);

  await sim('obsAuthError');
  await expect(page.getByTestId('degraded-banner')).toContainText('contraseña');
  await expect(page.getByTestId('chip-obs')).toContainText('Contraseña incorrecta');
  await goPage(page, 'OBS');
  await expectEnabled(page, 'rec-start', false);
});

test('Respuesta tardía: la acción no se da por hecha sin confirmación y se avisa si llega después', async ({ page }) => {
  await sim('reset', { power: 'on' });
  await sim('openApp', { id: 'obs' });
  await openPaired(page);
  await goPage(page, 'OBS');
  await expectEnabled(page, 'rec-start', true);

  await sim('behaviour', { confirmDelayMs: 12_000 });
  await button(page, 'rec-start').click();
  await expect(button(page, 'rec-start')).toHaveAttribute('data-run', 'running');
  await expect(button(page, 'rec-start')).toContainText('En curso');
  // Sin avisos (ADR-029): el propio botón dice que no se confirmó a tiempo.
  await expect(button(page, 'rec-start')).toHaveAttribute('data-error', /no confirmó/, { timeout: 15_000 });
  await expect(page.getByTestId('toast')).toHaveCount(0);
  // El estado real (grabando) llega por la instantánea: iniciar ya no está disponible y pausar sí.
  await expectEnabled(page, 'rec-start', false);
  await expectEnabled(page, 'rec-pause', true);
});

test('Si OBS no puede grabar, el iPad muestra el motivo concreto en lugar de un tiempo agotado', async ({ page }) => {
  await sim('reset', { power: 'on' });
  await sim('openApp', { id: 'obs' });
  await openPaired(page);
  await goPage(page, 'OBS');
  await expectEnabled(page, 'rec-start', true);

  await sim('behaviour', { silentRecordFailure: true });
  await button(page, 'rec-start').click();
  await expect(button(page, 'rec-start')).toHaveAttribute('data-error', /revisa el aviso en OBS/);
  await expectEnabled(page, 'rec-start', true); // no se da por grabando
});

test('Audio de Windows: silenciar y fijar volumen con indicador de activo', async ({ page }) => {
  await sim('reset', { power: 'on' });
  await openPaired(page);
  await page.getByRole('button', { name: 'Audio', exact: true }).click();
  await expectEnabled(page, 'audio-mute', true);

  await button(page, 'audio-mute').click();
  await expect(button(page, 'audio-mute')).toHaveAttribute('data-active', 'true');

  await button(page, 'vol-75').click();
  await expect(button(page, 'vol-75')).toHaveAttribute('data-active', 'true');
  await expect(button(page, 'vol-25')).toHaveAttribute('data-active', 'false');

  await sim('behaviour', { audioUnavailable: true });
  await expectEnabled(page, 'audio-mute', false);
  await expect(button(page, 'audio-mute')).toContainText('Audio de Windows no disponible');
});

test('Doble toque: una acción en curso no se envía dos veces', async ({ page }) => {
  await sim('reset', { power: 'on' });
  await sim('openApp', { id: 'obs' });
  await openPaired(page);
  await goPage(page, 'OBS');
  await expectEnabled(page, 'rec-start', true);

  await sim('behaviour', { confirmDelayMs: 1500 });
  await button(page, 'rec-start').click();
  await button(page, 'rec-start').click();
  await expectEnabled(page, 'rec-start', false); // grabando
  await expect(page.getByTestId('toast').filter({ hasText: 'ya está en curso' })).toHaveCount(0);
  await expect(page.locator('[data-testid="toast"][data-kind="error"]')).toHaveCount(0);
});

test('9 · Ajustes y página activa sobreviven a un reinicio de la PWA', async ({ page }) => {
  await sim('reset', { power: 'off' });
  await openPaired(page);
  await page.getByRole('button', { name: 'Configuración', exact: true }).click(); // engranaje de la cabecera
  await page.getByTestId('field-name').fill('PC del estudio');
  await page.getByTestId('settings-save').click();
  await expect(page.getByTestId('field-name')).toHaveValue('PC del estudio');
  await page.getByRole('button', { name: 'Panel', exact: true }).click();
  await page.getByRole('button', { name: 'OBS', exact: true }).click();

  await page.reload();
  await expect(page.getByRole('button', { name: 'OBS', exact: true })).toHaveAttribute('aria-current', 'page');
  await page.getByRole('button', { name: 'Configuración', exact: true }).click(); // engranaje de la cabecera
  await expect(page.getByTestId('field-name')).toHaveValue('PC del estudio');
  // Sin encendido: la configuración ya no tiene Wake-on-LAN (ADR-032).
  await expect(page.getByText('Wake-on-LAN')).toHaveCount(0);
});

test('Ajustes inválidos no se guardan y se marcan', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: 'Configuración', exact: true }).click(); // engranaje de la cabecera
  await page.getByTestId('field-name').fill('');
  await page.getByTestId('settings-save').click();
  await expect(page.getByText('Pon un nombre')).toBeVisible();
});

test('13 · Las acciones con confirmación muestran un verbo concreto y no se ejecutan si se cancela', async ({ page }) => {
  await sim('reset', { power: 'on' });
  await sim('openApp', { id: 'obs' });
  await openPaired(page);
  await goPage(page, 'OBS');
  await expectEnabled(page, 'stream-start', true);

  await button(page, 'stream-start').click();
  await expect(page.getByRole('alertdialog')).toContainText('¿Empezar a emitir en directo?');
  await page.getByRole('button', { name: 'Cancelar' }).click();
  await expect(page.getByRole('alertdialog')).toHaveCount(0);
  await expectEnabled(page, 'stream-stop', false);

  await button(page, 'stream-start').click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Emitir' }).click();
  await expectEnabled(page, 'stream-stop', true);
});

test('La cuadrícula 6×3 cabe sin scroll a 1080×810 y los botones son cómodos de tocar', async ({ page }) => {
  await sim('reset', { power: 'off' });
  await openPaired(page);
  await expect(page.getByTestId('btn-g-mic')).toBeVisible();
  const { scroll, viewport } = await page.evaluate(() => ({
    scroll: document.scrollingElement!.scrollHeight,
    viewport: window.innerHeight,
  }));
  expect(viewport).toBe(810);
  expect(scroll).toBeLessThanOrEqual(viewport);
  const box = await button(page, 'g-app-9').boundingBox(); // segunda fila
  expect(box!.y + box!.height).toBeLessThanOrEqual(810);
  expect(box!.width).toBeGreaterThanOrEqual(120);
  expect(box!.height).toBeGreaterThanOrEqual(120);
});
