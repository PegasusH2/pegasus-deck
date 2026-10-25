import { AppWindow, KeyRound, Monitor, RefreshCw, ScrollText, Server, UserRound } from 'lucide-react';
import { connection } from '../app/connection';
import { recentLog } from '../app/log';
import { useApp } from '../app/store';
import { IconButton, Sheet, StatusDot, toneText } from './kit';
import { iconFor } from './icons';
import {
  APP_STATE_LABEL,
  formatDuration,
  globalLine,
  linkStatus,
  obsStatus,
  pcStatus,
  sessionStatus,
  type StatusLine,
} from './status';

const ObsIcon = iconFor('obs');

function Row({ icon, title, detail, status, testId }: {
  icon: React.ReactNode; title: string; detail?: string; status: StatusLine; testId?: string;
}) {
  return (
    <div className="flex items-center gap-4 rounded-lg border border-line/10 bg-surface-2 p-3" data-testid={testId}>
      <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-md bg-surface-3 text-fg-2">{icon}</div>
      <div className="min-w-0 flex-1">
        <p className="font-semibold text-fg">{title}</p>
        {detail && <p className="truncate text-sm text-fg-3">{detail}</p>}
      </div>
      <div className={`flex items-center gap-2 text-sm font-medium ${toneText(status.tone)}`}>
        <StatusDot tone={status.tone} />
        {status.label}
      </div>
    </div>
  );
}

export function StatusSheet() {
  const open = useApp((s) => s.statusOpen);
  const setOpen = useApp((s) => s.setStatusOpen);
  const pc = useApp((s) => s.pc);
  const link = useApp((s) => s.link);
  const snapshot = useApp((s) => s.snapshot);
  const summary = useApp((s) => s.summary);
  const device = useApp((s) => s.device);
  const settings = useApp((s) => s.settings);
  const now = useApp((s) => s.now);
  if (!open) return null;

  const lastSeen = pc.lastOkAt ? `Último contacto hace ${formatDuration(now - pc.lastOkAt)}` : 'Sin contacto todavía';
  const obs = snapshot?.obs;
  const log = recentLog().slice(-12).reverse();

  return (
    <Sheet title="Estados del sistema" onClose={() => setOpen(false)}
      actions={<IconButton label="Comprobar ahora" onClick={() => void connection.probeNow()}><RefreshCw size={20} /></IconButton>}>
      <p className="text-sm text-fg-2">
        Estado general: <span className={toneText(globalLine(summary).tone)}>{globalLine(summary).label}</span>
        {summary.reasons.length > 0 && <span className="block text-fg-3">{summary.reasons.join(' ')}</span>}
      </p>

      <Row testId="status-pc" icon={<Monitor size={20} />} title={settings.name}
        detail={[snapshot?.windows.hostname, settings.host, lastSeen].filter(Boolean).join(' · ')} status={pcStatus(pc)} />
      <Row testId="status-service" icon={<Server size={20} />} title="Servicio Pegasus Deck"
        detail={snapshot ? `Versión ${snapshot.service.version} · eventos en tiempo real` : link.attempts > 0 ? `Reintento ${link.attempts}` : 'Sin canal de eventos'}
        status={linkStatus(pc, link)} />
      <Row testId="status-windows" icon={<UserRound size={20} />} title="Windows"
        detail={snapshot ? `Encendido hace ${formatDuration(snapshot.windows.uptimeSec * 1000)}` : 'Sin datos'}
        status={sessionStatus(snapshot)} />
      <Row testId="status-obs" icon={<ObsIcon size={20} />} title="OBS Studio"
        detail={obs?.state === 'ready'
          ? `Escena: ${obs.scene ?? '—'} · Grabación: ${obs.recording ? (obs.recordingPaused ? 'en pausa' : 'activa') : 'detenida'} · Streaming: ${obs.streaming ? 'activo' : 'detenido'}`
          : 'Sin conexión con OBS'}
        status={obsStatus(snapshot)} />

      <div className="rounded-lg border border-line/10 bg-surface-2 p-3">
        <p className="mb-2 flex items-center gap-2 font-semibold text-fg"><AppWindow size={18} /> Aplicaciones autorizadas</p>
        {!snapshot && <p className="text-sm text-fg-3">Disponible cuando el servicio esté conectado.</p>}
        {snapshot?.apps.length === 0 && <p className="text-sm text-fg-3">No hay aplicaciones autorizadas o no hay sesión de Windows.</p>}
        <ul className="space-y-2">
          {snapshot?.apps.map((a) => {
            const st = APP_STATE_LABEL[a.state];
            return (
              <li key={a.id} className="flex items-center justify-between text-sm" data-testid={`status-app-${a.id}`}>
                <span className="text-fg">{a.name}</span>
                <span className={`flex items-center gap-2 ${toneText(st.tone)}`}><StatusDot tone={st.tone} />{st.label}</span>
              </li>
            );
          })}
        </ul>
      </div>

      <div className="rounded-lg border border-line/10 bg-surface-2 p-3 text-sm">
        <p className="mb-1 flex items-center gap-2 font-semibold text-fg"><KeyRound size={18} /> Emparejamiento</p>
        <p className="text-fg-3">{device ? `«${device.deviceName}» · id ${device.deviceId}` : 'Este iPad no está emparejado.'}</p>
      </div>

      <details className="rounded-lg border border-line/10 bg-surface-2 p-3 text-sm">
        <summary className="flex cursor-pointer items-center gap-2 font-semibold text-fg"><ScrollText size={18} /> Diagnóstico</summary>
        <ul className="mt-2 space-y-1 font-mono text-xs text-fg-3">
          {log.length === 0 && <li>Sin entradas.</li>}
          {log.map((e, i) => (
            <li key={i}>{new Date(e.at).toLocaleTimeString()} · {e.level} · {e.event}{e.data ? ` ${JSON.stringify(e.data)}` : ''}</li>
          ))}
        </ul>
      </details>

      <p className="px-1 text-xs text-fg-3">
        Una PWA no puede hacer ping: el PC se considera encendido cuando el servicio Pegasus Deck responde.
      </p>
    </Sheet>
  );
}
