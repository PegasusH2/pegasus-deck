// Catálogo de acciones. Cada tipo declara:
//  - parámetros (de los que salen el esquema de validación y el formulario del editor),
//  - requisitos mínimos (se combinan con AND con la condición del botón),
//  - quién la ejecuta, cuánto puede tardar y cómo se confirma (ADR-006).
// El PC tiene su propia lista blanca: aunque alguien manipule la configuración,
// solo acepta tipos registrados con parámetros válidos.

import { z } from 'zod';
import type { Condition } from './conditions';
import { RESOLVE_COMMAND_IDS } from './resolve';
import { OBS_INPUT_PREFIX, OBS_SCENE_PREFIX } from './system';

export type ActionCategory = 'system' | 'apps' | 'obs' | 'local';
/** pwa: se resuelve en el iPad · service: API del PC. */
export type ActionExecutor = 'pwa' | 'service';
/** response: la respuesta HTTP es el resultado · event: hay que esperar el mensaje 'operation'. */
export type ActionCompletion = 'response' | 'event';

export type ParamKind = 'app' | 'obsScene' | 'obsInput' | 'page' | 'text' | 'percent' | 'resolveCommand' | 'output';

export interface ParamField {
  key: string;
  label: string;
  kind: ParamKind;
}

export type ActionParams = Record<string, string | number | boolean>;

export interface ActionDefinition {
  type: string;
  label: string;
  category: ActionCategory;
  executor: ActionExecutor;
  params: ParamField[];
  /** Requisitos implícitos; pueden depender de los parámetros (p. ej. que exista la escena). */
  requires: (params: ActionParams) => Condition;
  completion: ActionCompletion;
  timeoutMs: number;
  /** Se puede reintentar sin efectos duplicados. */
  idempotent: boolean;
  /** Fuerza confirmación aunque el botón no la tenga configurada. */
  destructive: boolean;
}

const ALWAYS: Condition = { always: true };
const SESSION: Condition = { all: [{ fact: 'pc.online' }, { fact: 'service.ready' }, { fact: 'session.ready' }] };
const OBS: Condition = { all: [{ fact: 'pc.online' }, { fact: 'service.ready' }, { fact: 'obs.ready' }] };
const AUDIO: Condition = { all: [SESSION, { fact: 'audio.ready' }] };
const MIC: Condition = { all: [SESSION, { fact: 'mic.ready' }] };
const MEDIA: Condition = { all: [SESSION, { fact: 'media.ready' }] };
const OUTPUTS: Condition = { all: [SESSION, { fact: 'outputs.ready' }] };

const ID_RE = /^[a-z0-9][a-z0-9-]{0,39}$/;

const FIELD_SCHEMA: Record<ParamKind, z.ZodTypeAny> = {
  app: z.string().regex(ID_RE, 'Identificador de aplicación no válido'),
  page: z.string().regex(ID_RE, 'Identificador de página no válido'),
  // ADR-035: id opaco que da el PC; nunca el identificador de Windows.
  output: z.string().regex(ID_RE, 'Salida de audio no válida'),
  // Sin trim(): se envía exactamente el nombre de OBS (puede tener espacios).
  obsScene: z.string().max(100).refine((s) => s.trim().length > 0, 'Indica la escena'),
  obsInput: z.string().max(100).refine((s) => s.trim().length > 0, 'Indica la fuente'),
  text: z.string().max(200),
  percent: z.number({ invalid_type_error: 'Indica un porcentaje' }).int('Sin decimales').min(0, 'Mínimo 0').max(100, 'Máximo 100'),
  resolveCommand: z.enum(RESOLVE_COMMAND_IDS, { errorMap: () => ({ message: 'Orden de DaVinci Resolve desconocida' }) }),
};

type Def = Omit<ActionDefinition, 'requires' | 'params' | 'completion' | 'timeoutMs' | 'idempotent' | 'destructive'>
  & Partial<Pick<ActionDefinition, 'params' | 'completion' | 'timeoutMs' | 'idempotent' | 'destructive'>>
  & { requires?: Condition | ((p: ActionParams) => Condition) };

function def(d: Def): ActionDefinition {
  const req = d.requires ?? ALWAYS;
  return {
    params: [],
    completion: 'response',
    timeoutMs: 8000,
    idempotent: false,
    destructive: false,
    ...d,
    requires: typeof req === 'function' ? req : () => req,
  };
}

const DEFINITIONS: ActionDefinition[] = [
  // Sistema
  def({ type: 'system.check', label: 'Comprobar disponibilidad del PC', category: 'system', executor: 'pwa', idempotent: true }),
  def({ type: 'system.status', label: 'Ver estado del sistema', category: 'system', executor: 'pwa', idempotent: true }),
  def({ type: 'windows.lock', label: 'Bloquear Windows', category: 'system', executor: 'service', requires: SESSION, idempotent: true }),

  // Aplicaciones: solo ids de la lista blanca del PC, nunca rutas
  def({ type: 'app.open', label: 'Abrir aplicación autorizada', category: 'apps', executor: 'service',
    params: [{ key: 'appId', label: 'Aplicación', kind: 'app' }],
    requires: (p) => ({ all: [SESSION, { fact: `app.${String(p.appId)}.state`, in: ['installed', 'starting', 'running', 'ready'] }] }),
    completion: 'event', timeoutMs: 30_000, idempotent: true }),
  // ADR-024: cierra sus ventanas como con la X (nunca fuerza); el PC exige el segundo paso.
  def({ type: 'app.closeAll', label: 'Cerrar todas las ventanas de una app', category: 'apps', executor: 'service',
    params: [{ key: 'appId', label: 'Aplicación', kind: 'app' }],
    requires: (p) => ({ all: [SESSION, { fact: `app.${String(p.appId)}.running` }] }),
    timeoutMs: 10_000, destructive: true }),

  // OBS: la petición se acepta y el evento de OBS confirma
  def({ type: 'obs.recording.start', label: 'Iniciar grabación', category: 'obs', executor: 'service', requires: OBS, completion: 'event', timeoutMs: 10_000 }),
  def({ type: 'obs.recording.stop', label: 'Detener grabación', category: 'obs', executor: 'service', requires: OBS, completion: 'event', timeoutMs: 15_000 }),
  def({ type: 'obs.recording.pause', label: 'Pausar / reanudar grabación', category: 'obs', executor: 'service', requires: OBS, completion: 'event', timeoutMs: 10_000 }),
  def({ type: 'obs.streaming.start', label: 'Iniciar streaming', category: 'obs', executor: 'service', requires: OBS, completion: 'event', timeoutMs: 20_000 }),
  def({ type: 'obs.streaming.stop', label: 'Detener streaming', category: 'obs', executor: 'service', requires: OBS, completion: 'event', timeoutMs: 15_000 }),
  def({ type: 'obs.scene.set', label: 'Cambiar escena', category: 'obs', executor: 'service',
    params: [{ key: 'scene', label: 'Escena', kind: 'obsScene' }],
    requires: (p) => ({ all: [OBS, { fact: OBS_SCENE_PREFIX + String(p.scene) }] }),
    completion: 'event', timeoutMs: 5000, idempotent: true }),
  def({ type: 'obs.input.toggleMute', label: 'Silenciar / activar fuente', category: 'obs', executor: 'service',
    params: [{ key: 'input', label: 'Fuente de audio', kind: 'obsInput' }],
    requires: (p) => ({ all: [OBS, { fact: OBS_INPUT_PREFIX + String(p.input) }] }),
    completion: 'event', timeoutMs: 5000 }),

  // Audio de Windows (lo ejecuta el agente de sesión)
  def({ type: 'audio.master.toggleMute', label: 'Silenciar / activar el audio del PC', category: 'system', executor: 'service', requires: AUDIO, timeoutMs: 5000 }),
  def({ type: 'audio.master.setVolume', label: 'Fijar el volumen del PC', category: 'system', executor: 'service',
    params: [{ key: 'percent', label: 'Volumen (%)', kind: 'percent' }], requires: AUDIO, timeoutMs: 5000, idempotent: true }),
  def({ type: 'audio.mic.toggleMute', label: 'Silenciar / activar el micrófono del PC', category: 'system', executor: 'service', requires: MIC, timeoutMs: 5000 }),
  // ADR-035: elegir la salida de audio predeterminada de Windows (solo una de las que informa el PC).
  def({ type: 'audio.output.set', label: 'Cambiar salida de audio', category: 'system', executor: 'service',
    params: [{ key: 'device', label: 'Salida', kind: 'output' }], requires: OUTPUTS, timeoutMs: 5000, idempotent: true }),
  def({ type: 'local.audioOutputs', label: 'Elegir salida de audio', category: 'local', executor: 'pwa', requires: OUTPUTS, idempotent: true }),

  // Multimedia de Windows (ADR-022): pausa/reanuda lo que suene en el PC, con estado real
  def({ type: 'media.pause', label: 'Pausar la reproducción del PC', category: 'system', executor: 'service',
    requires: { all: [MEDIA, { fact: 'media.state', eq: 'playing' }] }, timeoutMs: 5000, idempotent: true }),
  def({ type: 'media.play', label: 'Reanudar la reproducción del PC', category: 'system', executor: 'service',
    requires: { all: [MEDIA, { fact: 'media.state', in: ['paused', 'stopped'] }] }, timeoutMs: 5000, idempotent: true }),

  // DaVinci Resolve (ADR-028): el agente pulsa en Resolve las teclas de una orden de la lista cerrada.
  def({ type: 'davinci.command', label: 'Orden de DaVinci Resolve', category: 'apps', executor: 'service',
    params: [{ key: 'command', label: 'Orden', kind: 'resolveCommand' }],
    requires: { all: [SESSION, { fact: 'app.davinci.running' }] }, timeoutMs: 5000 }),

  // Locales
  def({ type: 'local.settings', label: 'Abrir configuración', category: 'local', executor: 'pwa', idempotent: true }),
  def({ type: 'local.page', label: 'Ir a página', category: 'local', executor: 'pwa',
    params: [{ key: 'pageId', label: 'Página', kind: 'page' }], idempotent: true }),
];

const REGISTRY = new Map(DEFINITIONS.map((d) => [d.type, d]));

export function getAction(type: string): ActionDefinition | undefined {
  return REGISTRY.get(type);
}

export function listActions(): ActionDefinition[] {
  return [...DEFINITIONS];
}

/** Esquema de validación de los parámetros de una acción (sin campos extra). */
export function paramsSchema(d: ActionDefinition): z.ZodType<ActionParams> {
  const shape: Record<string, z.ZodTypeAny> = {};
  for (const f of d.params) shape[f.key] = FIELD_SCHEMA[f.kind];
  return z.object(shape).strict() as unknown as z.ZodType<ActionParams>;
}

export type ParamsCheck = { ok: true; params: ActionParams } | { ok: false; error: string };

export function validateParams(type: string, params: unknown): ParamsCheck {
  const d = getAction(type);
  if (!d) return { ok: false, error: `Acción desconocida: ${type}` };
  const r = paramsSchema(d).safeParse(params);
  return r.success ? { ok: true, params: r.data } : { ok: false, error: r.error.issues[0]?.message ?? 'Parámetros no válidos' };
}
