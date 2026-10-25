// Máquina del enlace con el servicio Pegasus Deck (independiente de la del PC).
// READY significa: autenticado y con canal de eventos abierto, es decir, la PWA
// tiene el estado real y actualizado del PC.

export type LinkState = 'DISCONNECTED' | 'CONNECTING' | 'READY' | 'AUTH_REQUIRED' | 'INCOMPATIBLE';

export type LinkReason =
  | 'no_token'        // nunca se ha emparejado este iPad
  | 'rejected'        // el PC rechazó el token (revocado o de otro PC)
  | 'protocol'        // versión de protocolo distinta
  | null;

export interface LinkMachine {
  state: LinkState;
  since: number;
  reason: LinkReason;
  /** Intentos de conexión fallidos seguidos (para la espera exponencial). */
  attempts: number;
  /** Protocolo que anuncia el servicio (si es incompatible). */
  serviceProtocol: number | null;
}

export type LinkEvent =
  | { type: 'CONNECT'; at: number }
  | { type: 'CONNECTED'; at: number }
  | { type: 'LOST'; at: number }
  | { type: 'NO_TOKEN'; at: number }
  | { type: 'AUTH_REJECTED'; at: number }
  | { type: 'PROTOCOL_MISMATCH'; at: number; protocol: number }
  | { type: 'TOKEN_CHANGED'; at: number }
  | { type: 'PC_GONE'; at: number };

/** Tiempo máximo para abrir el WebSocket y autenticarse. */
export const HANDSHAKE_TIMEOUT_MS = 5000;
const BACKOFF_MS = [1000, 2000, 4000, 8000, 15000];

export function initialLink(at: number): LinkMachine {
  return { state: 'DISCONNECTED', since: at, reason: null, attempts: 0, serviceProtocol: null };
}

/** Espera antes del siguiente intento de conexión. */
export function backoffMs(link: LinkMachine): number {
  return BACKOFF_MS[Math.min(Math.max(link.attempts - 1, 0), BACKOFF_MS.length - 1)];
}

function to(m: LinkMachine, state: LinkState, at: number, patch: Partial<LinkMachine> = {}): LinkMachine {
  return { ...m, state, since: at, reason: null, ...patch };
}

export function linkTransition(m: LinkMachine, e: LinkEvent): LinkMachine {
  switch (e.type) {
    case 'CONNECT':
      // No se reintenta en bucle si falta emparejar o el protocolo no coincide.
      if (m.state === 'DISCONNECTED') return to(m, 'CONNECTING', e.at, { attempts: m.attempts });
      return m;
    case 'CONNECTED':
      return m.state === 'CONNECTING' ? to(m, 'READY', e.at, { attempts: 0 }) : m;
    case 'LOST':
      if (m.state === 'CONNECTING' || m.state === 'READY') return to(m, 'DISCONNECTED', e.at, { attempts: m.attempts + 1 });
      return m;
    case 'NO_TOKEN':
      return m.state === 'AUTH_REQUIRED' && m.reason === 'no_token' ? m : to(m, 'AUTH_REQUIRED', e.at, { reason: 'no_token', attempts: 0 });
    case 'AUTH_REJECTED':
      return to(m, 'AUTH_REQUIRED', e.at, { reason: 'rejected', attempts: 0 });
    case 'PROTOCOL_MISMATCH':
      return m.state === 'INCOMPATIBLE' && m.serviceProtocol === e.protocol
        ? m
        : to(m, 'INCOMPATIBLE', e.at, { reason: 'protocol', serviceProtocol: e.protocol, attempts: 0 });
    case 'TOKEN_CHANGED':
      return to(m, 'DISCONNECTED', e.at, { attempts: 0, serviceProtocol: null });
    case 'PC_GONE':
      // Si el PC deja de responder, el enlace cae; los motivos de emparejamiento se conservan.
      if (m.state === 'AUTH_REQUIRED') return m;
      return m.state === 'DISCONNECTED' && m.attempts === 0 ? m : to(m, 'DISCONNECTED', e.at, { attempts: 0, serviceProtocol: null });
  }
}
