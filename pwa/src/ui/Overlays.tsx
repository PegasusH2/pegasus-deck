import { CircleCheck, Info, TriangleAlert, X } from 'lucide-react';
import { useApp, type ToastKind } from '../app/store';
import { Button, Dialog } from './kit';

const TOAST: Record<ToastKind, { cls: string; Icon: typeof Info }> = {
  info: { cls: 'border-info/30 text-fg', Icon: Info },
  success: { cls: 'border-ok/35 text-fg', Icon: CircleCheck },
  error: { cls: 'border-danger/45 text-fg', Icon: TriangleAlert },
};
const ICON_TONE: Record<ToastKind, string> = { info: 'text-info', success: 'text-ok', error: 'text-danger' };

export function Toasts() {
  const toasts = useApp((s) => s.toasts);
  const dismiss = useApp((s) => s.dismissToast);
  return (
    <div className="pointer-events-none fixed inset-x-0 bottom-0 z-50 flex flex-col items-center gap-2 p-4 pb-[max(1rem,env(safe-area-inset-bottom))]"
      aria-live="polite">
      {toasts.map((t) => {
        const { cls, Icon } = TOAST[t.kind];
        return (
          <div key={t.id} role={t.kind === 'error' ? 'alert' : 'status'} data-testid="toast" data-kind={t.kind}
            className={`pointer-events-auto flex w-full max-w-lg items-start gap-3 rounded-lg border bg-surface-2 px-4 py-3 shadow-pd ${cls}`}>
            <Icon size={20} className={`mt-0.5 shrink-0 ${ICON_TONE[t.kind]}`} />
            <div className="min-w-0 flex-1">
              <p className="font-semibold">{t.title}</p>
              {t.detail && <p className="text-sm text-fg-2">{t.detail}</p>}
            </div>
            <button type="button" aria-label="Cerrar aviso" onClick={() => dismiss(t.id)} className="-m-2 p-2 text-fg-3 hover:text-fg">
              <X size={18} />
            </button>
          </div>
        );
      })}
    </div>
  );
}

export function ConfirmDialog() {
  const confirm = useApp((s) => s.confirm);
  const close = useApp((s) => s.closeConfirm);
  if (!confirm) return null;
  return (
    <Dialog role="alertdialog" title={confirm.title} onClose={() => close(false)}
      footer={<>
        <Button onClick={() => close(false)}>Cancelar</Button>
        <Button variant="primary" autoFocus onClick={() => close(true)}>{confirm.verb}</Button>
      </>}>
      <p>{confirm.text}</p>
    </Dialog>
  );
}
