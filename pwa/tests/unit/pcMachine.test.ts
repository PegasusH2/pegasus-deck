import { describe, expect, it } from 'vitest';
import { initialMachine, transition, type PcEvent, type PcMachine } from '../../src/core/pcMachine';

function run(events: PcEvent[], start: PcMachine = initialMachine(0)): PcMachine {
  return events.reduce((m, e) => transition(m, e), start);
}

describe('máquina de estados del PC (solo observa: sin encender ni apagar, ADR-032)', () => {
  it('UNKNOWN pasa a OFFLINE solo tras dos fallos seguidos', () => {
    const one = run([{ type: 'PROBE_FAIL', at: 1 }]);
    expect(one.state).toBe('UNKNOWN');
    expect(run([{ type: 'PROBE_FAIL', at: 1 }, { type: 'PROBE_FAIL', at: 2 }]).state).toBe('PC_OFFLINE');
  });

  it('UNKNOWN pasa a ONLINE con la primera respuesta', () => {
    const m = run([{ type: 'PROBE_OK', at: 5 }]);
    expect(m.state).toBe('PC_ONLINE');
    expect(m.lastOkAt).toBe(5);
  });

  it('OFFLINE pasa a ONLINE en cuanto responde (encendido a mano)', () => {
    const off = run([{ type: 'PROBE_FAIL', at: 1 }, { type: 'PROBE_FAIL', at: 2 }]);
    expect(transition(off, { type: 'PROBE_FAIL', at: 3 })).toBe(off);
    expect(transition(off, { type: 'PROBE_OK', at: 4 }).state).toBe('PC_ONLINE');
  });

  it('ONLINE necesita tres fallos seguidos para pasar a OFFLINE (sin parpadeos)', () => {
    const online = run([{ type: 'PROBE_OK', at: 1 }]);
    const twoFails = run([{ type: 'PROBE_FAIL', at: 2 }, { type: 'PROBE_FAIL', at: 3 }], online);
    expect(twoFails.state).toBe('PC_ONLINE');
    // una respuesta buena reinicia el contador
    const recovered = run([{ type: 'PROBE_OK', at: 4 }, { type: 'PROBE_FAIL', at: 5 }, { type: 'PROBE_FAIL', at: 6 }], twoFails);
    expect(recovered.state).toBe('PC_ONLINE');
    expect(transition(recovered, { type: 'PROBE_FAIL', at: 7 }).state).toBe('PC_OFFLINE');
  });

  it('el paso del tiempo no cambia el estado', () => {
    const online = run([{ type: 'PROBE_OK', at: 1 }]);
    expect(transition(online, { type: 'TICK', at: 999_999 })).toBe(online);
  });
});
