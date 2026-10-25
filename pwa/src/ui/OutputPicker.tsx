// Lista de salidas de audio del PC (ADR-035). Solo las que informa el PC (ids opacos); la marcada es la
// predeterminada real de Windows. Al elegir, se espera la confirmación del PC: si falla, se dice aquí.

import { useState } from 'react';
import { Check, LoaderCircle } from 'lucide-react';
import { executeAction } from '../app/actionEngine';
import { useApp } from '../app/store';
import { iconFor } from './icons';
import { Sheet } from './kit';

export function OutputPickerSheet() {
  const open = useApp((s) => s.outputPickerOpen);
  const setOpen = useApp((s) => s.setOutputPickerOpen);
  const outputs = useApp((s) => (s.snapshot?.session.ready ? s.snapshot.outputs : null));
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  if (!open) return null;

  const close = () => {
    setError(null);
    setOpen(false);
  };
  const devices = outputs?.state === 'ready' ? outputs.devices : [];
  const Speaker = iconFor('speaker');

  const choose = async (id: string, name: string) => {
    setBusy(id);
    setError(null);
    const outcome = await executeAction('audio.output.set', { device: id }, { label: name });
    setBusy(null);
    if (outcome.status === 'completed') close();
    else setError(outcome.status === 'failed' ? outcome.message : 'No se ha cambiado la salida.');
  };

  return (
    <Sheet title="Salida de audio" onClose={close}>
      <p className="text-sm text-fg-2">Elige por dónde suena el PC. Cambia la salida predeterminada de Windows.</p>
      {devices.length === 0 && (
        <p className="rounded-md border border-line/10 bg-bg px-3 py-2 text-sm text-fg-2" data-testid="outputs-empty">
          El PC no informa de salidas de audio ahora mismo.
        </p>
      )}
      <ul className="space-y-2" aria-label="Salidas de audio">
        {devices.map((d) => (
          <li key={d.id}>
            <button type="button" aria-pressed={d.default} disabled={busy !== null}
              onClick={() => void choose(d.id, d.name)}
              className={`flex min-h-[52px] w-full items-center gap-3 rounded-md border px-3 text-left transition-colors duration-press disabled:opacity-60 ${
                d.default ? 'border-info bg-info/15' : 'border-line/10 hover:bg-line/5'}`}>
              <Speaker size={22} className="shrink-0 text-fg-2" aria-hidden />
              <span className="min-w-0 flex-1 font-medium text-fg">{d.name}</span>
              {busy === d.id
                ? <LoaderCircle size={20} className="animate-spin text-fg-2" aria-hidden />
                : d.default && <Check size={20} className="text-info" aria-hidden />}
            </button>
          </li>
        ))}
      </ul>
      {error && <p role="alert" className="text-sm text-danger" data-testid="outputs-error">{error}</p>}
    </Sheet>
  );
}
