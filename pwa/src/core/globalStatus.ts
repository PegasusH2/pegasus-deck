// Estado global del sistema (ADR-005): un único valor para la cabecera y los
// resúmenes, derivado de las capas. Las condiciones de los botones NO lo usan:
// usan los hechos de cada capa, así perder OBS solo afecta a lo que depende de OBS.

import type { SystemView } from './system';

export type GlobalStatus =
  | 'PC_OFFLINE'
  | 'PC_ONLINE'
  | 'SERVICE_CONNECTING'
  | 'SERVICE_READY'
  | 'OBS_CONNECTING'
  | 'OBS_READY'
  | 'DEGRADED'
  | 'ERROR';

export interface GlobalSummary {
  status: GlobalStatus;
  /** Motivos legibles (para ERROR y DEGRADED). */
  reasons: string[];
  /** true mientras aún no se sabe nada del PC. */
  checking: boolean;
}

export function globalStatus({ pc, link, snapshot }: SystemView): GlobalSummary {
  const sum = (status: GlobalStatus, reasons: string[] = [], checking = false): GlobalSummary => ({ status, reasons, checking });

  if (pc.state === 'UNKNOWN') return sum('PC_OFFLINE', [], true);
  if (pc.state === 'PC_OFFLINE') return sum('PC_OFFLINE');

  // PC online
  if (link.state === 'AUTH_REQUIRED') {
    return sum('ERROR', [link.reason === 'rejected'
      ? 'El PC ha rechazado este iPad (emparejamiento revocado o de otro PC). Vuelve a emparejarlo.'
      : 'Este iPad no está emparejado con el PC.']);
  }
  if (link.state === 'INCOMPATIBLE') {
    return sum('ERROR', [`Versión del servicio incompatible (protocolo ${link.serviceProtocol ?? '?'}). Actualiza Pegasus Deck en el PC o la PWA.`]);
  }
  if (link.state === 'DISCONNECTED') return sum('PC_ONLINE');
  if (link.state === 'CONNECTING' || !snapshot) return sum('SERVICE_CONNECTING');

  const degraded: string[] = [];
  if (!snapshot.session.ready) degraded.push('Sin sesión de Windows: no se pueden abrir programas ni controlar OBS.');
  if (snapshot.session.ready && snapshot.obs.state === 'auth_error') degraded.push('OBS rechaza la contraseña configurada en el PC.');
  if (snapshot.session.ready && snapshot.obs.state === 'incompatible') degraded.push('La versión de OBS no es compatible (requiere obs-websocket 5).');
  if (snapshot.session.ready && snapshot.appsDetection === 'unavailable') degraded.push('El PC no puede comprobar qué aplicaciones están abiertas.');
  if (degraded.length) return sum('DEGRADED', degraded);

  if (snapshot.obs.state === 'connecting') return sum('OBS_CONNECTING');
  if (snapshot.obs.state === 'ready') return sum('OBS_READY');
  return sum('SERVICE_READY');
}
