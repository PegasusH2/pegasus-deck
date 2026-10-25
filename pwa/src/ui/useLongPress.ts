// Pulsación larga táctil (iPad) o con ratón. Si se mueve el dedo, se cancela.
// Tras una pulsación larga se ignora el clic que el navegador envía al soltar.

import { useEffect, useRef, type PointerEvent } from 'react';

export interface LongPressHandlers {
  handlers: {
    onPointerDown: (e: PointerEvent) => void;
    onPointerMove: (e: PointerEvent) => void;
    onPointerUp: () => void;
    onPointerCancel: () => void;
    onPointerLeave: () => void;
    onContextMenu: (e: { preventDefault: () => void }) => void;
  };
  /** true si el clic actual viene de una pulsación larga (y debe ignorarse). */
  consumeClick: () => boolean;
}

const HOLD_MS = 550;
const MOVE_TOLERANCE_PX = 10;

export function useLongPress(onLongPress: () => void): LongPressHandlers {
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const start = useRef<{ x: number; y: number } | null>(null);
  const fired = useRef(false);
  const callback = useRef(onLongPress);
  callback.current = onLongPress;

  const cancel = () => {
    if (timer.current) clearTimeout(timer.current);
    timer.current = null;
    start.current = null;
  };
  useEffect(() => cancel, []);

  return {
    handlers: {
      onPointerDown: (e) => {
        cancel();
        fired.current = false;
        start.current = { x: e.clientX, y: e.clientY };
        timer.current = setTimeout(() => {
          timer.current = null;
          fired.current = true;
          callback.current();
        }, HOLD_MS);
      },
      onPointerMove: (e) => {
        if (start.current && Math.hypot(e.clientX - start.current.x, e.clientY - start.current.y) > MOVE_TOLERANCE_PX) cancel();
      },
      onPointerUp: cancel,
      onPointerCancel: cancel,
      onPointerLeave: cancel,
      // Evita el menú contextual (ratón) y el de "copiar" de iOS.
      onContextMenu: (e) => e.preventDefault(),
    },
    consumeClick: () => {
      const was = fired.current;
      fired.current = false;
      return was;
    },
  };
}
