// Enlace con el servicio y estado global derivado.
import { describe, expect, it } from 'vitest';
import { globalStatus } from '../../src/core/globalStatus';
import { backoffMs, initialLink, linkTransition, type LinkEvent, type LinkMachine } from '../../src/core/linkMachine';
import { link, machine, snapshot } from './fixtures';

const runLink = (events: LinkEvent[], start: LinkMachine = initialLink(0)) => events.reduce(linkTransition, start);

describe('máquina del enlace', () => {
  it('DISCONNECTED → CONNECTING → READY', () => {
    const m = runLink([{ type: 'CONNECT', at: 1 }, { type: 'CONNECTED', at: 2 }]);
    expect(m.state).toBe('READY');
    expect(m.attempts).toBe(0);
  });

  it('pérdidas seguidas aumentan la espera exponencial hasta 15 s', () => {
    let m = initialLink(0);
    const waits: number[] = [];
    for (let i = 0; i < 7; i++) {
      m = runLink([{ type: 'CONNECT', at: i }, { type: 'LOST', at: i }], m);
      waits.push(backoffMs(m));
    }
    expect(waits).toEqual([1000, 2000, 4000, 8000, 15000, 15000, 15000]);
    // al conectar se reinicia
    expect(runLink([{ type: 'CONNECT', at: 9 }, { type: 'CONNECTED', at: 9 }], m).attempts).toBe(0);
  });

  it('token rechazado: AUTH_REQUIRED y no reintenta solo', () => {
    const m = runLink([{ type: 'CONNECT', at: 1 }, { type: 'AUTH_REJECTED', at: 2 }]);
    expect(m).toMatchObject({ state: 'AUTH_REQUIRED', reason: 'rejected' });
    expect(linkTransition(m, { type: 'CONNECT', at: 3 })).toBe(m);
    expect(linkTransition(m, { type: 'PC_GONE', at: 3 })).toBe(m);
    // un token nuevo desbloquea
    expect(linkTransition(m, { type: 'TOKEN_CHANGED', at: 4 }).state).toBe('DISCONNECTED');
  });

  it('protocolo distinto: INCOMPATIBLE', () => {
    const m = runLink([{ type: 'PROTOCOL_MISMATCH', at: 1, protocol: 3 }]);
    expect(m).toMatchObject({ state: 'INCOMPATIBLE', serviceProtocol: 3 });
    expect(linkTransition(m, { type: 'CONNECT', at: 2 })).toBe(m);
  });

  it('el PC se va: el enlace cae sin acumular intentos', () => {
    const ready = runLink([{ type: 'CONNECT', at: 1 }, { type: 'CONNECTED', at: 2 }]);
    expect(linkTransition(ready, { type: 'PC_GONE', at: 3 })).toMatchObject({ state: 'DISCONNECTED', attempts: 0 });
  });

  it('CONNECTED fuera de CONNECTING se ignora (respuesta tardía)', () => {
    const m = initialLink(0);
    expect(linkTransition(m, { type: 'CONNECTED', at: 1 })).toBe(m);
  });
});

describe('estado global derivado', () => {
  const g = (pc: Parameters<typeof machine>[0], l = link('DISCONNECTED'), s: ReturnType<typeof snapshot> | null = null) =>
    globalStatus({ pc: machine(pc), link: l, snapshot: s });

  it('cubre los ocho estados', () => {
    expect(g('UNKNOWN')).toMatchObject({ status: 'PC_OFFLINE', checking: true });
    expect(g('PC_OFFLINE').status).toBe('PC_OFFLINE');
    expect(g('PC_ONLINE').status).toBe('PC_ONLINE');
    expect(g('PC_ONLINE', link('CONNECTING')).status).toBe('SERVICE_CONNECTING');
    expect(g('PC_ONLINE', link('READY'), snapshot()).status).toBe('SERVICE_READY');
    expect(g('PC_ONLINE', link('READY'), snapshot({ obs: { state: 'connecting' } })).status).toBe('OBS_CONNECTING');
    expect(g('PC_ONLINE', link('READY'), snapshot({ obs: { state: 'ready' } })).status).toBe('OBS_READY');
    expect(g('PC_ONLINE', link('READY'), snapshot({ session: false })).status).toBe('DEGRADED');
  });

  it('errores de enlace con motivo comprensible', () => {
    expect(g('PC_ONLINE', link('AUTH_REQUIRED', { reason: 'no_token' }))).toMatchObject({ status: 'ERROR', reasons: ['Este iPad no está emparejado con el PC.'] });
    expect(g('PC_ONLINE', link('INCOMPATIBLE', { serviceProtocol: 3 })).reasons[0]).toMatch(/protocolo 3/);
    expect(g('PC_ONLINE', link('READY'), snapshot({ obs: { state: 'auth_error' } }))).toMatchObject({ status: 'DEGRADED' });
  });
});
