import { useState } from 'react';
import { pairWithCode } from '../app/pairing';
import { useApp } from '../app/store';
import { Button, Dialog, Field, inputClass } from './kit';

export function PairingDialog() {
  const open = useApp((s) => s.pairingOpen);
  const setOpen = useApp((s) => s.setPairingOpen);
  const toast = useApp((s) => s.toast);
  const pcOnline = useApp((s) => s.pc.state === 'PC_ONLINE');
  const [code, setCode] = useState('');
  const [name, setName] = useState('iPad');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  if (!open) return null;

  const close = () => { setOpen(false); setError(null); setCode(''); };
  const submit = async () => {
    setBusy(true);
    const r = await pairWithCode(code, name);
    setBusy(false);
    if (r.ok) {
      close();
      toast('success', 'iPad emparejado', 'Conectando con el PC…');
    } else {
      setError(r.error);
    }
  };

  return (
    <Dialog title="Emparejar con el PC" onClose={close} testId="pairing-dialog"
      footer={<>
        <Button onClick={close}>Cancelar</Button>
        <Button variant="primary" disabled={busy || code.length !== 6 || !pcOnline} onClick={() => void submit()}>
          {busy ? 'Emparejando…' : 'Emparejar'}
        </Button>
      </>}>
      <div className="space-y-4">
        <p className="text-sm">
          En el PC, abre el menú de Pegasus Deck y elige <b className="text-fg">Emparejar iPad</b>
          {' '}(o ejecuta <code className="text-fg">PegasusDeck.Service pair</code>). Aparecerá un código de 6 cifras válido 2 minutos.
        </p>
        {!pcOnline && <p className="text-sm text-warn">El PC tiene que estar encendido para emparejar.</p>}
        <Field label="Código del PC" error={error ?? undefined}>
          <input className={`${inputClass} text-center font-mono text-2xl tracking-[0.4em]`} data-testid="pair-code"
            value={code} onChange={(e) => setCode(e.target.value.replace(/\D/g, '').slice(0, 6))}
            inputMode="numeric" autoComplete="one-time-code" autoFocus aria-label="Código de emparejamiento" />
        </Field>
        <Field label="Nombre de este dispositivo" hint="Para reconocerlo en la lista de dispositivos del PC.">
          <input className={inputClass} value={name} maxLength={40} onChange={(e) => setName(e.target.value)} />
        </Field>
      </div>
    </Dialog>
  );
}
