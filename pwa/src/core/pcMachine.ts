// Máquina de estados del PC. Es una función pura: (estado, evento) -> estado.
// Las transiciones no contempladas devuelven el MISMO objeto (se ignoran).
// Pegasus Deck no enciende ni apaga el PC (ADR-032): solo observa si responde.

export type PcState =
  | 'UNKNOWN'
  | 'PC_OFFLINE'
  | 'PC_ONLINE';

export interface PcMachine {
  state: PcState;
  /** Momento (ms) en que se entró en el estado actual. */
  since: number;
  /** Sondeos fallidos consecutivos en el estado actual. */
  failures: number;
  /** Última vez que el PC respondió. */
  lastOkAt: number | null;
}

export type PcEvent =
  | { type: 'PROBE_OK'; at: number }
  | { type: 'PROBE_FAIL'; at: number }
  | { type: 'TICK'; at: number };

export interface PcMachineConfig {
  /** Fallos seguidos para pasar de ONLINE a OFFLINE (evita parpadeos). */
  failuresToOffline: number;
  /** Fallos seguidos al abrir la app para concluir que no responde. */
  initialFailuresToOffline: number;
}

export const DEFAULT_MACHINE_CONFIG: PcMachineConfig = {
  failuresToOffline: 3,
  initialFailuresToOffline: 2,
};

export function initialMachine(at: number): PcMachine {
  return { state: 'UNKNOWN', since: at, failures: 0, lastOkAt: null };
}

function enter(m: PcMachine, state: PcState, at: number, patch: Partial<PcMachine> = {}): PcMachine {
  return { ...m, state, since: at, failures: 0, ...patch };
}

export function transition(m: PcMachine, e: PcEvent, cfg: PcMachineConfig = DEFAULT_MACHINE_CONFIG): PcMachine {
  switch (e.type) {
    case 'PROBE_OK':
      return m.state === 'PC_ONLINE' ? { ...m, failures: 0, lastOkAt: e.at } : enter(m, 'PC_ONLINE', e.at, { lastOkAt: e.at });

    case 'PROBE_FAIL': {
      if (m.state === 'PC_OFFLINE') return m;
      const failures = m.failures + 1;
      const limit = m.state === 'UNKNOWN' ? cfg.initialFailuresToOffline : cfg.failuresToOffline;
      return failures >= limit ? enter(m, 'PC_OFFLINE', e.at) : { ...m, failures };
    }

    case 'TICK':
      return m;
  }
}
