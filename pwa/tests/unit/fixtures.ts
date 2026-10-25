// Ayudas compartidas por las pruebas unitarias.
import { initialLink, type LinkMachine, type LinkState } from '../../src/core/linkMachine';
import { initialMachine, type PcMachine, type PcState } from '../../src/core/pcMachine';
import type { AppState, ServiceSnapshot } from '../../src/core/system';

export function machine(state: PcState): PcMachine {
  return { ...initialMachine(0), state };
}

export function link(state: LinkState, patch: Partial<LinkMachine> = {}): LinkMachine {
  return { ...initialLink(0), state, ...patch };
}

export function snapshot(patch: {
  session?: boolean;
  obs?: Partial<ServiceSnapshot['obs']>;
  apps?: Record<string, AppState>;
  appsDetection?: ServiceSnapshot['appsDetection'];
  audio?: Partial<ServiceSnapshot['audio']>;
  mic?: Partial<ServiceSnapshot['mic']>;
  media?: ServiceSnapshot['media']['state'];
} = {}): ServiceSnapshot {
  const apps = { obs: 'installed', chrome: 'installed', davinci: 'installed', explorer: 'installed', ...patch.apps } as Record<string, AppState>;
  return {
    protocol: 2,
    service: { name: 'pegasus-deck', version: 't' },
    windows: { booted: true, hostname: 'PC', uptimeSec: 10 },
    session: { ready: patch.session ?? true },
    obs: {
      state: 'unavailable', recording: false, recordingPaused: false, streaming: false,
      scene: 'Juego', scenes: ['Juego', 'Charla'], inputs: [{ name: 'Mic/Aux', muted: false }], ...patch.obs,
    },
    apps: Object.entries(apps).map(([id, state]) => ({ id, name: id, state })),
    appsDetection: patch.appsDetection ?? ((patch.session ?? true) ? 'ok' : 'unavailable'),
    audio: { state: 'ready', muted: false, volume: 40, ...patch.audio },
    mic: { state: 'ready', muted: false, ...patch.mic },
    media: { state: patch.media ?? 'playing' },
  };
}
