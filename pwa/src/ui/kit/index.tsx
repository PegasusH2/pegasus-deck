// Componentes base del sistema de diseño. Las pantallas los componen y no
// reimplementan estilos (skill pegasus-design-system).

import { useEffect, type ButtonHTMLAttributes, type ReactNode } from 'react';
import { X } from 'lucide-react';

type Variant = 'primary' | 'secondary' | 'danger' | 'success' | 'ghost';

const VARIANT: Record<Variant, string> = {
  primary: 'bg-info text-white hover:bg-info/90',
  secondary: 'border border-line/15 bg-surface-2 text-fg hover:bg-surface-3',
  danger: 'border border-danger/40 bg-danger/10 text-danger hover:bg-danger/20',
  success: 'bg-ok/90 text-white hover:bg-ok',
  ghost: 'text-fg-2 hover:bg-line/5',
};

export function Button({ variant = 'secondary', className = '', children, ...rest }: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: Variant }) {
  return (
    <button type="button" {...rest}
      className={`inline-flex min-h-[44px] items-center justify-center gap-2 rounded-md px-4 text-sm font-semibold transition-colors duration-press disabled:opacity-50 ${VARIANT[variant]} ${className}`}>
      {children}
    </button>
  );
}

export function IconButton({ label, children, className = '', ...rest }: ButtonHTMLAttributes<HTMLButtonElement> & { label: string }) {
  return (
    <button type="button" aria-label={label} title={label} {...rest}
      className={`inline-flex h-11 w-11 items-center justify-center rounded-full text-fg-2 transition-colors duration-press hover:bg-line/10 hover:text-fg ${className}`}>
      {children}
    </button>
  );
}

export type Tone = 'ok' | 'warn' | 'bad' | 'info' | 'idle';

const DOT: Record<Tone, string> = {
  ok: 'bg-ok', warn: 'bg-warn', bad: 'bg-danger', info: 'bg-info', idle: 'bg-fg-3',
};
const TEXT: Record<Tone, string> = {
  ok: 'text-ok', warn: 'text-warn', bad: 'text-danger', info: 'text-info', idle: 'text-fg-2',
};

export function toneText(tone: Tone): string {
  return TEXT[tone];
}

export function StatusDot({ tone }: { tone: Tone }) {
  return <span aria-hidden className={`inline-block h-2 w-2 shrink-0 rounded-full ${DOT[tone]}`} />;
}

/** Indicador compacto "Nombre: estado". */
export function StatusPill({ name, label, tone, icon, testId }: { name: string; label: string; tone: Tone; icon?: ReactNode; testId?: string }) {
  return (
    <div data-testid={testId} data-tone={tone} className="flex items-center gap-1.5 whitespace-nowrap text-sm">
      {icon && <span className={TEXT[tone]}>{icon}</span>}
      <span className="text-fg-3">{name}</span>
      <StatusDot tone={tone} />
      <span className={`font-medium ${TEXT[tone]}`}>{label}</span>
    </div>
  );
}

const BANNER: Record<Tone, string> = {
  ok: 'border-ok/30 bg-ok/10',
  warn: 'border-warn/30 bg-warn/10',
  bad: 'border-danger/35 bg-danger/10',
  info: 'border-info/30 bg-info/10',
  idle: 'border-line/10 bg-surface-1',
};

export function Banner({ tone, icon, title, children, actions, testId, role = 'status' }: {
  tone: Tone; icon?: ReactNode; title: string; children?: ReactNode; actions?: ReactNode; testId?: string; role?: 'status' | 'alert';
}) {
  return (
    <div role={role} data-testid={testId}
      className={`flex flex-wrap items-center justify-between gap-3 rounded-lg border px-4 py-3 ${BANNER[tone]}`}>
      <div className="flex min-w-0 items-start gap-3">
        {icon && <span className={`mt-0.5 shrink-0 ${TEXT[tone]}`}>{icon}</span>}
        <div className="min-w-0">
          <p className="font-semibold text-fg">{title}</p>
          {children && <div className="text-sm text-fg-2">{children}</div>}
        </div>
      </div>
      {actions && <div className="flex shrink-0 flex-wrap gap-2">{actions}</div>}
    </div>
  );
}

export function Dialog({ title, children, footer, onClose, role = 'dialog', testId }: {
  title: string; children: ReactNode; footer?: ReactNode; onClose?: () => void; role?: 'dialog' | 'alertdialog'; testId?: string;
}) {
  useEffect(() => {
    if (!onClose) return;
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose();
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-bg/85 p-4">
      <div role={role} aria-modal="true" aria-label={title} data-testid={testId}
        className="w-full max-w-md rounded-xl border border-line/10 bg-surface-1 p-6 shadow-pd">
        <div className="mb-3 flex items-start justify-between gap-3">
          <h2 className="text-lg font-semibold text-fg">{title}</h2>
          {onClose && <IconButton label="Cerrar" onClick={onClose} className="-mr-2 -mt-2"><X size={20} /></IconButton>}
        </div>
        <div className="text-fg-2">{children}</div>
        {footer && <div className="mt-6 grid grid-cols-2 gap-3">{footer}</div>}
      </div>
    </div>
  );
}

export function Sheet({ title, onClose, actions, children }: { title: string; onClose: () => void; actions?: ReactNode; children: ReactNode }) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose();
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);
  return (
    <div className="fixed inset-0 z-40 flex justify-end bg-bg/60" onClick={onClose}>
      <aside role="dialog" aria-label={title} onClick={(e) => e.stopPropagation()}
        className="flex h-full w-full max-w-md flex-col gap-3 overflow-y-auto border-l border-line/10 bg-surface-1 p-5 pt-[max(1.25rem,env(safe-area-inset-top))]">
        <div className="mb-1 flex items-center justify-between">
          <h2 className="text-xl font-bold text-fg">{title}</h2>
          <div className="flex">
            {actions}
            <IconButton label="Cerrar" onClick={onClose}><X size={20} /></IconButton>
          </div>
        </div>
        {children}
      </aside>
    </div>
  );
}

export const inputClass = 'min-h-[44px] w-full rounded-md border border-line/10 bg-bg px-3 py-2 text-fg outline-none focus:border-info';

export function Field({ label, error, hint, children }: { label: string; error?: string; hint?: string; children: ReactNode }) {
  return (
    <label className="block space-y-1.5">
      <span className="text-sm text-fg-2">{label}</span>
      {children}
      {error ? <span className="block text-xs text-danger">{error}</span>
        : hint ? <span className="block text-xs text-fg-3">{hint}</span> : null}
    </label>
  );
}

export function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="space-y-4 rounded-xl border border-line/10 bg-surface-1 p-5">
      <h3 className="text-lg font-semibold text-fg">{title}</h3>
      {children}
    </section>
  );
}

export function Segmented<T extends string>({ value, options, onChange }: {
  value: T; options: { value: T; label: string; icon?: ReactNode }[]; onChange: (v: T) => void;
}) {
  return (
    <div className="grid gap-2" style={{ gridTemplateColumns: `repeat(${options.length}, minmax(0, 1fr))` }} role="radiogroup">
      {options.map((o) => (
        <button key={o.value} type="button" role="radio" aria-checked={value === o.value} onClick={() => onChange(o.value)}
          className={`flex min-h-[44px] items-center justify-center gap-2 rounded-md border px-3 font-medium transition-colors duration-press ${
            value === o.value ? 'border-info bg-info/15 text-fg' : 'border-line/10 text-fg-2 hover:bg-line/5'}`}>
          {o.icon} {o.label}
        </button>
      ))}
    </div>
  );
}
