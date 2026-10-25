import { PlugZap, TriangleAlert } from 'lucide-react';
import { useApp } from '../app/store';
import { Banner } from './kit';

/** Un único aviso contextual, solo cuando hay algo que saber o hacer. */
export function StatusBanner() {
  const pc = useApp((s) => s.pc);
  const link = useApp((s) => s.link);
  const summary = useApp((s) => s.summary);

  if (pc.state !== 'PC_ONLINE') return null;

  if (link.state === 'INCOMPATIBLE') {
    return <Banner tone="bad" role="alert" testId="incompatible-banner" icon={<TriangleAlert size={20} />} title="Versiones incompatibles">{summary.reasons[0]}</Banner>;
  }

  if (link.state === 'DISCONNECTED' && link.attempts > 0) {
    return (
      <Banner tone="warn" testId="reconnect-banner" icon={<PlugZap size={20} />} title="Reconectando con el servicio…">
        El PC responde pero se ha perdido el canal de eventos. Los botones del PC están en pausa hasta recuperarlo.
      </Banner>
    );
  }

  if (summary.status === 'DEGRADED') {
    return (
      <Banner tone="warn" testId="degraded-banner" icon={<TriangleAlert size={20} />} title="Funcionamiento parcial">
        {summary.reasons.join(' ')}
      </Banner>
    );
  }

  return null;
}
