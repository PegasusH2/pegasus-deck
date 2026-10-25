// Toques seguidos: uno → onSingle (tras una breve espera, por si llegan más); tres → onTriple
// al momento. Dos toques cuentan como uno (abrir la app no debe duplicarse).

import { useEffect, useRef } from 'react';

const WINDOW_MS = 350;

export function useMultiTap(onSingle: () => void, onTriple: () => void): () => void {
  const count = useRef(0);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const handlers = useRef({ onSingle, onTriple });
  handlers.current = { onSingle, onTriple };
  useEffect(() => () => { if (timer.current) clearTimeout(timer.current); }, []);

  return () => {
    count.current += 1;
    if (timer.current) clearTimeout(timer.current);
    if (count.current >= 3) {
      count.current = 0;
      timer.current = null;
      handlers.current.onTriple();
      return;
    }
    timer.current = setTimeout(() => {
      count.current = 0;
      timer.current = null;
      handlers.current.onSingle();
    }, WINDOW_MS);
  };
}
