// Sin emparejar no hay panel (ADR-033): en lugar de botones bloqueados, un único aviso con la forma de arreglarlo.

import { KeyRound } from 'lucide-react';
import { useApp } from '../app/store';
import { Button } from './kit';

/** El iPad no tiene token, o el PC lo ha rechazado (revocado o de otro PC). */
export function useNeedsPairing(): boolean {
  const device = useApp((s) => s.device);
  const auth = useApp((s) => s.link.state === 'AUTH_REQUIRED');
  return !device || auth;
}

export function NotPaired() {
  const rejected = useApp((s) => s.device !== null && s.link.state === 'AUTH_REQUIRED' && s.link.reason === 'rejected');
  const setPairingOpen = useApp((s) => s.setPairingOpen);

  return (
    <section data-testid="not-paired" aria-labelledby="not-paired-title"
      className="flex min-h-0 flex-1 flex-col items-center justify-center gap-5 rounded-xl border border-line/10 bg-surface-1 p-8 text-center">
      <div className="flex h-20 w-20 items-center justify-center rounded-full bg-surface-3 text-info" aria-hidden>
        <KeyRound size={40} strokeWidth={1.75} />
      </div>
      <div className="max-w-md space-y-2">
        <h2 id="not-paired-title" className="text-[22px] font-bold text-fg">
          {rejected ? 'El PC ya no reconoce este iPad' : 'Este iPad no está emparejado'}
        </h2>
        <p className="text-sm text-fg-2">
          {rejected
            ? 'Se revocó el emparejamiento o es de otro PC. Vuelve a emparejarlo para usar el panel.'
            : 'Empareja este iPad con el PC para usar el panel.'}
          {' '}En el PC: icono de Pegasus Deck en la bandeja → <b className="text-fg">Emparejar iPad…</b> y escribe aquí el código.
        </p>
      </div>
      <Button variant="primary" className="min-w-[200px]" onClick={() => setPairingOpen(true)}>
        <KeyRound size={18} aria-hidden /> Emparejar
      </Button>
    </section>
  );
}
