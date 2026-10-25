import { useEffect, useRef, useState, type ReactNode } from 'react';
import { LoaderCircle, Lock } from 'lucide-react';
import { buttonActive } from '../core/activity';
import {
  buttonAvailability,
  isConfigurableSlot,
  moveGeneralSlot,
  obsSceneButton,
  resolveButton,
  sectionSelectorSlot,
  sectionSlot,
  type DeckButton,
  type DeckPage,
} from '../core/deck';
import { closeAppWindows, explainUnavailable, runButton } from '../app/executor';
import { useApp } from '../app/store';
import { iconFor } from './icons';
import { useLongPress } from './useLongPress';
import { useMultiTap } from './useMultiTap';
import { appIconName } from '../core/appIcons';
import { VolumeSlider } from './VolumeSlider';

export function PageTabs() {
  const pages = useApp((s) => s.deck.pages);
  const active = useApp((s) => s.activePageId);
  const setActive = useApp((s) => s.setActivePage);
  return (
    <nav className="flex gap-1 overflow-x-auto rounded-lg border border-line/10 bg-surface-1 p-1" aria-label="Páginas">
      {pages.map((p) => {
        const Icon = iconFor(p.icon);
        const on = p.id === active;
        return (
          <button key={p.id} type="button" onClick={() => setActive(p.id)} aria-current={on ? 'page' : undefined}
            className={`flex min-h-[44px] shrink-0 items-center gap-2 rounded-md px-4 text-sm font-medium transition-colors duration-press ${
              on ? 'bg-info text-white' : 'text-fg-2 hover:bg-line/5'}`}>
            <Icon size={18} />
            {p.name}
          </button>
        );
      })}
    </nav>
  );
}

/**
 * `longPress` y `onTap`: solo en huecos de app de General (pulsación larga, triple toque); el resto
 * de botones no cambia. Un botón con variante (ADR-024) se muestra según el estado.
 */
function ButtonTile({ button: original, consumeClick, onTap }: { button: DeckButton; consumeClick?: () => boolean; onTap?: () => void }) {
  const facts = useApp((s) => s.facts);
  const run = useApp((s) => s.buttonState[original.id]);
  const note = useApp((s) => s.buttonNote[original.id]);
  const button = resolveButton(original, facts);
  const av = buttonAvailability(button, facts);
  if (!av.enabled && button.whenUnavailable === 'hide') return <div aria-hidden />;

  const Icon = iconFor(button.icon);
  const active = av.enabled && buttonActive(button, facts);
  // Las apps abiertas no se marcan (ni punto ni borde verde): solo lo activo de verdad, como la escena de OBS.
  const marked = active && button.action.type !== 'app.open';
  const running = run === 'running';
  const caption = running ? 'En curso…'
    : run === 'failed' && note ? note
    : av.enabled ? button.caption
    : av.blockers[0] ?? (av.unknown.length ? 'Comprobando…' : 'No disponible');
  const reasons = av.enabled ? '' : (av.missing.length ? av.missing : av.unknown).join(', ');

  return (
    <button
      type="button"
      data-testid={`btn-${button.id}`}
      data-enabled={av.enabled}
      data-active={active}
      data-run={run ?? 'idle'}
      data-error={run === 'failed' ? note : undefined}
      aria-disabled={!av.enabled}
      aria-busy={running}
      aria-label={av.enabled ? `${button.name}${active ? ' (activo)' : ''}` : `${button.name}. No disponible. Falta: ${reasons}`}
      onClick={() => {
        if (consumeClick?.()) return;
        if (onTap) onTap();
        else if (av.enabled) void runButton(original);
        else explainUnavailable(button);
      }}
      className={`${consumeClick ? 'select-none [-webkit-touch-callout:none] ' : ''}relative flex h-full min-h-[88px] w-full flex-col items-center justify-center gap-1.5 overflow-hidden rounded-lg border p-2 text-center shadow-pd transition-transform duration-press active:scale-[0.96] ${
        av.enabled ? `bg-tile-${button.color} border-line/15 text-white` : 'border-line/10 bg-surface-1 text-fg-3'
      } ${run === 'failed' ? 'animate-shake ring-2 ring-danger' : ''} ${marked ? 'ring-2 ring-ok ring-offset-2 ring-offset-bg' : ''}`}
    >
      {!av.enabled && <Lock size={14} className="absolute right-2 top-2 opacity-70" aria-hidden />}
      {marked && <span className="absolute left-2 top-2 h-2.5 w-2.5 rounded-full bg-ok" aria-hidden />}
      {running
        ? <LoaderCircle className="h-10 w-10 animate-spin" strokeWidth={1.75} aria-hidden />
        : <Icon className="h-10 w-10" strokeWidth={1.75} aria-hidden />}
      <span className="line-clamp-2 text-[15px] font-semibold leading-tight">{button.name}</span>
      {caption && (
        <span className={`line-clamp-1 text-[11px] font-semibold uppercase tracking-wide ${
          run === 'failed' && note ? 'text-white' : av.enabled ? 'text-white/80' : 'text-danger/90'}`}>
          {caption}
        </span>
      )}
    </button>
  );
}

type Drag = { from: number; over: number };

/**
 * Casilla de una página editable (General, ADR-029). Un toque ejecuta (en apps: tres toques cierran sus
 * ventanas, ADR-024). Mantener pulsado: si se arrastra, mueve el botón; si se suelta sin moverlo, abre el
 * selector para elegir qué hay en la casilla. El toque que acaba una pulsación larga nunca ejecuta nada.
 */
function EditableSlot({ slot, button, drag, onHold }: { slot: number; button?: DeckButton; drag: Drag | null; onHold: (slot: number) => void }) {
  const longPress = useLongPress(() => onHold(slot));
  const isApp = button?.action.type === 'app.open';
  const tap = useMultiTap(
    () => {
      if (!button) return;
      if (buttonAvailability(button, useApp.getState().facts).enabled) void runButton(button);
      else explainUnavailable(button);
    },
    () => { if (button && isApp) void closeAppWindows(button); },
  );
  // Icono según la app que abre (logotipo de la marca o mando si es un juego, ADR-027).
  const info = useApp((s) => (isApp ? s.snapshot?.apps.find((a) => a.id === button?.action.params.appId) : undefined));
  const dragging = drag?.from === slot;
  const target = drag !== null && drag.over === slot && drag.from !== slot;

  let content: ReactNode;
  if (!button) {
    content = (
      <div role="button" tabIndex={0} data-testid={`slot-${slot}`} aria-label={`Casilla libre ${slot + 1}: mantén pulsado para elegir qué poner`}
        onKeyDown={(e) => { if (e.key === 'Enter') onHold(slot); }}
        className="h-full w-full rounded-lg border border-dashed border-line/5" />
    );
  } else if (button.widget === 'slider') {
    content = <VolumeSlider button={button} frozen={dragging} />;
  } else {
    const icon = isApp
      ? appIconName({ id: String(button.action.params.appId), name: info?.name ?? button.name.replace(/^Abrir /, ''), kind: info?.kind })
      : 'app';
    const shown = icon === 'app' ? button : { ...button, icon };
    // Los botones normales se ejecutan al momento; las apps esperan un instante por si es un triple toque.
    content = <ButtonTile button={shown} consumeClick={longPress.consumeClick} onTap={isApp ? tap : undefined} />;
  }

  return (
    <div {...longPress.handlers} data-slot={slot} data-dragging={dragging || undefined} data-drop-target={target || undefined}
      className={`relative min-h-0 touch-none select-none rounded-lg transition-[opacity,transform] duration-press [-webkit-touch-callout:none] ${
        dragging ? 'scale-95 opacity-50' : ''} ${target ? 'ring-2 ring-info ring-offset-2 ring-offset-bg' : ''}`}>
      {content}
    </div>
  );
}

/** Arrastrar y soltar en una página editable: sigue el dedo por toda la pantalla tras la pulsación larga. */
function useSlotDrag(page: DeckPage): [Drag | null, (slot: number) => void] {
  const [drag, setDrag] = useState<Drag | null>(null);
  const current = useRef<Drag | null>(null);
  current.current = drag;

  useEffect(() => {
    if (!drag) return;
    const slotAt = (x: number, y: number): number | null => {
      const el = document.elementFromPoint(x, y)?.closest('[data-slot]');
      const n = el ? Number(el.getAttribute('data-slot')) : NaN;
      return Number.isInteger(n) && isConfigurableSlot(page, n) ? n : null;
    };
    const move = (e: PointerEvent) => {
      const over = slotAt(e.clientX, e.clientY);
      if (over !== null && current.current && over !== current.current.over) setDrag({ ...current.current, over });
    };
    const up = () => {
      const d = current.current;
      setDrag(null);
      if (!d) return;
      const s = useApp.getState();
      if (d.over === d.from) {
        s.setAppPickerSlot(d.from); // mantener y soltar sin mover: elegir qué hay en la casilla
        return;
      }
      const next = moveGeneralSlot(s.deck, d.from, d.over);
      if (next) s.setDeck(next); // se guarda solo (persistencia automática)
    };
    window.addEventListener('pointermove', move);
    window.addEventListener('pointerup', up);
    window.addEventListener('pointercancel', up);
    return () => {
      window.removeEventListener('pointermove', move);
      window.removeEventListener('pointerup', up);
      window.removeEventListener('pointercancel', up);
    };
  }, [drag !== null, page]); // eslint-disable-line react-hooks/exhaustive-deps

  return [drag, (slot) => setDrag({ from: slot, over: slot })];
}

/** Casilla que elige la sección (páginas con secciones, ADR-031): misma forma que un botón. */
function SectionTile({ page }: { page: DeckPage }) {
  const active = useApp((s) => s.activeSections[page.id]);
  const open = useApp((s) => s.setSectionPickerPage);
  const section = page.sections?.find((x) => x.id === active) ?? page.sections?.[0];
  const Icon = iconFor(section?.icon ?? 'sections');
  return (
    <button type="button" data-testid="section-tile" aria-label={`Sección: ${section?.name ?? ''}. Cambiar de sección`}
      onClick={() => open(page.id)}
      className="relative flex h-full min-h-[88px] w-full flex-col items-center justify-center gap-1.5 overflow-hidden rounded-lg border border-line/15 bg-tile-slate p-2 text-center text-white shadow-pd transition-transform duration-press active:scale-[0.96]">
      <Icon className="h-10 w-10" strokeWidth={1.75} aria-hidden />
      <span className="line-clamp-2 text-[15px] font-semibold leading-tight">{section?.name}</span>
      <span className="line-clamp-1 text-[11px] font-semibold uppercase tracking-wide text-white/80">Sección</span>
    </button>
  );
}

/** Casillas con las escenas reales de OBS (ADR-031). La escena activa se marca como activa. */
function useObsSceneButtons(page: DeckPage): Map<number, DeckButton> {
  const scenes = useApp((s) => s.snapshot?.obs.state === 'ready' ? s.snapshot.obs.scenes : null);
  const map = new Map<number, DeckButton>();
  if (!page.dynamic || !scenes) return map;
  scenes.slice(0, page.slots.length - page.dynamic.from).forEach((scene, i) => {
    const slot = page.dynamic!.from + i;
    map.set(slot, obsSceneButton(slot, scene));
  });
  return map;
}

export function DeckGrid() {
  const deck = useApp((s) => s.deck);
  const activePageId = useApp((s) => s.activePageId);
  const page = deck.pages.find((p) => p.id === activePageId) ?? deck.pages[0];
  const byId = new Map(deck.buttons.map((b) => [b.id, b]));
  const [drag, startDrag] = useSlotDrag(page);
  const activeSection = useApp((s) => s.activeSections[page.id]);
  const scenes = useObsSceneButtons(page);
  const obsReady = useApp((s) => s.facts['obs.ready']);

  return (
    <section aria-label={`Página ${page.name}`} data-testid="deck-grid"
      className="grid min-h-0 flex-1 gap-grid-gap"
      // Columnas y filas son configurables por página: valores dinámicos.
      style={{
        gridTemplateColumns: `repeat(${page.grid.cols}, minmax(0, 1fr))`,
        gridTemplateRows: `repeat(${page.grid.rows}, minmax(0, 1fr))`,
      }}>
      {page.slots.map((pageSlotId, i) => {
        if (page.sections && i === sectionSelectorSlot(page)) return <SectionTile key={`section-${i}`} page={page} />;
        if (page.dynamic && i >= page.dynamic.from) {
          const scene = scenes.get(i);
          if (scene) return <ButtonTile key={`scene-${i}-${scene.name}`} button={scene} />;
          // Sin escenas que mostrar: la primera casilla explica por qué; el resto, vacías.
          return i === page.dynamic.from && obsReady !== true
            ? <div key={`scenes-${i}`} data-testid="obs-scenes-empty"
                className="flex flex-col items-center justify-center gap-1 rounded-lg border border-line/10 bg-surface-1 p-2 text-center text-fg-3">
                <span className="text-[15px] font-semibold leading-tight">Escenas de OBS</span>
                <span className="text-[11px] font-semibold uppercase tracking-wide text-danger/90">OBS no conectado</span>
              </div>
            : <div key={`empty-${i}`} aria-hidden className="rounded-lg border border-dashed border-line/5" />;
        }
        const id = page.sections ? sectionSlot(page, activeSection, i) : pageSlotId;
        const b = id ? byId.get(id) : undefined;
        if (isConfigurableSlot(page, i)) {
          return <EditableSlot key={`slot-${i}-${b?.id ?? ''}`} slot={i} button={b} drag={drag} onHold={startDrag} />;
        }
        if (b?.widget === 'slider') return <VolumeSlider key={`${i}-${b.id}`} button={b} />;
        return b
          ? <ButtonTile key={`${i}-${activeSection ?? ''}-${b.id}`} button={b} />
          : <div key={`empty-${i}`} aria-hidden className="rounded-lg border border-dashed border-line/5" />;
      })}
    </section>
  );
}
