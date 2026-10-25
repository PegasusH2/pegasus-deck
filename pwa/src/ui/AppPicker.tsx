// Selector de la app de un hueco de General (se abre con una pulsación larga).
// Solo ofrece las apps de la lista blanca que envía el PC: nunca rutas ni
// ejecutables escritos en el iPad.

import { Check } from 'lucide-react';
import { appIconName } from '../core/appIcons';
import { assignGeneralSlot, GENERAL_CONTROL_BUTTONS, GENERAL_CONTROL_IDS, GENERAL_PAGE_ID, type SlotChoice } from '../core/deck';
import { useApp } from '../app/store';
import { iconFor } from './icons';
import { Button, Sheet } from './kit';

const STATE_TEXT: Record<string, string> = {
  not_installed: 'No instalada',
  installed: 'Cerrada',
  starting: 'Abriéndose',
  running: 'Abierta',
  ready: 'Abierta',
};


export function AppPickerSheet() {
  const slot = useApp((s) => s.appPickerSlot);
  const close = useApp((s) => s.setAppPickerSlot);
  const deck = useApp((s) => s.deck);
  const snapshot = useApp((s) => s.snapshot);
  if (slot === null) return null;

  const page = deck.pages.find((p) => p.id === GENERAL_PAGE_ID);
  const currentId = page?.slots[slot] ?? null;
  const current = currentId ? deck.buttons.find((b) => b.id === currentId) : undefined;
  const currentApp = current?.action.type === 'app.open' ? String(current.action.params.appId) : null;
  const apps = snapshot?.session.ready ? snapshot.apps : [];
  // Controles del PC que no están ya en la página (nunca se duplican).
  const controls = GENERAL_CONTROL_IDS
    .filter((id) => !page?.slots.includes(id))
    .map((id) => deck.buttons.find((b) => b.id === id) ?? GENERAL_CONTROL_BUTTONS.find((b) => b.id === id))
    .filter((b) => b !== undefined);

  // Sin avisos (ADR-029): el cambio se ve en la propia casilla.
  const apply = (choice: SlotChoice) => {
    const s = useApp.getState();
    const next = assignGeneralSlot(s.deck, slot, choice);
    if (next) s.setDeck(next);
    close(null);
  };

  return (
    <Sheet title={`Casilla ${slot + 1}`} onClose={() => close(null)}>
      {controls.length > 0 && (
        <>
          <h3 className="text-sm font-semibold text-fg">Controles del PC</h3>
          <ul className="space-y-2" aria-label="Controles del PC">
            {controls.map((b) => {
              const Icon = iconFor(b.icon);
              return (
                <li key={b.id}>
                  <button type="button" onClick={() => apply({ control: b.id })}
                    className="flex min-h-[52px] w-full items-center gap-3 rounded-md border border-line/10 px-3 text-left transition-colors duration-press hover:bg-line/5">
                    <Icon size={22} className="shrink-0 text-fg-2" aria-hidden />
                    <span className="font-medium text-fg">{b.name}</span>
                  </button>
                </li>
              );
            })}
          </ul>
          <h3 className="pt-2 text-sm font-semibold text-fg">Aplicaciones</h3>
        </>
      )}
      <p className="text-sm text-fg-2">
        Elige qué aplicación abre este botón. Solo aparecen las autorizadas en el PC. Para añadir otra, en el PC:
        icono de Pegasus Deck en la bandeja → <span className="text-fg">Aplicaciones autorizadas…</span>
      </p>
      {apps.length === 0 && (
        <p className="rounded-md border border-line/10 bg-bg px-3 py-2 text-sm text-fg-2" data-testid="picker-empty">
          Conecta con el PC (con la sesión de Windows iniciada) para ver las aplicaciones autorizadas.
        </p>
      )}
      <ul className="space-y-2" aria-label="Aplicaciones autorizadas">
        {apps.map((app) => {
          const Icon = iconFor(appIconName(app));
          const selected = app.id === currentApp;
          return (
            <li key={app.id}>
              <button type="button" onClick={() => apply({ app: { id: app.id, name: app.name, kind: app.kind } })} aria-pressed={selected}
                className={`flex min-h-[52px] w-full items-center gap-3 rounded-md border px-3 text-left transition-colors duration-press ${
                  selected ? 'border-info bg-info/15' : 'border-line/10 hover:bg-line/5'}`}>
                <Icon size={22} className="shrink-0 text-fg-2" aria-hidden />
                <span className="min-w-0 flex-1">
                  <span className="block font-medium text-fg">{app.name}</span>
                  <span className="block text-xs text-fg-3">{STATE_TEXT[app.state] ?? app.state}</span>
                </span>
                {selected && <Check size={20} className="text-info" aria-hidden />}
              </button>
            </li>
          );
        })}
      </ul>
      {current && (
        <Button variant="danger" onClick={() => apply(null)}>Dejar la casilla vacía</Button>
      )}
    </Sheet>
  );
}
