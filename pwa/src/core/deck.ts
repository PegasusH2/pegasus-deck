// Modelo de botones y páginas (versión 7). Lo editará el editor visual (fase 4).
// Las páginas usan huecos fijos (ADR-010): un botón no disponible no mueve a los
// demás. El color es un nombre de la paleta del sistema de diseño, no un hex.

import { z } from 'zod';
import { allOf, evaluate, type Condition, type EvalResult } from './conditions';
import { getAction, validateParams } from './actions';
import { appIconName } from './appIcons';
import type { Facts } from './system';

export const DECK_SCHEMA_VERSION = 8;

export const PALETTE = ['green', 'red', 'blue', 'violet', 'purple', 'teal', 'amber', 'slate', 'pink', 'cyan'] as const;
export type ButtonColor = (typeof PALETTE)[number];

export const GRID_LIMITS = { cols: { min: 3, max: 8 }, rows: { min: 2, max: 5 } } as const;
export const DEFAULT_GRID = { cols: 6, rows: 3 } as const;

export const conditionSchema: z.ZodType<Condition> = z.lazy(() => z.union([
  z.object({ always: z.literal(true) }).strict(),
  z.object({ all: z.array(conditionSchema).max(20) }).strict(),
  z.object({ any: z.array(conditionSchema).max(20) }).strict(),
  z.object({ not: conditionSchema }).strict(),
  z.object({
    fact: z.string().min(1).max(140),
    eq: z.union([z.string().max(100), z.boolean()]).optional(),
    in: z.array(z.string().max(40)).max(10).optional(),
  }).strict(),
]));

const id = z.string().regex(/^[a-z0-9][a-z0-9-]{0,39}$/, 'Identificador no válido');
const paramValue = z.union([z.string().max(200), z.number(), z.boolean()]);

export const buttonSchema = z.object({
  id,
  name: z.string().trim().min(1).max(24),
  icon: z.string().max(30),
  color: z.enum(PALETTE),
  action: z.object({ type: z.string().min(1).max(60), params: z.record(paramValue) }).strict(),
  availability: conditionSchema,
  whenUnavailable: z.enum(['disable', 'hide']),
  confirm: z.object({ text: z.string().min(1).max(200), verb: z.string().min(1).max(20) }).strict().nullable(),
  /** null = el tiempo máximo de la acción. */
  timeoutMs: z.number().int().min(1000).max(120_000).nullable(),
  retry: z.object({ attempts: z.number().int().min(0).max(3), delayMs: z.number().int().min(0).max(10_000) }).strict(),
  feedback: z.object({ success: z.string().max(80).nullable() }).strict(),
  /** Texto pequeño bajo el nombre cuando el botón está disponible. */
  caption: z.string().max(24),
  /** v3: control especial en lugar de botón. 'slider' = deslizador de volumen (solo audio.master.setVolume). */
  widget: z.enum(['slider']).optional(),
  /**
   * v4 (ADR-024): mientras se cumple `when`, el botón muestra y hace esto otro (p. ej. «Encender PC»
   * pasa a «Apagar PC» con el PC encendido). Excepción expresa a «un botón no cambia de significado»:
   * cambian nombre, icono y color, y la variante destructiva siempre pide confirmación.
   */
  alternate: z.object({
    when: conditionSchema,
    name: z.string().trim().min(1).max(24),
    icon: z.string().max(30),
    color: z.enum(PALETTE),
    action: z.object({ type: z.string().min(1).max(60), params: z.record(paramValue) }).strict(),
    confirm: z.object({ text: z.string().min(1).max(200), verb: z.string().min(1).max(20) }).strict().nullable(),
    caption: z.string().max(24),
  }).strict().optional(),
}).strict();

export const pageSchema = z.object({
  id,
  name: z.string().trim().min(1).max(24),
  icon: z.string().max(30),
  grid: z.object({
    cols: z.number().int().min(GRID_LIMITS.cols.min).max(GRID_LIMITS.cols.max),
    rows: z.number().int().min(GRID_LIMITS.rows.min).max(GRID_LIMITS.rows.max),
  }).strict(),
  slots: z.array(id.nullable()),
  /** v3: huecos (índices) cuya app se elige con una pulsación larga. Solo los usa la página General. */
  appSlots: z.array(z.number().int().min(0).max(39)).max(40).optional(),
  /** v6: todas las casillas se configuran (pulsación larga) y se reordenan arrastrando. Solo General (ADR-029). */
  editable: z.boolean().optional(),
  /**
   * v7 (ADR-031): secciones. Las filas de arriba muestran la sección elegida; la primera casilla de la última
   * fila elige la sección y el resto de esa fila (page.slots) son controles fijos. Solo DaVinci.
   */
  sections: z.array(z.object({
    id, name: z.string().trim().min(1).max(24), icon: z.string().max(30), slots: z.array(id.nullable()),
  }).strict()).min(1).max(8).optional(),
  /** v7 (ADR-031): casillas desde `from` con botones generados (escenas de OBS de la colección activa). Solo OBS. */
  dynamic: z.object({ kind: z.literal('obsScenes'), from: z.number().int().min(0).max(39) }).strict().optional(),
}).strict();

export const deckSchema = z.object({
  schemaVersion: z.literal(DECK_SCHEMA_VERSION),
  pages: z.array(pageSchema).min(1).max(30),
  buttons: z.array(buttonSchema).max(500),
}).strict().superRefine((deck, ctx) => {
  const buttonIds = new Set<string>();
  deck.buttons.forEach((b, i) => {
    if (buttonIds.has(b.id)) ctx.addIssue({ code: 'custom', path: ['buttons', i, 'id'], message: `Botón duplicado: ${b.id}` });
    buttonIds.add(b.id);
    const check = validateParams(b.action.type, b.action.params);
    if (!check.ok) ctx.addIssue({ code: 'custom', path: ['buttons', i, 'action'], message: `«${b.name}»: ${check.error}` });
    const def = getAction(b.action.type);
    if (def && !def.idempotent && b.retry.attempts > 0) {
      ctx.addIssue({ code: 'custom', path: ['buttons', i, 'retry'], message: `«${b.name}»: esta acción no admite reintentos automáticos` });
    }
    if (b.alternate) {
      const alt = validateParams(b.alternate.action.type, b.alternate.action.params);
      if (!alt.ok) ctx.addIssue({ code: 'custom', path: ['buttons', i, 'alternate'], message: `«${b.name}»: ${alt.error}` });
    }
    if (b.widget === 'slider' && b.action.type !== 'audio.master.setVolume') {
      ctx.addIssue({ code: 'custom', path: ['buttons', i, 'widget'], message: `«${b.name}»: el deslizador solo sirve para el volumen` });
    }
  });
  const byId = new Map(deck.buttons.map((b) => [b.id, b]));
  const pageIds = new Set<string>();
  deck.pages.forEach((p, i) => {
    if (pageIds.has(p.id)) ctx.addIssue({ code: 'custom', path: ['pages', i, 'id'], message: `Página duplicada: ${p.id}` });
    pageIds.add(p.id);
    if (p.slots.length !== p.grid.cols * p.grid.rows) {
      ctx.addIssue({ code: 'custom', path: ['pages', i, 'slots'], message: `La página «${p.name}» no tiene ${p.grid.cols}×${p.grid.rows} huecos` });
    }
    p.slots.forEach((s, j) => {
      if (s && !buttonIds.has(s)) ctx.addIssue({ code: 'custom', path: ['pages', i, 'slots', j], message: `La página «${p.name}» usa un botón inexistente: ${s}` });
    });
    (p.sections ?? []).forEach((sec, k) => {
      if (sec.slots.length !== p.grid.cols * (p.grid.rows - 1)) {
        ctx.addIssue({ code: 'custom', path: ['pages', i, 'sections', k], message: `La sección «${sec.name}» no ocupa las filas de arriba` });
      }
      sec.slots.forEach((s, j) => {
        if (s && !buttonIds.has(s)) ctx.addIssue({ code: 'custom', path: ['pages', i, 'sections', k, 'slots', j], message: `La sección «${sec.name}» usa un botón inexistente: ${s}` });
      });
    });
    (p.editable ? [] : p.appSlots ?? []).forEach((slot, j) => {
      const b = slot < p.slots.length ? p.slots[slot] : undefined;
      if (b === undefined || (p.appSlots ?? []).indexOf(slot) !== j) {
        ctx.addIssue({ code: 'custom', path: ['pages', i, 'appSlots', j], message: `La página «${p.name}» tiene un hueco de app no válido: ${slot}` });
      } else if (b !== null && byId.get(b)?.action.type !== 'app.open') {
        ctx.addIssue({ code: 'custom', path: ['pages', i, 'appSlots', j], message: `En «${p.name}», el hueco ${slot + 1} solo puede abrir una aplicación` });
      }
    });
  });
});

export type DeckButton = z.infer<typeof buttonSchema>;
export type DeckPage = z.infer<typeof pageSchema>;
export type DeckConfig = z.infer<typeof deckSchema>;

/** El botón tal como se ve ahora: su variante si se cumple su condición (ADR-024). */
export function resolveButton(button: DeckButton, facts: Facts): DeckButton {
  const alt = button.alternate;
  if (!alt || evaluate(alt.when, facts).value !== true) return button;
  const { when: _when, ...shown } = alt;
  return { ...button, ...shown, alternate: undefined };
}

export interface ButtonAvailability extends EvalResult {
  enabled: boolean;
  /** Condición efectiva: requisitos de la acción + condición del botón. */
  condition: Condition;
}

export function effectiveCondition(button: DeckButton): Condition {
  const def = getAction(button.action.type);
  if (!def) return { not: { always: true } };
  return allOf(def.requires(button.action.params), button.availability);
}

export function buttonAvailability(button: DeckButton, facts: Facts): ButtonAvailability {
  const condition = effectiveCondition(button);
  const result = evaluate(condition, facts);
  if (!getAction(button.action.type)) {
    return { ...result, value: false, enabled: false, condition, missing: [`Acción desconocida: ${button.action.type}`], blockers: ['Acción desconocida'] };
  }
  return { ...result, enabled: result.value === true, condition };
}

/** Tiempo máximo efectivo de un botón. */
export function buttonTimeoutMs(button: DeckButton): number {
  return button.timeoutMs ?? getAction(button.action.type)?.timeoutMs ?? 8000;
}

// ---------------------------------------------------------------------------
// Configuración inicial (equivale al mockup)

type ButtonInit = Partial<DeckButton> & Pick<DeckButton, 'id' | 'name' | 'icon' | 'color' | 'action'>;

export function newButton(b: ButtonInit): DeckButton {
  return {
    availability: { always: true },
    whenUnavailable: 'disable',
    confirm: null,
    timeoutMs: null,
    retry: { attempts: 0, delayMs: 0 },
    feedback: { success: null },
    caption: '',
    ...b,
  };
}

export function pageWithSlots(p: Omit<DeckPage, 'slots' | 'grid'> & { grid?: DeckPage['grid'] }, buttonIds: string[]): DeckPage {
  const grid = p.grid ?? { ...DEFAULT_GRID };
  const size = grid.cols * grid.rows;
  const slots: (string | null)[] = buttonIds.slice(0, size);
  while (slots.length < size) slots.push(null);
  return { ...p, grid, slots };
}

/**
 * v3/v4 → v5: variantes de los controles fijos de General (Activar sonido, Activar micrófono).
 * Solo toca esos botones y solo si aún no tienen variante. Nada más cambia.
 */
export function upgradePowerButton(buttons: DeckButton[]): DeckButton[] {
  return buttons.map((b) => {
    if (b.alternate) return b;
    if (b.id === 'g-mute-pc' && b.action.type === 'audio.master.toggleMute') return { ...b, alternate: SOUND_ON };
    if (b.id === 'g-mic' && b.action.type === 'audio.mic.toggleMute') return { ...b, icon: 'mic-off', alternate: MIC_ON };
    return b;
  });
}

/** Silenciado: el botón pasa a «Activar sonido» (ADR-027). */
export const SOUND_ON: NonNullable<DeckButton['alternate']> = {
  when: { fact: 'audio.muted' },
  name: 'Activar sonido',
  icon: 'volume',
  color: 'green',
  action: { type: 'audio.master.toggleMute', params: {} },
  confirm: null,
  caption: 'Audio de Windows',
};

/** Micrófono silenciado: el botón pasa a «Activar micrófono» (ADR-027). */
export const MIC_ON: NonNullable<DeckButton['alternate']> = {
  when: { fact: 'mic.muted' },
  name: 'Activar micrófono',
  icon: 'mic',
  color: 'green',
  action: { type: 'audio.mic.toggleMute', params: {} },
  confirm: null,
  caption: 'Micrófono',
};

type DeckBody = Omit<DeckConfig, 'schemaVersion'>;

/** Panel de fábrica de la v2. La v3 solo cambia General (upgradeGeneral). Exportado para las pruebas de migración. */
export const DEFAULT_DECK_V2: DeckBody = {
  pages: [
    pageWithSlots({ id: 'general', name: 'General', icon: 'home' },
      ['check', 'settings', 'open-obs', 'rec-start', 'rec-pause', 'open-chrome', 'open-davinci', 'mic-mute', 'scene-game', 'open-explorer', 'lock']),
    pageWithSlots({ id: 'obs', name: 'OBS', icon: 'obs' },
      ['stream-start', 'stream-stop', 'rec-start', 'rec-stop', 'rec-pause', 'mic-mute', 'scene-game', 'scene-chat']),
    pageWithSlots({ id: 'apps', name: 'Aplicaciones', icon: 'grid' },
      ['open-obs', 'open-chrome', 'open-davinci', 'open-explorer']),
    pageWithSlots({ id: 'audio', name: 'Audio', icon: 'volume' },
      ['audio-mute', 'vol-25', 'vol-50', 'vol-75', 'vol-100', 'mic-mute']),
  ],
  buttons: [
    newButton({ id: 'check', name: 'Comprobar estado', icon: 'monitor', color: 'slate', action: { type: 'system.check', params: {} }, caption: 'Siempre' }),
    newButton({ id: 'settings', name: 'Configuración', icon: 'settings', color: 'slate', action: { type: 'local.settings', params: {} }, caption: 'Siempre' }),
    newButton({ id: 'open-obs', name: 'Abrir OBS', icon: 'obs', color: 'purple', action: { type: 'app.open', params: { appId: 'obs' } },
      availability: { fact: 'app.obs.state', in: ['installed'] }, caption: 'Aplicación', feedback: { success: 'OBS abierto' } }),
    newButton({ id: 'rec-start', name: 'Iniciar grabación', icon: 'record', color: 'red', action: { type: 'obs.recording.start', params: {} },
      availability: { not: { fact: 'obs.recording' } }, caption: 'OBS', feedback: { success: 'Grabando' } }),
    newButton({ id: 'rec-stop', name: 'Detener grabación', icon: 'stop', color: 'red', action: { type: 'obs.recording.stop', params: {} },
      availability: { fact: 'obs.recording' }, caption: 'OBS', confirm: { text: '¿Detener la grabación en curso?', verb: 'Detener' } }),
    newButton({ id: 'rec-pause', name: 'Pausar grabación', icon: 'pause', color: 'slate', action: { type: 'obs.recording.pause', params: {} },
      availability: { fact: 'obs.recording' }, caption: 'OBS' }),
    newButton({ id: 'stream-start', name: 'Iniciar streaming', icon: 'radio', color: 'green', action: { type: 'obs.streaming.start', params: {} },
      availability: { not: { fact: 'obs.streaming' } }, caption: 'OBS', confirm: { text: '¿Empezar a emitir en directo?', verb: 'Emitir' } }),
    newButton({ id: 'stream-stop', name: 'Detener streaming', icon: 'stop', color: 'red', action: { type: 'obs.streaming.stop', params: {} },
      availability: { fact: 'obs.streaming' }, caption: 'OBS', confirm: { text: '¿Terminar la emisión en directo?', verb: 'Terminar' } }),
    newButton({ id: 'open-chrome', name: 'Abrir Chrome', icon: 'chrome', color: 'blue', action: { type: 'app.open', params: { appId: 'chrome' } }, caption: 'Aplicación' }),
    newButton({ id: 'open-davinci', name: 'Abrir DaVinci', icon: 'davinci', color: 'teal', action: { type: 'app.open', params: { appId: 'davinci' } }, caption: 'Aplicación' }),
    newButton({ id: 'mic-mute', name: 'Silenciar micro', icon: 'mic', color: 'violet', action: { type: 'obs.input.toggleMute', params: { input: 'Mic/Aux' } }, caption: 'OBS' }),
    newButton({ id: 'scene-game', name: 'Escena Juego', icon: 'scene', color: 'blue', action: { type: 'obs.scene.set', params: { scene: 'Juego' } },
      availability: { not: { fact: 'obs.scene', eq: 'Juego' } }, caption: 'OBS' }),
    newButton({ id: 'scene-chat', name: 'Escena Charla', icon: 'scene', color: 'blue', action: { type: 'obs.scene.set', params: { scene: 'Charla' } },
      availability: { not: { fact: 'obs.scene', eq: 'Charla' } }, caption: 'OBS' }),
    newButton({ id: 'open-explorer', name: 'Explorador', icon: 'folder', color: 'amber', action: { type: 'app.open', params: { appId: 'explorer' } }, caption: 'Aplicación' }),
    newButton({ id: 'audio-mute', name: 'Silenciar PC', icon: 'volume-off', color: 'violet', action: { type: 'audio.master.toggleMute', params: {} }, caption: 'Audio de Windows' }),
    newButton({ id: 'vol-25', name: 'Volumen 25 %', icon: 'volume-low', color: 'slate', action: { type: 'audio.master.setVolume', params: { percent: 25 } }, caption: 'Audio de Windows' }),
    newButton({ id: 'vol-50', name: 'Volumen 50 %', icon: 'volume-low', color: 'slate', action: { type: 'audio.master.setVolume', params: { percent: 50 } }, caption: 'Audio de Windows' }),
    newButton({ id: 'vol-75', name: 'Volumen 75 %', icon: 'volume', color: 'slate', action: { type: 'audio.master.setVolume', params: { percent: 75 } }, caption: 'Audio de Windows' }),
    newButton({ id: 'vol-100', name: 'Volumen 100 %', icon: 'volume', color: 'slate', action: { type: 'audio.master.setVolume', params: { percent: 100 } }, caption: 'Audio de Windows' }),
    newButton({ id: 'lock', name: 'Bloquear Windows', icon: 'lock', color: 'slate', action: { type: 'windows.lock', params: {} },
      confirm: { text: '¿Bloquear la sesión de Windows?', verb: 'Bloquear' }, caption: 'Windows' }),
  ],
};

// ---------------------------------------------------------------------------
// Página General (v3, ADR-022): 6 controles del PC fijos + 6 accesos a apps que
// se eligen con una pulsación larga. Solo afecta a la página 'general': las
// demás páginas y sus botones no se tocan (los botones de General tienen ids
// propios, prefijo «g-», aunque hagan lo mismo que otros).

export const GENERAL_PAGE_ID = 'general';
/** Huecos de las apps configurables (segunda fila de la cuadrícula 6×3). */
export const GENERAL_APP_SLOTS: readonly number[] = [6, 7, 8, 9, 10, 11];

export const GENERAL_NATIVE_BUTTONS: readonly DeckButton[] = [
  newButton({ id: 'g-mic', name: 'Silenciar micrófono', icon: 'mic-off', color: 'violet', action: { type: 'audio.mic.toggleMute', params: {} },
    caption: 'Micrófono', alternate: MIC_ON }),
  newButton({ id: 'g-volume', name: 'Volumen', icon: 'volume', color: 'slate', action: { type: 'audio.master.setVolume', params: { percent: 50 } },
    caption: 'Audio de Windows', widget: 'slider' }),
  newButton({ id: 'g-mute-pc', name: 'Silenciar PC', icon: 'volume-off', color: 'violet', action: { type: 'audio.master.toggleMute', params: {} },
    caption: 'Audio de Windows', alternate: SOUND_ON }),
  newButton({ id: 'g-pause', name: 'Pausar', icon: 'pause', color: 'amber', action: { type: 'media.pause', params: {} },
    caption: 'Multimedia' }),
  newButton({ id: 'g-play', name: 'Reanudar', icon: 'play', color: 'green', action: { type: 'media.play', params: {} },
    caption: 'Multimedia' }),
];

const APP_COLORS: readonly ButtonColor[] = ['purple', 'blue', 'teal', 'amber', 'cyan', 'pink'];

export function generalAppButtonId(slot: number): string {
  return `g-app-${slot}`;
}

/** Botón de un hueco de app de General. Con `base`, copia su aspecto (migración). */
export function generalAppButton(slot: number, app: { id: string; name: string; kind?: 'game' }, base?: DeckButton, id = generalAppButtonId(slot)): DeckButton {
  if (base) return { ...base, id, action: { type: 'app.open', params: { appId: app.id } } };
  return newButton({
    id,
    // «Abrir X» si cabe; si no, solo el nombre (sin cortar a media palabra si se puede evitar).
    name: (`Abrir ${app.name}`.length <= 24 ? `Abrir ${app.name}` : app.name.slice(0, 24)).trim(),
    icon: appIconName(app),
    color: APP_COLORS[slot % APP_COLORS.length] ?? 'slate',
    action: { type: 'app.open', params: { appId: app.id } },
    caption: 'Aplicación',
  });
}

/** Lo que ya hacen los controles fijos (p. ej. el antiguo «Encender PC»): no se avisa de que desaparezca. */
const NATIVE_TYPES = new Set(GENERAL_NATIVE_BUTTONS.map((b) => b.action.type));
const GENERAL_IDS = new Set([...GENERAL_NATIVE_BUTTONS.map((b) => b.id), ...GENERAL_APP_SLOTS.map(generalAppButtonId)]);

/**
 * Convierte la página General al formato v3. Conserva su cuadrícula, sus huecos a
 * partir del 12 y, en los huecos de apps, las apps que ya tenía (en orden). Las
 * demás páginas y los botones existentes se devuelven tal cual (mismos objetos).
 */
export function upgradeGeneral(deck: DeckBody, warnings: string[] = []): DeckBody {
  const index = deck.pages.findIndex((p) => p.id === GENERAL_PAGE_ID);
  if (index < 0) return deck;
  const page = deck.pages[index];
  const byId = new Map(deck.buttons.map((b) => [b.id, b]));

  const apps = page.slots
    .map((id) => (id ? byId.get(id) : undefined))
    .filter((b): b is DeckButton => b?.action.type === 'app.open')
    .slice(0, GENERAL_APP_SLOTS.length);

  let grid = page.grid;
  let slots = [...page.slots];
  if (grid.cols * grid.rows < 12) {
    grid = { ...DEFAULT_GRID };
    slots = Array.from({ length: grid.cols * grid.rows }, () => null);
    warnings.push('La página General era demasiado pequeña: ahora tiene 6×3 huecos.');
  }
  // Primera fila: solo los controles fijos (el resto de la fila queda libre).
  for (let i = 0; i < GENERAL_APP_SLOTS[0]; i++) slots[i] = null;
  GENERAL_NATIVE_BUTTONS.forEach((b, i) => { slots[i] = b.id; });
  const appButtons = GENERAL_APP_SLOTS.map((slot, i) => {
    const base = apps[i];
    slots[slot] = base ? generalAppButtonId(slot) : null;
    return base ? generalAppButton(slot, { id: String(base.action.params.appId), name: base.name }, base) : null;
  }).filter((b): b is DeckButton => b !== null);

  // Botones que salen de General y no están en otra página: siguen guardados, pero se avisa.
  const elsewhere = new Set(deck.pages.filter((_, i) => i !== index).flatMap((p) => p.slots));
  const removed = page.slots
    .filter((id): id is string => !!id && !slots.includes(id) && !elsewhere.has(id) && !GENERAL_IDS.has(id))
    .map((id) => byId.get(id))
    .filter((b): b is DeckButton => !!b && b.action.type !== 'app.open' && !NATIVE_TYPES.has(b.action.type));
  if (removed.length > 0) {
    warnings.push(`General ahora tiene 6 controles del PC y 6 accesos a apps. Ya no aparecen en ninguna página: ${removed.map((b) => `«${b.name}»`).join(', ')} (siguen guardados en la configuración).`);
  }

  const pages = [...deck.pages];
  pages[index] = { ...page, grid, slots, appSlots: [...GENERAL_APP_SLOTS] };
  return {
    pages,
    buttons: [...deck.buttons.filter((b) => !GENERAL_IDS.has(b.id)), ...GENERAL_NATIVE_BUTTONS, ...appButtons],
  };
}

// ---------------------------------------------------------------------------
// v6 (ADR-028/029): pestaña DaVinci; General con las 18 casillas configurables y
// reordenables; «Pausar» y «Reanudar» en un único botón según el estado real.

/** Con algo en pausa o detenido, «Pausar» pasa a «Reanudar». */
export const RESUME: NonNullable<DeckButton['alternate']> = {
  when: { fact: 'media.state', in: ['paused', 'stopped'] },
  name: 'Reanudar',
  icon: 'play',
  color: 'green',
  action: { type: 'media.play', params: {} },
  confirm: null,
  caption: 'Multimedia',
};

/** Controles del PC que se pueden poner en cualquier casilla de General (pulsación larga). */
export const GENERAL_CONTROL_IDS: readonly string[] = ['g-mic', 'g-volume', 'g-mute-pc', 'g-pause'];

export const DAVINCI_PAGE_ID = 'davinci';

function dv(id: string, name: string, command: string, icon: string, color: ButtonColor, caption: string): DeckButton {
  return newButton({ id: `d-${id}`, name, icon, color, action: { type: 'davinci.command', params: { command } }, caption });
}

/** v7 (ADR-031): órdenes de DaVinci agrupadas por secciones; los atajos son los del Resolve del usuario. */
export const DAVINCI_BUTTONS: readonly DeckButton[] = [
  // Páginas
  dv('page-media', 'Media', 'page.media', 'folder', 'blue', 'Página'),
  dv('page-cut', 'Cortar', 'page.cut', 'scissors', 'blue', 'Página'),
  dv('page-edit', 'Edición', 'page.edit', 'film', 'blue', 'Página'),
  dv('page-fusion', 'Fusion', 'page.fusion', 'node-parallel', 'purple', 'Página'),
  dv('page-color', 'Color', 'page.color', 'palette', 'purple', 'Página'),
  dv('page-fairlight', 'Fairlight', 'page.fairlight', 'volume', 'teal', 'Página'),
  dv('page-deliver', 'Entrega', 'page.deliver', 'rocket', 'amber', 'Página'),
  // Edición
  dv('blade', 'Cortar en el cursor', 'edit.blade', 'scissors', 'red', 'Edición'),
  dv('delete', 'Eliminar', 'edit.delete', 'stop', 'red', 'Edición'),
  dv('mark-in', 'Marcar entrada', 'edit.markIn', 'skip-back', 'slate', 'Edición'),
  dv('mark-out', 'Marcar salida', 'edit.markOut', 'skip-forward', 'slate', 'Edición'),
  dv('snapping', 'Ajuste magnético', 'edit.snapping', 'grid', 'slate', 'Edición'),
  dv('insert', 'Insertar', 'edit.insert', 'copy', 'blue', 'Edición'),
  dv('overwrite', 'Sobrescribir', 'edit.overwrite', 'copy', 'amber', 'Edición'),
  dv('undo', 'Deshacer', 'edit.undo', 'undo', 'slate', 'Edición'),
  dv('redo', 'Rehacer', 'edit.redo', 'redo', 'slate', 'Edición'),
  // Color
  dv('serial', 'Nodo en serie', 'color.addSerialNode', 'node-serial', 'violet', 'Color'),
  dv('parallel', 'Nodo en paralelo', 'color.addParallelNode', 'node-parallel', 'violet', 'Color'),
  dv('layer', 'Nodo de capa', 'color.addLayerNode', 'columns', 'violet', 'Color'),
  dv('toggle-node', 'Activar/desactivar nodo', 'color.toggleNode', 'toggle', 'slate', 'Color'),
  dv('toggle-all', 'Activar/desactivar nodos', 'color.toggleAllNodes', 'toggle', 'slate', 'Color'),
  dv('bypass', 'Desactivar correcciones', 'color.bypassAll', 'eye-off', 'amber', 'Color'),
  dv('still', 'Capturar fotograma', 'color.grabStill', 'camera', 'blue', 'Color'),
  dv('split', 'Pantalla dividida', 'color.splitScreen', 'columns', 'blue', 'Color'),
  dv('highlight', 'Resaltar', 'color.highlight', 'highlighter', 'teal', 'Color'),
  dv('copy-prev', 'Corrección anterior', 'color.copyPrevious', 'copy', 'teal', 'Color'),
  dv('reset', 'Restablecer corrección', 'color.resetGrade', 'undo', 'red', 'Color'),
  // Entrega
  dv('export', 'Exportar proyecto', 'project.export', 'save', 'amber', 'Entrega'),
  // Photo
  dv('like', 'Like', 'photo.like', 'thumbs-up', 'green', 'Photo'),
  dv('dislike', 'Dislike', 'photo.dislike', 'thumbs-down', 'red', 'Photo'),
  // Fila fija
  dv('marker', 'Marcador', 'edit.marker', 'bookmark', 'amber', 'Global'),
  dv('prev', 'Clip anterior', 'clip.previous', 'skip-back', 'slate', 'Global'),
  dv('next', 'Clip siguiente', 'clip.next', 'skip-forward', 'slate', 'Global'),
  dv('play', 'Reproducir / pausar', 'playback.toggle', 'play', 'green', 'Global'),
  dv('save', 'Guardar', 'project.save', 'save', 'green', 'Global'),
];

const top = (ids: string[]): (string | null)[] => {
  const slots: (string | null)[] = ids.map((x) => `d-${x}`);
  while (slots.length < 12) slots.push(null);
  return slots;
};

export const DAVINCI_SECTIONS: NonNullable<DeckPage['sections']> = [
  { id: 'pages', name: 'Páginas', icon: 'grid', slots: top(['page-media', 'page-cut', 'page-edit', 'page-fusion', 'page-color', 'page-fairlight', 'page-deliver']) },
  { id: 'edit', name: 'Edición', icon: 'film', slots: top(['blade', 'delete', 'mark-in', 'mark-out', 'snapping', 'insert', 'overwrite', 'undo', 'redo']) },
  { id: 'color', name: 'Color', icon: 'palette', slots: top(['serial', 'parallel', 'layer', 'toggle-node', 'toggle-all', 'bypass', 'still', 'split', 'highlight', 'copy-prev', 'reset']) },
  { id: 'deliver', name: 'Entrega', icon: 'rocket', slots: top(['page-deliver', 'export']) },
  { id: 'photo', name: 'Photo', icon: 'camera', slots: top(['like', 'dislike']) },
];

export const DAVINCI_PAGE: DeckPage = {
  id: DAVINCI_PAGE_ID, name: 'DaVinci', icon: 'davinci', grid: { cols: 6, rows: 3 },
  // Fila fija: la casilla 12 elige la sección (no es un botón); el resto, controles globales.
  slots: [...Array<null>(13).fill(null), 'd-marker', 'd-prev', 'd-next', 'd-play', 'd-save'],
  sections: DAVINCI_SECTIONS,
};

/** Primera casilla de la última fila de una página con secciones (la que elige la sección). */
export function sectionSelectorSlot(page: DeckPage): number {
  return page.grid.cols * (page.grid.rows - 1);
}

/** Botón que se ve en una casilla de una página con secciones. */
export function sectionSlot(page: DeckPage, sectionId: string | undefined, slot: number): string | null {
  if (!page.sections) return page.slots[slot] ?? null;
  const split = sectionSelectorSlot(page);
  if (slot >= split) return page.slots[slot] ?? null;
  const section = page.sections.find((x) => x.id === sectionId) ?? page.sections[0];
  return section.slots[slot] ?? null;
}

/** Escenas de OBS que caben en las casillas dinámicas de una página. */
export function dynamicSceneCount(page: DeckPage): number {
  return page.dynamic ? page.slots.length - page.dynamic.from : 0;
}

/** Botón generado para una escena de OBS (no se guarda: sale de las escenas reales de OBS). */
export function obsSceneButton(slot: number, scene: string): DeckButton {
  return newButton({
    id: `obs-scene-${slot}`, name: scene, icon: 'scene', color: 'blue',
    action: { type: 'obs.scene.set', params: { scene } }, caption: 'Escena',
  });
}

/**
 * v6 → v7 (ADR-031). DaVinci: secciones y fila fija (la pestaña no es editable, se rehace entera con las
 * órdenes nuevas). OBS: la fila 1 se conserva y desde la casilla 6 se muestran las escenas reales de OBS.
 * Las demás páginas y botones no cambian (los de escena fijos siguen guardados).
 */
export function upgradeV7(deck: DeckBody): DeckBody {
  let pages = [...deck.pages];
  let buttons = [...deck.buttons];
  const di = pages.findIndex((p) => p.id === DAVINCI_PAGE_ID);
  if (di >= 0 && !pages[di].sections) {
    pages[di] = DAVINCI_PAGE;
    buttons = [...buttons.filter((b) => !b.id.startsWith('d-')), ...DAVINCI_BUTTONS];
  }
  const oi = pages.findIndex((p) => p.id === 'obs');
  if (oi >= 0 && !pages[oi].dynamic && pages[oi].slots.length > 6) {
    const o = pages[oi];
    pages[oi] = { ...o, slots: o.slots.map((x, i) => (i < 6 ? x : null)), dynamic: { kind: 'obsScenes', from: 6 } };
  }
  return { pages, buttons };
}

/**
 * v5 → v6. Solo General y una pestaña nueva: «Pausar» gana la variante «Reanudar» y la casilla del antiguo
 * «Reanudar» queda libre (el botón sigue guardado); General pasa a editable; se añade la pestaña DaVinci
 * (detrás de OBS) si no existe. Las demás páginas y botones no cambian.
 */
export function upgradeV6(deck: DeckBody): DeckBody {
  let buttons = deck.buttons.map((b) => (b.id === 'g-pause' && b.action.type === 'media.pause' && !b.alternate ? { ...b, alternate: RESUME } : b));
  let pages = [...deck.pages];
  const gi = pages.findIndex((p) => p.id === GENERAL_PAGE_ID);
  if (gi >= 0) {
    const g = pages[gi];
    const hasPause = g.slots.includes('g-pause');
    pages[gi] = { ...g, slots: g.slots.map((id) => (id === 'g-play' && hasPause ? null : id)), editable: true };
  }
  if (!pages.some((p) => p.id === DAVINCI_PAGE_ID)) {
    buttons = [...buttons, ...DAVINCI_BUTTONS.filter((b) => !buttons.some((x) => x.id === b.id))];
    const at = pages.findIndex((p) => p.id === 'obs');
    pages.splice(at >= 0 ? at + 1 : pages.length, 0, DAVINCI_PAGE);
  }
  return { pages, buttons };
}

// ---------------------------------------------------------------------------
// v8 (ADR-032): Pegasus Deck ya no enciende ni apaga el PC. Las acciones retiradas desaparecen de la
// configuración guardada o importada: sus botones se borran y sus casillas quedan vacías (nada se mueve).

export const RETIRED_ACTION_TYPES: ReadonlySet<string> = new Set(['system.wake', 'system.shutdown', 'system.restart']);

const isObject = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v);
const retiredType = (action: unknown): boolean => isObject(action) && RETIRED_ACTION_TYPES.has(String(action.type));

/** Sobre la configuración aún sin validar (cualquier versión): quita los botones de acciones retiradas. */
export function removeRetiredActions(raw: unknown): unknown {
  if (!isObject(raw) || !Array.isArray(raw.buttons)) return raw;
  const gone = new Set<string>();
  const buttons = raw.buttons.flatMap((b: unknown) => {
    if (!isObject(b)) return [b];
    if (retiredType(b.action)) { gone.add(String(b.id)); return []; }
    if (isObject(b.alternate) && retiredType(b.alternate.action)) {
      const { alternate: _drop, ...rest } = b;
      return [rest];
    }
    return [b];
  });
  if (gone.size === 0 && buttons.every((b, i) => b === (raw.buttons as unknown[])[i])) return raw;
  const clear = (slots: unknown): unknown => (Array.isArray(slots) ? slots.map((x) => (typeof x === 'string' && gone.has(x) ? null : x)) : slots);
  const pages = Array.isArray(raw.pages) ? raw.pages.map((p: unknown) => {
    if (!isObject(p)) return p;
    const page: Record<string, unknown> = { ...p };
    if ('slots' in page) page.slots = clear(page.slots);
    if (Array.isArray(page.buttonIds)) page.buttonIds = page.buttonIds.filter((x) => !(typeof x === 'string' && gone.has(x))); // v1
    if (Array.isArray(page.sections)) page.sections = page.sections.map((sec: unknown) => (isObject(sec) ? { ...sec, slots: clear(sec.slots) } : sec));
    return page;
  }) : raw.pages;
  return { ...raw, pages, buttons };
}

const v5 = upgradeGeneral(DEFAULT_DECK_V2);
export const DEFAULT_DECK: DeckConfig = { schemaVersion: DECK_SCHEMA_VERSION, ...upgradeV7(upgradeV6({ ...v5, buttons: upgradePowerButton(v5.buttons) })) };

/** ¿Se puede configurar esta casilla de esta página? */
export function isConfigurableSlot(page: DeckPage, slot: number): boolean {
  return slot >= 0 && slot < page.slots.length && (page.editable === true || (page.appSlots ?? []).includes(slot));
}

export type SlotChoice = { app: { id: string; name: string; kind?: 'game' } } | { control: string } | null;

function nextAppId(buttons: readonly DeckButton[]): string {
  const used = new Set(buttons.map((b) => b.id));
  for (let n = 0; ; n++) if (!used.has(generalAppButtonId(n))) return generalAppButtonId(n);
}

/**
 * Pone en una casilla de General una app autorizada, un control del PC (que no esté ya en la página) o
 * nada. El acceso a app que hubiera se borra; un control solo se quita de la casilla (sigue guardado y se
 * puede volver a poner). Devuelve null si no es posible.
 */
export function assignGeneralSlot(deck: DeckConfig, slot: number, choice: SlotChoice): DeckConfig | null {
  const index = deck.pages.findIndex((p) => p.id === GENERAL_PAGE_ID);
  const page = deck.pages[index];
  if (!page || !isConfigurableSlot(page, slot)) return null;
  const previous = page.slots[slot];
  let buttons = previous?.startsWith('g-app-') ? deck.buttons.filter((b) => b.id !== previous) : [...deck.buttons];
  const slots = [...page.slots];
  if (choice === null) {
    slots[slot] = null;
  } else if ('control' in choice) {
    if (!GENERAL_CONTROL_IDS.includes(choice.control) || slots.includes(choice.control)) return null;
    if (!buttons.some((b) => b.id === choice.control)) {
      const control = GENERAL_NATIVE_BUTTONS.find((b) => b.id === choice.control);
      if (!control) return null;
      buttons.push(choice.control === 'g-pause' ? { ...control, alternate: RESUME } : control);
    }
    slots[slot] = choice.control;
  } else {
    const id = nextAppId(buttons);
    buttons.push(generalAppButton(slot, choice.app, undefined, id));
    slots[slot] = id;
  }
  const pages = [...deck.pages];
  pages[index] = { ...page, slots };
  const next: DeckConfig = { ...deck, pages, buttons };
  return deckSchema.safeParse(next).success ? next : null;
}

/** Compatibilidad: asigna (o vacía) un acceso a app. */
export function assignGeneralApp(deck: DeckConfig, slot: number, app: { id: string; name: string; kind?: 'game' } | null): DeckConfig | null {
  return assignGeneralSlot(deck, slot, app ? { app } : null);
}

/** Arrastrar y soltar en General: intercambia dos casillas (nada se duplica ni se pierde). */
export function moveGeneralSlot(deck: DeckConfig, from: number, to: number): DeckConfig | null {
  const index = deck.pages.findIndex((p) => p.id === GENERAL_PAGE_ID);
  const page = deck.pages[index];
  if (!page?.editable || from === to || !isConfigurableSlot(page, from) || !isConfigurableSlot(page, to)) return null;
  const slots = [...page.slots];
  [slots[from], slots[to]] = [slots[to], slots[from]];
  const pages = [...deck.pages];
  pages[index] = { ...page, slots };
  return { ...deck, pages };
}
