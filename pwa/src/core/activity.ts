// ¿Lo que controla este botón está "activo" ahora mismo? (indicador ● en el botón)
// Por ejemplo: la app está abierta, la fuente silenciada, la escena es la actual.

import type { DeckButton } from './deck';
import { factValue, type Facts } from './system';

export function buttonActive(button: DeckButton, facts: Facts): boolean {
  const p = button.action.params;
  switch (button.action.type) {
    case 'app.open':
      return factValue(facts, `app.${String(p.appId)}.running`) === true;
    case 'obs.recording.start':
    case 'obs.recording.stop':
      return facts['obs.recording'] === true;
    case 'obs.recording.pause':
      return facts['obs.recordingPaused'] === true;
    case 'obs.streaming.start':
    case 'obs.streaming.stop':
      return facts['obs.streaming'] === true;
    case 'obs.scene.set':
      return facts['obs.scene'] === p.scene;
    case 'audio.master.toggleMute':
      return facts['audio.muted'] === true;
    case 'audio.mic.toggleMute':
      return facts['mic.muted'] === true;
    case 'audio.master.setVolume':
      return facts['audio.volume'] === String(p.percent);
    case 'obs.input.toggleMute':
      return facts[`obs.inputMuted:${String(p.input)}`] === true;
    default:
      return false;
  }
}
