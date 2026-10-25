// Deslizador de volumen estilo iOS (Centro de control): se arrastra arriba/abajo
// sobre el botón y el relleno muestra el nivel. Envía audio.master.setVolume
// (lista blanca, 0-100) con una sola petición en curso; el valor que se muestra
// al soltar es el que confirma el PC.

import { useEffect, useRef, useState, type KeyboardEvent, type PointerEvent } from 'react';
import { Lock } from 'lucide-react';
import { buttonAvailability, type DeckButton } from '../core/deck';
import { executeAction } from '../app/actionEngine';
import { explainUnavailable } from '../app/executor';
import { useApp } from '../app/store';
import { iconFor } from './icons';

const SEND_EVERY_MS = 120;
/** Tras soltar, se mantiene el valor local hasta que el PC lo confirme (o pase este tiempo). */
const HOLD_LOCAL_MS = 2500;
const KEY_STEP = 5;

function clamp(v: number): number {
  return Math.min(100, Math.max(0, Math.round(v)));
}

/** `frozen`: su casilla se está moviendo (arrastrar y soltar): no cambia el volumen. */
export function VolumeSlider({ button, frozen = false }: { button: DeckButton; frozen?: boolean }) {
  const facts = useApp((s) => s.facts);
  const av = buttonAvailability(button, facts);
  const reported = typeof facts['audio.volume'] === 'string' ? Number(facts['audio.volume']) : null;
  const muted = facts['audio.muted'] === true;

  const [local, setLocal] = useState<number | null>(null);
  // last: último valor del arrastre (no el del render: al soltar rápido el estado puede ir un paso por detrás).
  const drag = useRef<{ y: number; value: number; height: number; moved: boolean; last: number } | null>(null);
  const sender = useRef<{ inflight: boolean; pending: number | null; lastSent: number }>({ inflight: false, pending: null, lastSent: 0 });
  const release = useRef<ReturnType<typeof setTimeout> | null>(null);

  // El PC ya informa del valor enviado: se deja de mostrar el local.
  useEffect(() => {
    if (local !== null && !drag.current && reported === local) setLocal(null);
  }, [reported, local]);
  useEffect(() => () => { if (release.current) clearTimeout(release.current); }, []);

  const send = (percent: number) => {
    const s = sender.current;
    if (s.inflight) {
      s.pending = percent;
      return;
    }
    s.inflight = true;
    s.lastSent = Date.now();
    void executeAction('audio.master.setVolume', { percent }, { label: button.name }).then((outcome) => {
      s.inflight = false;
      if (outcome.status !== 'completed') {
        s.pending = null;
        setLocal(null);
        return;
      }
      if (s.pending !== null) {
        const next = s.pending;
        s.pending = null;
        if (next !== percent) send(next);
      }
    });
  };

  const holdLocalThenRelease = () => {
    if (release.current) clearTimeout(release.current);
    release.current = setTimeout(() => setLocal(null), HOLD_LOCAL_MS);
  };

  const value = local ?? reported ?? 0;
  const Icon = iconFor(muted || value === 0 ? 'volume-off' : value < 50 ? 'volume-low' : 'volume');

  const onPointerDown = (e: PointerEvent<HTMLDivElement>) => {
    if (!av.enabled) return;
    e.currentTarget.setPointerCapture(e.pointerId);
    drag.current = { y: e.clientY, value, height: e.currentTarget.getBoundingClientRect().height || 1, moved: false, last: value };
  };
  const onPointerMove = (e: PointerEvent<HTMLDivElement>) => {
    const d = drag.current;
    if (!d || frozen) return;
    const next = clamp(d.value + ((d.y - e.clientY) / d.height) * 100);
    if (!d.moved && Math.abs(d.y - e.clientY) < 4) return;
    d.moved = true;
    d.last = next;
    setLocal(next);
    if (Date.now() - sender.current.lastSent >= SEND_EVERY_MS) send(next);
    else sender.current.pending = next;
  };
  const onPointerUp = () => {
    const d = drag.current;
    drag.current = null;
    if (!d?.moved) return;
    send(d.last);
    holdLocalThenRelease();
  };
  const onKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
    if (!av.enabled) return;
    const delta = e.key === 'ArrowUp' || e.key === 'ArrowRight' ? KEY_STEP : e.key === 'ArrowDown' || e.key === 'ArrowLeft' ? -KEY_STEP : 0;
    if (!delta) return;
    e.preventDefault();
    const next = clamp(value + delta);
    setLocal(next);
    send(next);
    holdLocalThenRelease();
  };

  const caption = av.enabled
    ? `${muted ? 'Silenciado · ' : ''}${value} %`
    : av.blockers[0] ?? (av.unknown.length ? 'Comprobando…' : 'No disponible');
  const reasons = av.enabled ? '' : (av.missing.length ? av.missing : av.unknown).join(', ');

  return (
    <div
      role="slider"
      tabIndex={0}
      data-testid={`btn-${button.id}`}
      data-enabled={av.enabled}
      aria-label={av.enabled ? button.name : `${button.name}. No disponible. Falta: ${reasons}`}
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={value}
      aria-valuetext={`${value} %${muted ? ', silenciado' : ''}`}
      aria-disabled={!av.enabled}
      onPointerDown={onPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={onPointerUp}
      onPointerCancel={onPointerUp}
      onKeyDown={onKeyDown}
      onClick={() => { if (!av.enabled) explainUnavailable(button); }}
      className={`relative flex h-full min-h-[88px] w-full touch-none select-none flex-col items-center justify-center gap-1.5 overflow-hidden rounded-lg border p-2 text-center shadow-pd [-webkit-touch-callout:none] ${
        av.enabled ? `bg-tile-${button.color} border-line/15 text-white` : 'border-line/10 bg-surface-1 text-fg-3'}`}
    >
      {av.enabled && (
        // Relleno desde abajo (valor dinámico: altura en %).
        <span aria-hidden data-testid="volume-fill"
          className={`absolute inset-x-0 bottom-0 transition-[height] duration-press ${muted ? 'bg-white/10' : 'bg-white/25'}`}
          style={{ height: `${value}%` }} />
      )}
      {!av.enabled && <Lock size={14} className="absolute right-2 top-2 opacity-70" aria-hidden />}
      <Icon className="relative h-10 w-10" strokeWidth={1.75} aria-hidden />
      <span className="relative line-clamp-2 text-[15px] font-semibold leading-tight">{button.name}</span>
      <span className={`relative line-clamp-1 text-[11px] font-semibold uppercase tracking-wide ${av.enabled ? 'text-white/80' : 'text-danger/90'}`}>
        {caption}
      </span>
    </div>
  );
}
