// Textos de estado para la interfaz (sin jerga técnica).

import type { GlobalStatus, GlobalSummary } from '../core/globalStatus';
import type { LinkMachine } from '../core/linkMachine';
import type { PcMachine } from '../core/pcMachine';
import type { ServiceSnapshot } from '../core/system';
import type { Tone } from './kit';

export interface StatusLine {
  label: string;
  tone: Tone;
}

export function formatDuration(ms: number): string {
  const total = Math.max(0, Math.floor(ms / 1000));
  return `${Math.floor(total / 60)}:${String(total % 60).padStart(2, '0')}`;
}

export const GLOBAL_LABEL: Record<GlobalStatus, StatusLine> = {
  PC_OFFLINE: { label: 'PC apagado', tone: 'bad' },
  PC_ONLINE: { label: 'PC encendido', tone: 'warn' },
  SERVICE_CONNECTING: { label: 'Conectando', tone: 'warn' },
  SERVICE_READY: { label: 'Listo', tone: 'ok' },
  OBS_CONNECTING: { label: 'Conectando con OBS', tone: 'warn' },
  OBS_READY: { label: 'Listo · OBS', tone: 'ok' },
  DEGRADED: { label: 'Funcionamiento parcial', tone: 'warn' },
  ERROR: { label: 'Requiere atención', tone: 'bad' },
};

export function globalLine(summary: GlobalSummary): StatusLine {
  return summary.checking ? { label: 'Comprobando…', tone: 'idle' } : GLOBAL_LABEL[summary.status];
}

export function pcStatus(pc: PcMachine): StatusLine {
  switch (pc.state) {
    case 'UNKNOWN': return { label: 'Comprobando…', tone: 'idle' };
    case 'PC_OFFLINE': return { label: 'Offline', tone: 'bad' };
    case 'PC_ONLINE': return { label: 'Online', tone: 'ok' };
  }
}

export function linkStatus(pc: PcMachine, link: LinkMachine): StatusLine {
  if (pc.state === 'UNKNOWN') return { label: '—', tone: 'idle' };
  if (pc.state !== 'PC_ONLINE') return { label: 'Desconectado', tone: 'bad' };
  switch (link.state) {
    case 'READY': return { label: 'Conectado', tone: 'ok' };
    case 'CONNECTING': return { label: 'Conectando…', tone: 'warn' };
    case 'DISCONNECTED': return { label: link.attempts > 0 ? 'Reconectando…' : 'Conectando…', tone: 'warn' };
    case 'AUTH_REQUIRED': return { label: link.reason === 'rejected' ? 'Rechazado' : 'Sin emparejar', tone: 'bad' };
    case 'INCOMPATIBLE': return { label: 'Incompatible', tone: 'bad' };
  }
}

export function sessionStatus(snapshot: ServiceSnapshot | null): StatusLine {
  if (!snapshot) return { label: '—', tone: 'idle' };
  return snapshot.session.ready ? { label: 'Sesión iniciada', tone: 'ok' } : { label: 'Sin sesión', tone: 'warn' };
}

export function obsStatus(snapshot: ServiceSnapshot | null): StatusLine {
  if (!snapshot) return { label: '—', tone: 'idle' };
  if (!snapshot.session.ready) return { label: 'Sin sesión', tone: 'idle' };
  switch (snapshot.obs.state) {
    case 'ready': return { label: 'Conectado', tone: 'ok' };
    case 'connecting': return { label: 'Conectando…', tone: 'warn' };
    case 'auth_error': return { label: 'Contraseña incorrecta', tone: 'bad' };
    case 'incompatible': return { label: 'Incompatible', tone: 'bad' };
    case 'unavailable': return { label: 'Cerrado', tone: 'idle' };
  }
}

export const APP_STATE_LABEL: Record<ServiceSnapshot['apps'][number]['state'], StatusLine> = {
  not_installed: { label: 'No instalada', tone: 'idle' },
  installed: { label: 'Cerrada', tone: 'idle' },
  starting: { label: 'Iniciándose…', tone: 'warn' },
  running: { label: 'Abierta', tone: 'ok' },
  ready: { label: 'Lista', tone: 'ok' },
};
