// Condiciones de disponibilidad: árbol AND/OR/NOT con hojas que consultan hechos.
// La evaluación es de tres valores (sí / no / desconocido) y devuelve los motivos
// legibles por los que un botón no está disponible.

import { factValue, OBS_INPUT_PREFIX, OBS_SCENE_PREFIX, type Facts, type FactValue } from './system';

export type Condition =
  | { always: true }
  | { all: Condition[] }
  | { any: Condition[] }
  | { not: Condition }
  | FactCondition;

export interface FactCondition {
  fact: string;
  /** Compara con un valor concreto (p. ej. una escena). */
  eq?: string | boolean;
  /** El hecho debe tener uno de estos valores (p. ej. estados de una app). */
  in?: string[];
}

export type Tri = boolean | null;

export interface EvalResult {
  value: Tri;
  /** Requisitos que no se cumplen. */
  missing: string[];
  /** Estado actual que bloquea cada requisito (p. ej. "OBS no conectado"). */
  blockers: string[];
  /** Requisitos cuyo estado no se conoce. */
  unknown: string[];
}

/** Texto del hecho cuando se cumple y cuando no. */
const FACT_TEXT: Record<string, [string, string]> = {
  'pc.online': ['PC online', 'PC no disponible'],
  'pc.offline': ['PC apagado', 'PC encendido'],
  'service.ready': ['Servicio conectado', 'Servicio desconectado'],
  'windows.booted': ['Windows iniciado', 'Windows sin iniciar'],
  'session.ready': ['Sesión de Windows iniciada', 'Sin sesión de Windows'],
  'obs.ready': ['OBS conectado', 'OBS no conectado'],
  'obs.connecting': ['OBS conectándose', 'OBS sin conectar'],
  'obs.recording': ['Grabación activa', 'Grabación no iniciada'],
  'obs.recordingPaused': ['Grabación en pausa', 'Grabación sin pausar'],
  'obs.streaming': ['Streaming activo', 'Streaming no iniciado'],
  'audio.ready': ['Audio de Windows disponible', 'Audio de Windows no disponible'],
  'audio.muted': ['Audio silenciado', 'Audio con sonido'],
  'mic.ready': ['Micrófono disponible', 'Micrófono no disponible'],
  'mic.muted': ['Micrófono silenciado', 'Micrófono activo'],
  'media.ready': ['Multimedia disponible', 'Multimedia no disponible'],
  'apps.known': ['Detección de apps activa', 'Detección de apps no disponible'],
};

const APP_STATE_TEXT: Record<string, string> = {
  not_allowed: 'no autorizada en el PC', // no está en la lista blanca (solo en la PWA, no en el contrato)
  not_installed: 'no instalada',
  installed: 'cerrada',
  starting: 'iniciándose',
  running: 'abierta',
  ready: 'lista',
};

const APP_NAMES: Record<string, string> = {
  obs: 'OBS',
  chrome: 'Chrome',
  davinci: 'DaVinci Resolve',
  explorer: 'Explorador',
};

function appName(id: string): string {
  return APP_NAMES[id] ?? id.charAt(0).toUpperCase() + id.slice(1);
}

export function describe(c: Condition, negated = false): string {
  if ('always' in c) return negated ? 'Nunca' : 'Siempre';
  if ('not' in c) return describe(c.not, !negated);
  if ('all' in c || 'any' in c) {
    const isAll = 'all' in c;
    const parts = (isAll ? (c as { all: Condition[] }).all : (c as { any: Condition[] }).any)
      .map((child) => describe(child, negated));
    // De Morgan: NOT(A y B) = (no A) o (no B)
    const joiner = isAll !== negated ? ' y ' : ' o ';
    return parts.length > 1 ? `(${parts.join(joiner)})` : (parts[0] ?? '');
  }
  return describeFact(c, negated);
}

function describeFact(c: FactCondition, negated: boolean): string {
  const app = /^app\.([^.]+)\.(state|running|ready)$/.exec(c.fact);
  if (app) {
    const name = appName(app[1]);
    if (app[2] === 'state') {
      // "cualquier estado salvo no instalada" = está en la lista blanca del PC
      if (c.in && c.in.length === 4 && !c.in.includes('not_installed')) {
        return negated ? `${name} no autorizada en el PC` : `${name} autorizada en el PC`;
      }
      const states = (c.in ?? (c.eq !== undefined ? [String(c.eq)] : [])).map((s) => APP_STATE_TEXT[s] ?? s);
      return `${name} ${negated ? 'no ' : ''}${states.join(' o ')}`;
    }
    const what = app[2] === 'ready' ? 'lista' : 'abierta';
    return negated ? `${name} no ${what}` : `${name} ${what}`;
  }
  if (c.fact.startsWith(OBS_SCENE_PREFIX)) {
    const name = c.fact.slice(OBS_SCENE_PREFIX.length);
    return negated ? `Escena «${name}» no existe` : `Escena «${name}» existe`;
  }
  if (c.fact.startsWith(OBS_INPUT_PREFIX)) {
    const name = c.fact.slice(OBS_INPUT_PREFIX.length);
    return negated ? `Fuente «${name}» no existe` : `Fuente «${name}» existe`;
  }
  if (c.fact === 'obs.scene' && c.eq !== undefined) {
    return negated ? `Escena distinta de «${c.eq}»` : `Escena «${c.eq}» activa`;
  }
  if (c.fact === 'media.state') {
    const playing = c.eq === 'playing' || (c.in?.length === 1 && c.in[0] === 'playing');
    if (playing) return negated ? 'Nada reproduciéndose' : 'Algo reproduciéndose';
    return negated ? 'Nada en pausa' : 'Reproducción en pausa';
  }
  const text = FACT_TEXT[c.fact];
  const wantTrue = (c.eq === undefined || c.eq === true) !== negated;
  if (text) return wantTrue ? text[0] : text[1];
  const base = c.eq !== undefined ? `${c.fact} = ${String(c.eq)}` : c.fact;
  return negated ? `No: ${base}` : base;
}

function leafValue(c: FactCondition, facts: Facts): Tri {
  const v: FactValue = factValue(facts, c.fact);
  if (v === null || v === undefined) return null;
  if (c.in) return c.in.includes(String(v));
  if (c.eq !== undefined) return v === c.eq;
  return v === true;
}

function ev(c: Condition, facts: Facts, negated: boolean): EvalResult {
  if ('always' in c) {
    return { value: !negated, missing: negated ? ['Nunca disponible'] : [], blockers: negated ? ['Desactivado'] : [], unknown: [] };
  }
  if ('not' in c) return ev(c.not, facts, !negated);

  if ('all' in c || 'any' in c) {
    const children = 'all' in c ? c.all : c.any;
    const results = children.map((child) => ev(child, facts, negated));
    const conjunction = ('all' in c) !== negated;
    if (conjunction) {
      const value: Tri = results.some((r) => r.value === false) ? false
        : results.some((r) => r.value === null) ? null : true;
      return {
        value,
        missing: dedupe(results.flatMap((r) => r.missing)),
        blockers: dedupe(results.flatMap((r) => r.blockers)),
        unknown: dedupe(results.flatMap((r) => r.unknown)),
      };
    }
    const value: Tri = results.some((r) => r.value === true) ? true
      : results.some((r) => r.value === null) ? null : false;
    const text = describe(c, negated);
    return {
      value,
      missing: value === false ? [`Una de: ${text}`] : [],
      blockers: value === false ? dedupe(results.flatMap((r) => r.blockers)) : [],
      unknown: value === null ? [text] : [],
    };
  }

  const raw = leafValue(c, facts);
  const value: Tri = raw === null ? null : raw !== negated;
  const text = describeFact(c, negated);
  return {
    value,
    missing: value === false ? [text] : [],
    blockers: value === false ? [blockerText(c, facts, negated)] : [],
    unknown: value === null ? [text] : [],
  };
}

/** Estado actual que impide cumplir una hoja (p. ej. "OBS lista" en vez de "OBS no cerrada"). */
function blockerText(c: FactCondition, facts: Facts, negated: boolean): string {
  const app = /^app\.([^.]+)\.state$/.exec(c.fact);
  const v = factValue(facts, c.fact);
  if (app && typeof v === 'string') return `${appName(app[1])} ${APP_STATE_TEXT[v] ?? v}`;
  return describeFact(c, !negated);
}

function dedupe(list: string[]): string[] {
  return [...new Set(list)];
}

export function evaluate(c: Condition, facts: Facts): EvalResult {
  return ev(c, facts, false);
}

/** Une varias condiciones con AND, descartando las que son "siempre". */
export function allOf(...conds: (Condition | null | undefined)[]): Condition {
  const parts = conds.filter((c): c is Condition => !!c && !('always' in c));
  if (parts.length === 0) return { always: true };
  if (parts.length === 1) return parts[0];
  return { all: parts };
}

/** Lista plana de requisitos (para mostrarlos como chips en la interfaz). */
export function requirementList(c: Condition): string[] {
  if ('always' in c) return ['Siempre disponible'];
  if ('all' in c) return dedupe(c.all.flatMap(requirementList));
  return [describe(c)];
}
