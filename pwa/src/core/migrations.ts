// Migraciones de la configuración del panel. Funciones puras vN → vN+1.
// Regla: nunca se descarta una página o un botón del usuario en silencio; si
// algo no cabe se crea una página adicional y se avisa. v2 → v3 solo reescribe
// la página General (ADR-022): los botones que salen de ella siguen guardados y
// se avisa de los que ya no aparecen en ninguna página.

import { z } from 'zod';
import {
  DECK_SCHEMA_VERSION,
  DEFAULT_GRID,
  deckSchema,
  GRID_LIMITS,
  newButton,
  type ButtonColor,
  type DeckConfig,
  type DeckPage,
  upgradeGeneral,
  upgradePowerButton,
  upgradeV6,
  upgradeV7,
  removeRetiredActions,
} from './deck';

// --- Versión 1 (fase 1 inicial) -------------------------------------------

const v1Button = z.object({
  id: z.string(),
  name: z.string(),
  icon: z.string(),
  color: z.string(),
  action: z.object({ type: z.string(), params: z.record(z.union([z.string(), z.number(), z.boolean()])) }),
  availability: z.unknown(),
  whenUnavailable: z.enum(['disable', 'hide']),
  confirm: z.object({ text: z.string() }).nullable(),
  caption: z.string(),
});
const v1Deck = z.object({
  schemaVersion: z.literal(1),
  pages: z.array(z.object({ id: z.string(), name: z.string(), icon: z.string(), buttonIds: z.array(z.string()) })),
  buttons: z.array(v1Button),
});

/** Colores hex de la v1 → nombre de la paleta. */
const V1_COLORS: Record<string, ButtonColor> = {
  '#1f9d55': 'green', '#2b3a5c': 'slate', '#6d3fd1': 'purple', '#b4233a': 'red',
  '#2563eb': 'blue', '#0f8f7a': 'teal', '#7c3aed': 'violet', '#b7791f': 'amber',
};

function v1ToV2(old: z.infer<typeof v1Deck>, warnings: string[]): unknown {
  const buttons = old.buttons.map((b) => newButton({
    id: b.id,
    name: b.name.slice(0, 24),
    icon: b.icon,
    color: V1_COLORS[b.color.toLowerCase()] ?? 'slate',
    action: b.action,
    availability: b.availability as DeckConfig['buttons'][number]['availability'],
    whenUnavailable: b.whenUnavailable,
    confirm: b.confirm ? { text: b.confirm.text, verb: 'Confirmar' } : null,
    caption: b.caption,
  }));

  const pages: DeckPage[] = [];
  for (const p of old.pages) {
    // Rejilla mínima en la que caben los botones, sin pasar de los límites.
    let { cols, rows } = DEFAULT_GRID as { cols: number; rows: number };
    while (cols * rows < p.buttonIds.length && rows < GRID_LIMITS.rows.max) rows++;
    while (cols * rows < p.buttonIds.length && cols < GRID_LIMITS.cols.max) cols++;
    const size = cols * rows;
    for (let part = 0; part * size < Math.max(p.buttonIds.length, 1); part++) {
      const chunk = p.buttonIds.slice(part * size, (part + 1) * size);
      const slots: (string | null)[] = [...chunk];
      while (slots.length < size) slots.push(null);
      pages.push({
        id: part === 0 ? p.id : `${p.id}-${part + 1}`.slice(0, 40),
        name: part === 0 ? p.name : `${p.name} (${part + 1})`.slice(0, 24),
        icon: p.icon,
        grid: { cols, rows },
        slots,
      });
      if (part > 0) warnings.push(`La página «${p.name}» tenía demasiados botones; el resto está en «${p.name} (${part + 1})».`);
    }
  }
  return { schemaVersion: 2, pages, buttons };
}

export type MigrationResult =
  | { ok: true; deck: DeckConfig; migrated: boolean; warnings: string[] }
  | { ok: false; error: string };

/** Valida y, si hace falta, migra una configuración de panel guardada o importada. */
export function migrateDeck(raw: unknown): MigrationResult {
  const warnings: string[] = [];
  const version = (raw as { schemaVersion?: unknown } | null)?.schemaVersion;
  // v8 (ADR-032): fuera encender/apagar el PC, en cualquier versión anterior.
  let data = typeof version === 'number' && version < DECK_SCHEMA_VERSION ? removeRetiredActions(raw) : raw;
  let migrated = false;

  if (version === 1) {
    const old = v1Deck.safeParse(raw);
    if (!old.success) return { ok: false, error: 'Configuración de la versión 1 dañada.' };
    data = v1ToV2(old.data, warnings);
    migrated = true;
  } else if (typeof version === 'number' && version > DECK_SCHEMA_VERSION) {
    return { ok: false, error: `La configuración es de una versión más nueva (${version}). Actualiza la PWA.` };
  } else if (version !== 2 && version !== 3 && version !== 4 && version !== 5 && version !== 6 && version !== 7 && version !== DECK_SCHEMA_VERSION) {
    return { ok: false, error: 'No es una configuración de Pegasus Deck.' };
  }

  if (version === 1 || version === 2) {
    // v2 → v3 (ADR-022): solo cambia la página General. Una v2 válida es una v3 válida sin campos nuevos.
    const v2 = deckSchema.safeParse({ ...(data as object), schemaVersion: DECK_SCHEMA_VERSION });
    if (!v2.success) {
      const issue = v2.error.issues[0];
      return { ok: false, error: `${issue?.message ?? 'Configuración no válida'} (${issue?.path.join('.') ?? ''})` };
    }
    const { schemaVersion: _v, ...body } = v2.data;
    const v3 = upgradeGeneral(body, warnings);
    data = { schemaVersion: DECK_SCHEMA_VERSION, ...upgradeV7(upgradeV6({ ...v3, buttons: upgradePowerButton(v3.buttons) })) };
    migrated = true;
  } else if (version === 3 || version === 4 || version === 5 || version === 6 || version === 7) {
    // v3/v4 → v5 (ADR-027): solo los controles fijos de General ganan sus variantes (Activar sonido/micrófono).
    // v6 → v7 (ADR-031) y v7 → v8 (ADR-032, ya aplicado arriba) no cambian nada más.
    const v3 = deckSchema.safeParse({ ...(data as object), schemaVersion: DECK_SCHEMA_VERSION });
    if (!v3.success) {
      const issue = v3.error.issues[0];
      return { ok: false, error: `${issue?.message ?? 'Configuración no válida'} (${issue?.path.join('.') ?? ''})` };
    }
    const { schemaVersion: _s, ...body } = v3.data;
    data = { schemaVersion: DECK_SCHEMA_VERSION, ...upgradeV7(upgradeV6({ ...body, buttons: upgradePowerButton(body.buttons) })) };
    migrated = true;
  }

  const parsed = deckSchema.safeParse(data);
  if (!parsed.success) {
    const issue = parsed.error.issues[0];
    return { ok: false, error: `${issue?.message ?? 'Configuración no válida'} (${issue?.path.join('.') ?? ''})` };
  }
  return { ok: true, deck: parsed.data, migrated, warnings };
}
