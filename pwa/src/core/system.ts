// "Hechos" del sistema para las condiciones de los botones, derivados de capas
// independientes: PC (sondeo), enlace con el servicio (WebSocket autenticado) e
// instantánea del servicio (sesión, OBS, apps). Lo que no se puede saber es null
// (desconocido), nunca "cerrado".

import type { ServiceSnapshot } from './contract';
import type { LinkMachine } from './linkMachine';
import type { PcMachine } from './pcMachine';

export type { AppState, ObsState, ServiceSnapshot } from './contract';

export interface SystemView {
  pc: PcMachine;
  link: LinkMachine;
  snapshot: ServiceSnapshot | null;
}

/** true / false / cadena, o null si es desconocido. */
export type FactValue = boolean | string | null;
export type Facts = Record<string, FactValue>;

/** Prefijos de hechos con nombre libre (escenas y fuentes de OBS). */
export const OBS_SCENE_PREFIX = 'obs.scene:';
export const OBS_INPUT_PREFIX = 'obs.input:';

export function buildFacts({ pc, link, snapshot }: SystemView): Facts {
  const known = pc.state !== 'UNKNOWN';
  const online = pc.state === 'PC_ONLINE';
  const serviceReady = online && link.state === 'READY';
  // PC fuera de línea ⇒ nada del PC está disponible (false). PC online pero sin
  // enlace ⇒ no sabemos el estado real (null).
  const down: FactValue = !known ? null : online ? null : false;
  const snap = serviceReady ? snapshot : null;
  const fromSnap = (get: (s: ServiceSnapshot) => FactValue): FactValue => (snap ? get(snap) : down);

  const obsReady = fromSnap((s) => s.session.ready && s.obs.state === 'ready');
  const audioReady = fromSnap((s) => s.session.ready && s.audio.state === 'ready');
  const micReady = fromSnap((s) => s.session.ready && s.mic.state === 'ready');
  const mediaReady = fromSnap((s) => s.session.ready && s.media.state !== 'unavailable');
  // P-4: sin detección de apps (sin sesión o detección caída) no se sabe su estado.
  const appsKnown = snap ? snap.session.ready && snap.appsDetection === 'ok' : false;
  const whenObs = (get: (s: ServiceSnapshot) => FactValue): FactValue => (obsReady === true ? get(snap!) : obsReady);

  const facts: Facts = {
    always: true,
    'pc.online': known ? online : null,
    'pc.offline': known ? pc.state === 'PC_OFFLINE' : null,
    'service.ready': known ? serviceReady : null,
    'windows.booted': fromSnap((s) => s.windows.booted),
    'session.ready': fromSnap((s) => s.session.ready),
    'obs.ready': obsReady,
    'obs.connecting': fromSnap((s) => s.obs.state === 'connecting'),
    'obs.recording': whenObs((s) => s.obs.recording),
    'obs.recordingPaused': whenObs((s) => s.obs.recordingPaused),
    'obs.streaming': whenObs((s) => s.obs.streaming),
    'obs.scene': obsReady === true ? snap!.obs.scene : null,
    'audio.ready': audioReady,
    'audio.muted': audioReady === true ? snap!.audio.muted : audioReady,
    'audio.volume': audioReady === true ? String(snap!.audio.volume) : null,
    'mic.ready': micReady,
    'mic.muted': micReady === true ? snap!.mic.muted : micReady,
    'media.ready': mediaReady,
    'media.state': mediaReady === true ? snap!.media.state : null,
    'apps.known': snap ? appsKnown : down,
  };

  if (obsReady === true) {
    for (const scene of snap!.obs.scenes) facts[OBS_SCENE_PREFIX + scene] = true;
    for (const input of snap!.obs.inputs) {
      facts[OBS_INPUT_PREFIX + input.name] = true;
      facts[`obs.inputMuted:${input.name}`] = input.muted;
    }
  }
  for (const app of appsKnown ? snap!.apps : []) {
    facts[`app.${app.id}.state`] = app.state;
    facts[`app.${app.id}.running`] = app.state === 'running' || app.state === 'ready';
    facts[`app.${app.id}.ready`] = app.state === 'ready';
  }
  return facts;
}

/** Valor de un hecho, incluidos los que no aparecen explícitamente (apps, escenas, fuentes). */
export function factValue(facts: Facts, key: string): FactValue {
  if (Object.hasOwn(facts, key)) return facts[key];
  if (key.startsWith(OBS_SCENE_PREFIX) || key.startsWith(OBS_INPUT_PREFIX)) {
    // OBS listo y no aparece ⇒ no existe; si OBS no está listo, se hereda (false / desconocido).
    return facts['obs.ready'] === true ? false : (facts['obs.ready'] ?? null);
  }
  if (key.startsWith('app.')) {
    if (facts['apps.known'] === true) {
      // Con la detección del agente funcionando, una app que no está en la lista blanca no se puede usar.
      return key.endsWith('.state') ? 'not_allowed' : false;
    }
    if (facts['session.ready'] === false) {
      // Sin sesión no hay detección de apps: no se sabe si existen, pero seguro que no están abiertas.
      return key.endsWith('.state') ? null : false;
    }
    if (facts['session.ready'] === true) return null; // sesión, pero detección caída: desconocido
    if (facts['pc.online'] === false) return key.endsWith('.state') ? null : false;
  }
  return null;
}
