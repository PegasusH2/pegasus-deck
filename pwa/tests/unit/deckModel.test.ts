// Catálogo de acciones, modelo del panel, migraciones, tokens de diseño y logs.
import fs from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { listActions, validateParams } from '../../src/core/actions';
import { DEFAULT_DECK, deckSchema, newButton, PALETTE } from '../../src/core/deck';
import { migrateDeck } from '../../src/core/migrations';
import { redact } from '../../src/app/log';

describe('catálogo de acciones', () => {
  it('cada acción declara tiempo máximo y modo de confirmación', () => {
    for (const a of listActions()) {
      expect(a.timeoutMs, a.type).toBeGreaterThan(0);
      expect(['response', 'event']).toContain(a.completion);
    }
  });

  it('valida parámetros: sin campos extra, ids seguros', () => {
    expect(validateParams('app.open', { appId: 'obs' }).ok).toBe(true);
    expect(validateParams('app.open', { appId: 'C:\\Windows\\cmd.exe' }).ok).toBe(false);
    expect(validateParams('app.open', { appId: 'obs', args: '/c calc' }).ok).toBe(false);
    expect(validateParams('obs.scene.set', { scene: '' }).ok).toBe(false);
    expect(validateParams('windows.lock', {}).ok).toBe(true);
    expect(validateParams('system.exec', {}).ok).toBe(false);
  });
});

describe('modelo del panel', () => {
  const clone = () => structuredClone(DEFAULT_DECK);

  it('rechaza huecos que no cuadran con la rejilla y botones inexistentes', () => {
    const a = clone();
    a.pages[0].slots.pop();
    expect(deckSchema.safeParse(a).success).toBe(false);
    const b = clone();
    b.pages[0].slots[17] = 'no-existe';
    expect(deckSchema.safeParse(b).success).toBe(false);
  });

  it('rechaza ids duplicados, colores fuera de la paleta y reintentos en acciones no idempotentes', () => {
    const a = clone();
    a.buttons.push({ ...a.buttons[0] });
    expect(deckSchema.safeParse(a).success).toBe(false);
    const b = clone() as unknown as { buttons: { color: string }[] };
    b.buttons[0].color = '#ff0000';
    expect(deckSchema.safeParse(b).success).toBe(false);
    const c = clone();
    c.buttons.find((x) => x.id === 'rec-start')!.retry = { attempts: 2, delayMs: 500 };
    expect(deckSchema.safeParse(c).success).toBe(false);
    const d = clone();
    d.buttons.find((x) => x.id === 'lock')!.retry = { attempts: 2, delayMs: 500 };
    expect(deckSchema.safeParse(d).success).toBe(true);
  });

  it('rechaza parámetros de acción no válidos dentro del panel', () => {
    const a = clone();
    a.buttons.push(newButton({ id: 'evil', name: 'Evil', icon: 'x', color: 'red', action: { type: 'app.open', params: { appId: '../../cmd' } } }));
    expect(deckSchema.safeParse(a).success).toBe(false);
  });
});

describe('migraciones', () => {
  const v1 = {
    schemaVersion: 1,
    // Página distinta de General: aquí se prueba solo v1 → v2 (General tiene su propia conversión, general.test.ts).
    pages: [{ id: 'inicio', name: 'Inicio', icon: 'home', buttonIds: ['a', 'b'] }],
    buttons: [
      { id: 'a', name: 'Bloquear', icon: 'lock', color: '#2b3a5c', action: { type: 'windows.lock', params: {} },
        availability: { always: true }, whenUnavailable: 'disable', confirm: { text: '¿Seguro?' }, caption: '' },
      { id: 'b', name: 'Raro', icon: 'x', color: '#123456', action: { type: 'system.check', params: {} },
        availability: { always: true }, whenUnavailable: 'disable', confirm: null, caption: '' },
    ],
  };

  it('v1 → v2 conserva botones, convierte colores y crea la rejilla', () => {
    const r = migrateDeck(v1);
    expect(r.ok).toBe(true);
    if (!r.ok) return;
    expect(r.migrated).toBe(true);
    const own = r.deck.buttons.filter((b) => !b.id.startsWith('d-')); // sin la pestaña nueva DaVinci
    expect(own.map((b) => b.color)).toEqual(['slate', 'slate']);
    expect(own[0].confirm).toEqual({ text: '¿Seguro?', verb: 'Confirmar' });
    expect(r.deck.pages[0].slots.slice(0, 3)).toEqual(['a', 'b', null]);
  });

  it('nunca descarta botones: si no caben, crea otra página', () => {
    const many = structuredClone(v1);
    many.buttons = Array.from({ length: 45 }, (_, i) => ({ ...v1.buttons[1], id: `b${i}` }));
    many.pages[0].buttonIds = many.buttons.map((b) => b.id);
    const r = migrateDeck(many);
    expect(r.ok).toBe(true);
    if (!r.ok) return;
    const mine = r.deck.pages.filter((p) => p.id !== 'davinci');
    const placed = mine.flatMap((p) => p.slots).filter(Boolean);
    expect(placed).toHaveLength(45);
    expect(mine.length).toBe(2);
    expect(r.warnings[0]).toMatch(/demasiados botones/);
  });

  it('rechaza versiones futuras, otros formatos y configuraciones dañadas', () => {
    expect(migrateDeck({ schemaVersion: 99 })).toMatchObject({ ok: false });
    expect(migrateDeck({ foo: 1 })).toMatchObject({ ok: false });
    expect(migrateDeck(null)).toMatchObject({ ok: false });
    expect(migrateDeck({ schemaVersion: 1, pages: 'x' })).toMatchObject({ ok: false });
    expect(migrateDeck(DEFAULT_DECK)).toMatchObject({ ok: true, migrated: false });
  });
});

describe('sistema de diseño', () => {
  const css = fs.readFileSync(path.resolve(__dirname, '../../src/ui/tokens.css'), 'utf8');

  function luminance([r, g, b]: number[]): number {
    const c = [r, g, b].map((v) => {
      const s = v / 255;
      return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
    });
    return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2];
  }

  it('todos los colores de botón tienen contraste ≥ 4.5:1 con texto blanco', () => {
    for (const name of PALETTE) {
      const m = new RegExp(`--pd-tile-${name}:\\s*(\\d+) (\\d+) (\\d+)`).exec(css);
      expect(m, name).not.toBeNull();
      const ratio = 1.05 / (luminance(m!.slice(1).map(Number)) + 0.05);
      expect(ratio, `${name}: ${ratio.toFixed(2)}`).toBeGreaterThanOrEqual(4.5);
    }
  });
});

describe('registro de diagnóstico', () => {
  it('enmascara tokens, contraseñas y códigos', () => {
    expect(redact({ token: 'abc', nested: { Authorization: 'Bearer x', password: 'p' }, code: '123456', errorCode: 'AUTH_REQUIRED', ok: 1 }))
      .toEqual({ token: '***', nested: { Authorization: '***', password: '***' }, code: '***', errorCode: 'AUTH_REQUIRED', ok: 1 });
  });
});
