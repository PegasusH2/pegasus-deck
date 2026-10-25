// Página General v3 (ADR-022): 6 controles del PC + 6 accesos a apps configurables.
// Criterio principal: la conversión SOLO cambia General; el resto de páginas y
// todos los botones existentes quedan idénticos.
import { describe, expect, it } from 'vitest';
import {
  dynamicSceneCount,
  obsSceneButton,
  sectionSelectorSlot,
  sectionSlot,
  assignGeneralApp,
  assignGeneralSlot,
  moveGeneralSlot,
  resolveButton,
  buttonAvailability,
  DEFAULT_DECK,
  DEFAULT_DECK_V2,
  deckSchema,
  GENERAL_APP_SLOTS,
  newButton,
  type DeckConfig,
} from '../../src/core/deck';
import { migrateDeck } from '../../src/core/migrations';
import { snapshotSchema } from '../../src/core/contract';
import { buildFacts } from '../../src/core/system';
import { link, machine, snapshot } from './fixtures';

const NATIVES = ['g-mic', 'g-volume', 'g-mute-pc', 'g-pause', 'g-play'];
const v2 = (): unknown => structuredClone({ schemaVersion: 2, ...DEFAULT_DECK_V2 });
const general = (d: DeckConfig) => d.pages.find((p) => p.id === 'general')!;
const btn = (d: DeckConfig, id: string) => d.buttons.find((b) => b.id === id)!;

/** El panel que tiene ahora mismo el usuario: una página «Prueba OBS» delante de las de fábrica. */
function userDeckV2(): unknown {
  const d = structuredClone({ schemaVersion: 2, ...DEFAULT_DECK_V2 });
  d.buttons.push(
    newButton({ id: 't-scene-base', name: 'Escena Base', icon: 'scene', color: 'blue', action: { type: 'obs.scene.set', params: { scene: 'base' } } }),
    newButton({ id: 't-rec-start', name: 'Grabar', icon: 'record', color: 'red', action: { type: 'obs.recording.start', params: {} } }),
  );
  d.pages.unshift({ id: 'prueba-obs', name: 'Prueba OBS', icon: 'obs', grid: { cols: 6, rows: 3 },
    slots: ['t-rec-start', null, null, null, null, null, 't-scene-base', ...Array<null>(11).fill(null)] });
  return d;
}

const OWN = new Set(['general', 'davinci', 'obs']); // General cambia, DaVinci es nueva, OBS con escenas reales (ADR-028/029/031)

describe('General por defecto (v6)', () => {
  it('4 controles en la primera fila (Pausar/Reanudar en uno; sin Encender/Apagar, ADR-032), apps en la segunda, 18 casillas editables', () => {
    const g = general(DEFAULT_DECK);
    expect(g.grid).toEqual({ cols: 6, rows: 3 });
    expect(g.slots).toHaveLength(18);
    expect(g.editable).toBe(true);
    expect(g.slots.slice(0, 6)).toEqual(['g-mic', 'g-volume', 'g-mute-pc', 'g-pause', null, null]);
    expect(g.slots.slice(6, 12)).toEqual(['g-app-6', 'g-app-7', 'g-app-8', 'g-app-9', null, null]);
    expect(g.slots.slice(6, 10).map((id) => btn(DEFAULT_DECK, id!).action.params.appId)).toEqual(['obs', 'chrome', 'davinci', 'explorer']);
    expect(btn(DEFAULT_DECK, 'g-volume').widget).toBe('slider');
    void GENERAL_APP_SLOTS;
  });

  it('pestaña DaVinci detrás de OBS: secciones arriba y fila inferior fija', () => {
    const ids = DEFAULT_DECK.pages.map((p) => p.id);
    expect(ids.indexOf('davinci')).toBe(ids.indexOf('obs') + 1);
    const dv = DEFAULT_DECK.pages.find((p) => p.id === 'davinci')!;
    expect(dv.sections!.map((x) => x.name)).toEqual(['Páginas', 'Edición', 'Color', 'Entrega', 'Photo']);
    expect(sectionSelectorSlot(dv)).toBe(12);
    expect(dv.slots.slice(13)).toEqual(['d-marker', 'd-prev', 'd-next', 'd-play', 'd-save']);
    // La fila fija es la misma en todas las secciones; arriba cambia.
    for (const sec of dv.sections!) {
      expect(sectionSlot(dv, sec.id, 14)).toBe('d-prev');
      for (const id of sec.slots.filter(Boolean)) expect(btn(DEFAULT_DECK, id!).action.type).toBe('davinci.command');
    }
    expect(sectionSlot(dv, 'photo', 0)).toBe('d-like');
    expect(sectionSlot(dv, 'photo', 1)).toBe('d-dislike');
    expect(sectionSlot(dv, 'pages', 4)).toBe('d-page-color');
    expect(sectionSlot(dv, 'desconocida', 0)).toBe('d-page-media'); // sección no válida → la primera
  });

  it('pestaña OBS: fila 1 igual y escenas reales desde la casilla 6', () => {
    const obs = DEFAULT_DECK.pages.find((p) => p.id === 'obs')!;
    const before = DEFAULT_DECK_V2.pages.find((p) => p.id === 'obs')!;
    expect(obs.slots.slice(0, 6)).toEqual(before.slots.slice(0, 6));
    expect(obs.slots.slice(6).every((x) => x === null)).toBe(true);
    expect(obs.dynamic).toEqual({ kind: 'obsScenes', from: 6 });
    expect(dynamicSceneCount(obs)).toBe(12);
    expect(obsSceneButton(7, 'Juego')).toMatchObject({ name: 'Juego', action: { type: 'obs.scene.set', params: { scene: 'Juego' } } });
  });

  it('las demás páginas por defecto no cambian respecto a la v2', () => {
    for (const p of DEFAULT_DECK.pages.filter((x) => !OWN.has(x.id))) {
      expect(p).toEqual(DEFAULT_DECK_V2.pages.find((x) => x.id === p.id));
    }
  });
});

describe('migración desde v2: solo General (y la pestaña nueva DaVinci)', () => {
  it('las demás páginas y todos los botones anteriores quedan idénticos (mismo contenido)', () => {
    const before = userDeckV2() as DeckConfig;
    const r = migrateDeck(structuredClone(before));
    expect(r.ok).toBe(true);
    if (!r.ok) return;
    expect(r.migrated).toBe(true);
    expect(r.deck.schemaVersion).toBe(8);
    // Mismas páginas y mismo orden, más DaVinci.
    expect(r.deck.pages.map((p) => p.id).filter((id) => id !== 'davinci')).toEqual(before.pages.map((p) => p.id));
    for (const p of before.pages.filter((x) => !OWN.has(x.id))) {
      expect(r.deck.pages.find((x) => x.id === p.id)).toStrictEqual(p);
    }
    for (const b of before.buttons) expect(btn(r.deck, b.id)).toStrictEqual(b);
  });

  it('General conserva su cuadrícula y sus apps, y avisa de lo que ya no aparece en ninguna página', () => {
    const r = migrateDeck(v2());
    if (!r.ok) throw new Error(r.error);
    const g = general(r.deck);
    expect(g.grid).toEqual({ cols: 6, rows: 3 });
    expect(g.slots.slice(0, 6)).toEqual(['g-mic', 'g-volume', 'g-mute-pc', 'g-pause', null, null]);
    const obs = btn(r.deck, 'g-app-6');
    const old = DEFAULT_DECK_V2.buttons.find((b) => b.id === 'open-obs')!;
    expect({ ...obs, id: old.id }).toEqual(old);
    expect(r.warnings.join(' ')).toMatch(/«Comprobar estado».*«Configuración».*«Bloquear Windows»/);
    expect(r.warnings.join(' ')).not.toMatch(/Encender PC|Iniciar grabación/);
  });

  it('una General más grande conserva los huecos a partir del 12', () => {
    const d = v2() as DeckConfig;
    const g = d.pages.find((p) => p.id === 'general')!;
    g.grid = { cols: 8, rows: 3 };
    g.slots = [...g.slots, ...Array<null>(6).fill(null)];
    g.slots[20] = 'lock';
    const r = migrateDeck(d);
    if (!r.ok) throw new Error(r.error);
    expect(general(r.deck).grid).toEqual({ cols: 8, rows: 3 });
    expect(general(r.deck).slots[20]).toBe('lock');
  });

  it('sin página General solo se añade la pestaña DaVinci', () => {
    const d = v2() as DeckConfig;
    d.pages = d.pages.filter((p) => p.id !== 'general');
    const r = migrateDeck(structuredClone(d));
    if (!r.ok) throw new Error(r.error);
    expect(r.deck.pages.filter((p) => !OWN.has(p.id))).toEqual(d.pages.filter((p) => !OWN.has(p.id)));
    for (const b of d.buttons) expect(btn(r.deck, b.id)).toEqual(b);
  });

  it('v5 → v6: solo General (Pausar/Reanudar, editable) y DaVinci; una v6 no se vuelve a convertir', () => {
    const v5 = structuredClone({ ...DEFAULT_DECK, schemaVersion: 5 }) as unknown as DeckConfig;
    v5.pages = v5.pages.filter((p) => p.id !== 'davinci');
    v5.buttons = v5.buttons.filter((b) => !b.id.startsWith('d-'));
    const g5 = general(v5);
    delete g5.editable;
    g5.slots[5] = 'g-play';
    delete btn(v5, 'g-pause').alternate;
    const r = migrateDeck(v5);
    if (!r.ok) throw new Error(r.error);
    expect(general(r.deck).slots[5]).toBeNull();
    expect(general(r.deck).editable).toBe(true);
    expect(btn(r.deck, 'g-play')).toBeDefined(); // no se borra: solo deja de estar en la casilla
    expect(btn(r.deck, 'g-pause').alternate?.name).toBe('Reanudar');
    for (const p of v5.pages.filter((x) => x.id !== 'general')) expect(r.deck.pages.find((x) => x.id === p.id)).toStrictEqual(p);
    expect(migrateDeck(structuredClone(DEFAULT_DECK))).toMatchObject({ ok: true, migrated: false });
  });
});

describe('casillas configurables y reordenables de General', () => {
  it('cualquier casilla admite una app autorizada; vaciar borra el acceso sin tocar nada más', () => {
    const next = assignGeneralApp(DEFAULT_DECK, 15, { id: 'notepad', name: 'Bloc de notas' })!;
    expect(next).not.toBeNull();
    const id = general(next).slots[15]!;
    expect(btn(next, id)).toMatchObject({ name: 'Abrir Bloc de notas', action: { type: 'app.open', params: { appId: 'notepad' } } });
    const empty = assignGeneralApp(next, 6, null)!;
    expect(general(empty).slots[6]).toBeNull();
    expect(empty.buttons.some((b) => b.id === 'g-app-6')).toBe(false);
    for (const p of empty.pages.filter((x) => x.id !== 'general')) expect(p).toBe(DEFAULT_DECK.pages.find((x) => x.id === p.id));
  });

  it('un control del PC se puede quitar y volver a poner, pero nunca duplicar', () => {
    const without = assignGeneralSlot(DEFAULT_DECK, 0, null)!;
    expect(general(without).slots[0]).toBeNull();
    expect(btn(without, 'g-mic')).toBeDefined(); // sigue guardado
    const back = assignGeneralSlot(without, 17, { control: 'g-mic' })!;
    expect(general(back).slots[17]).toBe('g-mic');
    expect(assignGeneralSlot(back, 16, { control: 'g-mic' })).toBeNull();
    expect(assignGeneralSlot(back, 16, { control: 'open-obs' })).toBeNull(); // solo controles de General
  });

  it('arrastrar intercambia dos casillas sin duplicar ni perder nada', () => {
    const moved = moveGeneralSlot(DEFAULT_DECK, 0, 17)!;
    const g = general(moved);
    expect(g.slots[0]).toBeNull();
    expect(g.slots[17]).toBe('g-mic');
    expect(g.slots.filter(Boolean).sort()).toEqual(general(DEFAULT_DECK).slots.filter(Boolean).sort());
    expect(moved.buttons).toBe(DEFAULT_DECK.buttons);
    expect(moveGeneralSlot(DEFAULT_DECK, 3, 3)).toBeNull();
    expect(deckSchema.safeParse(moved).success).toBe(true);
  });

  it('rechaza ids que no son de la lista blanca (rutas)', () => {
    expect(assignGeneralApp(DEFAULT_DECK, 10, { id: 'C:/Windows/System32/cmd.exe', name: 'cmd' })).toBeNull();
  });

  it('el deslizador solo sirve para el volumen; en páginas no editables los huecos de app solo admiten apps', () => {
    const slider = structuredClone(DEFAULT_DECK);
    btn(slider, 'g-mic').widget = 'slider';
    expect(deckSchema.safeParse(slider).success).toBe(false);
    const legacy = structuredClone(DEFAULT_DECK);
    delete general(legacy).editable;
    general(legacy).appSlots = [6];
    general(legacy).slots[6] = 'g-mic';
    expect(deckSchema.safeParse(legacy).success).toBe(false);
  });
});

describe('Sin encender ni apagar el PC (ADR-032)', () => {
  const OLD_POWER = {
    id: 'g-wake', name: 'Encender PC', icon: 'power', color: 'green', action: { type: 'system.wake', params: {} },
    availability: null, whenUnavailable: 'disable', confirm: null, caption: 'Wake-on-LAN', retry: { attempts: 0, delayMs: 1000 },
    alternate: { when: { fact: 'pc.online' }, name: 'Apagar PC', icon: 'power-off', color: 'red', action: { type: 'system.shutdown', params: {} },
      confirm: { text: '¿Apagar?', verb: 'Apagar' }, caption: 'Apagar' },
  };

  it('el catálogo ya no tiene encender ni apagar', async () => {
    const { getAction } = await import('../../src/core/actions');
    for (const t of ['system.wake', 'system.shutdown', 'system.restart']) expect(getAction(t)).toBeUndefined();
    expect(DEFAULT_DECK.buttons.some((b) => b.action.type.startsWith('system.wake') || b.alternate?.action.type === 'system.shutdown')).toBe(false);
  });

  it('v7 → v8: el botón Encender/Apagar desaparece y su casilla queda vacía; nada más se mueve', () => {
    const v7 = structuredClone({ ...DEFAULT_DECK, schemaVersion: 7 }) as unknown as { pages: DeckConfig['pages']; buttons: unknown[] };
    const g = v7.pages.find((p) => p.id === 'general')!;
    g.slots[9] = 'g-wake';
    v7.buttons.push(structuredClone(OLD_POWER));
    const r = migrateDeck(v7);
    if (!r.ok) throw new Error(r.error);
    expect(r.migrated).toBe(true);
    expect(r.deck.schemaVersion).toBe(8);
    expect(r.deck.buttons.some((b) => b.id === 'g-wake')).toBe(false);
    expect(general(r.deck).slots[9]).toBeNull();
    const before = { ...g, slots: g.slots.map((x) => (x === 'g-wake' ? null : x)) };
    expect(general(r.deck)).toStrictEqual(before);
    for (const p of v7.pages.filter((x) => x.id !== 'general')) expect(r.deck.pages.find((x) => x.id === p.id)).toStrictEqual(p);
  });

  it('v2 con «Encender PC» en otra página: se quita también de ahí', () => {
    const d = v2() as DeckConfig;
    d.pages.find((p) => p.id === 'apps')!.slots[5] = 'wake';
    d.buttons.push({ ...structuredClone(OLD_POWER), id: 'wake', alternate: undefined } as unknown as DeckConfig['buttons'][number]);
    const r = migrateDeck(d);
    if (!r.ok) throw new Error(r.error);
    expect(r.deck.buttons.some((b) => b.id === 'wake')).toBe(false);
    expect(r.deck.pages.find((p) => p.id === 'apps')!.slots[5]).toBeNull();
  });

  it('un JSON actual (v8) que pida encender o apagar se rechaza entero', () => {
    const bad = structuredClone(DEFAULT_DECK) as unknown as { buttons: unknown[] };
    bad.buttons.push(structuredClone(OLD_POWER));
    expect(migrateDeck(bad).ok).toBe(false);
  });
});

describe('estados de los controles de General', () => {
  const facts = (snap = snapshot(), pc: Parameters<typeof machine>[0] = 'PC_ONLINE') =>
    buildFacts({ pc: machine(pc), link: link('READY'), snapshot: snap });
  const enabled = (id: string, f = facts()) => buttonAvailability(btn(DEFAULT_DECK, id), f);

  it('los controles solo funcionan con el PC encendido y sesión', () => {
    const off = buildFacts({ pc: machine('PC_OFFLINE'), link: link('DISCONNECTED'), snapshot: null });
    for (const id of NATIVES) expect(enabled(id, off).enabled, id).toBe(false);
    const noSession = facts(snapshot({ session: false }));
    for (const id of NATIVES) expect(enabled(id, noSession).enabled, id).toBe(false);
  });

  it('pausar solo si algo suena; reanudar solo si está en pausa (con motivo)', () => {
    expect(enabled('g-pause', facts(snapshot({ media: 'playing' }))).enabled).toBe(true);
    expect(enabled('g-play', facts(snapshot({ media: 'playing' }))).enabled).toBe(false);
    const paused = facts(snapshot({ media: 'paused' }));
    expect(enabled('g-play', paused).enabled).toBe(true);
    const p = enabled('g-pause', paused);
    expect(p.enabled).toBe(false);
    expect(p.blockers[0]).toBe('Nada reproduciéndose');
    expect(enabled('g-pause', facts(snapshot({ media: 'none' }))).enabled).toBe(false);
  });

  it('micrófono: no disponible si Windows no tiene; su estado silenciado se refleja', () => {
    expect(enabled('g-mic', facts(snapshot({ mic: { state: 'unavailable' } }))).blockers[0]).toBe('Micrófono no disponible');
    const f = facts(snapshot({ mic: { muted: true } }));
    expect(f['mic.muted']).toBe(true);
  });

  it('un PC sin mic/multimedia en la instantánea (versión anterior) los muestra como no disponibles', () => {
    const { mic: _m, media: _x, ...old } = snapshot();
    const parsed = snapshotSchema.parse(old);
    expect(parsed.mic.state).toBe('unavailable');
    expect(parsed.media.state).toBe('unavailable');
    expect(enabled('g-pause', facts(parsed)).enabled).toBe(false);
  });
});

describe('ADR-027: iconos de apps y «Activar sonido / micrófono»', () => {
  it('logotipo de la marca, mando para juegos y genérico si no se conoce', async () => {
    const { appIconName } = await import('../../src/core/appIcons');
    expect(appIconName({ id: 'steam', name: 'Steam' })).toBe('brand-steam');
    expect(appIconName({ id: 'discord', name: 'Discord' })).toBe('brand-discord');
    expect(appIconName({ id: 'claude', name: 'Claude' })).toBe('brand-claude');
    expect(appIconName({ id: 'forza-horizon-5', name: 'Forza Horizon 5' })).toBe('gamepad');
    expect(appIconName({ id: 'obs', name: 'OBS Studio' })).toBe('brand-obs');
    expect(appIconName({ id: 'cod', name: 'Call of Duty' })).toBe('gamepad');
    expect(appIconName({ id: 'gta-v', name: 'Grand Theft Auto V' })).toBe('gamepad');
    expect(appIconName({ id: 'helldivers', name: 'Algo raro 2', kind: 'game' })).toBe('gamepad');
    expect(appIconName({ id: 'explorer', name: 'Explorador de archivos' })).toBe('folder');
    expect(appIconName({ id: 'notas', name: 'Bloc de notas' })).toBe('app');
  });

  it('silenciado: «Activar sonido»; micrófono silenciado: «Activar micrófono»', () => {
    const muted = buildFacts({ pc: machine('PC_ONLINE'), link: link('READY'), snapshot: snapshot({ audio: { muted: true }, mic: { muted: true } }) });
    const normal = buildFacts({ pc: machine('PC_ONLINE'), link: link('READY'), snapshot: snapshot() });
    expect(resolveButton(btn(DEFAULT_DECK, 'g-mute-pc'), normal).name).toBe('Silenciar PC');
    expect(resolveButton(btn(DEFAULT_DECK, 'g-mute-pc'), muted)).toMatchObject({ name: 'Activar sonido', action: { type: 'audio.master.toggleMute' } });
    expect(resolveButton(btn(DEFAULT_DECK, 'g-mic'), normal).name).toBe('Silenciar micrófono');
    expect(resolveButton(btn(DEFAULT_DECK, 'g-mic'), muted).name).toBe('Activar micrófono');
  });

  it('migración v4 → v5: solo cambian los botones de silencio de General', () => {
    const v4 = structuredClone({ ...DEFAULT_DECK, schemaVersion: 4 }) as unknown as DeckConfig;
    delete btn(v4, 'g-mute-pc').alternate;
    delete btn(v4, 'g-mic').alternate;
    btn(v4, 'g-mic').icon = 'mic';
    const r = migrateDeck(v4);
    if (!r.ok) throw new Error(r.error);
    expect(btn(r.deck, 'g-mute-pc').alternate?.name).toBe('Activar sonido');
    expect(btn(r.deck, 'g-mic').alternate?.name).toBe('Activar micrófono');
    expect(r.deck.pages).toStrictEqual(v4.pages);
    for (const b of v4.buttons.filter((x) => !['g-mute-pc', 'g-mic'].includes(x.id))) expect(btn(r.deck, b.id)).toStrictEqual(b);
  });
});

describe('v6 → v7 (ADR-031): solo DaVinci y OBS', () => {
  it('DaVinci se rehace con secciones; OBS conserva la fila 1; el resto igual', () => {
    const v6 = structuredClone({ ...DEFAULT_DECK, schemaVersion: 6 }) as unknown as DeckConfig;
    const dv = v6.pages.find((p) => p.id === 'davinci')!;
    delete dv.sections;
    dv.slots = ['d-save', ...Array<null>(17).fill(null)];
    v6.buttons = v6.buttons.filter((b) => !b.id.startsWith('d-') || b.id === 'd-save');
    const obs = v6.pages.find((p) => p.id === 'obs')!;
    delete obs.dynamic;
    obs.slots[6] = 'scene-game';
    obs.slots[7] = 'scene-chat';
    const r = migrateDeck(v6);
    if (!r.ok) throw new Error(r.error);
    expect(r.deck.pages.find((p) => p.id === 'davinci')!.sections).toHaveLength(5);
    const o = r.deck.pages.find((p) => p.id === 'obs')!;
    expect(o.dynamic).toEqual({ kind: 'obsScenes', from: 6 });
    expect(o.slots[6]).toBeNull();
    expect(btn(r.deck, 'scene-game')).toBeDefined(); // los botones fijos siguen guardados
    for (const p of v6.pages.filter((x) => !['davinci', 'obs'].includes(x.id))) expect(r.deck.pages.find((x) => x.id === p.id)).toStrictEqual(p);
  });

  it('una sección que no ocupa las filas de arriba no es válida', () => {
    const bad = structuredClone(DEFAULT_DECK);
    bad.pages.find((p) => p.id === 'davinci')!.sections![0].slots = ['d-save'];
    expect(deckSchema.safeParse(bad).success).toBe(false);
  });
});
