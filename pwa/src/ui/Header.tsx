import { Activity, Monitor, Server, Settings } from 'lucide-react';
import { useApp } from '../app/store';
import { IconButton, StatusPill, toneText } from './kit';
import { iconFor } from './icons';
import { Logo } from './Logo';
import { globalLine, linkStatus, obsStatus, pcStatus } from './status';

const ObsIcon = iconFor('obs');

export function Header() {
  const pc = useApp((s) => s.pc);
  const link = useApp((s) => s.link);
  const snapshot = useApp((s) => s.snapshot);
  const summary = useApp((s) => s.summary);
  const pageName = useApp((s) => s.deck.pages.find((p) => p.id === s.activePageId)?.name ?? '');
  const setView = useApp((s) => s.setView);
  const setStatusOpen = useApp((s) => s.setStatusOpen);
  const global = globalLine(summary);

  return (
    <header className="flex items-center justify-between gap-4">
      <div className="flex min-w-0 items-center gap-3">
        <Logo className="h-9 w-9 shrink-0 text-fg" />
        <div className="min-w-0">
          <h1 className="text-lg font-bold leading-tight tracking-wide text-fg">PEGASUS DECK</h1>
          <p className="truncate text-xs text-fg-3" data-testid="global-status" data-status={summary.status}>
            <span className={toneText(global.tone)}>{global.label}</span>
            <span> · Página {pageName}</span>
          </p>
        </div>
      </div>
      <div className="flex items-center gap-4">
        <StatusPill testId="chip-pc" name="PC" icon={<Monitor size={16} />} {...pcStatus(pc)} />
        <StatusPill testId="chip-service" name="Servicio" icon={<Server size={16} />} {...linkStatus(pc, link)} />
        <StatusPill testId="chip-obs" name="OBS" icon={<ObsIcon size={16} />} {...obsStatus(snapshot)} />
        <div className="flex">
          <IconButton label="Estados del sistema" onClick={() => setStatusOpen(true)}><Activity size={20} /></IconButton>
          <IconButton label="Configuración" onClick={() => setView('settings')}><Settings size={20} /></IconButton>
        </div>
      </div>
    </header>
  );
}
