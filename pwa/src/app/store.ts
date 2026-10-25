import { create } from 'zustand';
import {
  initialMachine,
  transition,
  type PcEvent,
  type PcMachine,
} from '../core/pcMachine';
import { initialLink, linkTransition, type LinkEvent, type LinkMachine } from '../core/linkMachine';
import { buildFacts, type Facts, type ServiceSnapshot, type SystemView } from '../core/system';
import { globalStatus, type GlobalSummary } from '../core/globalStatus';
import { DEFAULT_PC_SETTINGS, type PcSettings } from '../core/settings';
import { DEFAULT_DECK, type DeckConfig } from '../core/deck';

export type ToastKind = 'info' | 'success' | 'error';

export interface Toast {
  id: number;
  kind: ToastKind;
  title: string;
  detail?: string;
}

export interface ConfirmRequest {
  title: string;
  text: string;
  verb: string;
  resolve: (ok: boolean) => void;
}

export interface DeviceCredentials {
  deviceId: string;
  token: string;
  deviceName: string;
  pairedAt: number;
}

/** Estado visual transitorio de un botón. */
export type ButtonRunState = 'running' | 'failed';

export type View = 'deck' | 'settings';

interface AppState {
  loaded: boolean;
  settings: PcSettings;
  deck: DeckConfig;
  activePageId: string;
  device: DeviceCredentials | null;

  pc: PcMachine;
  link: LinkMachine;
  snapshot: ServiceSnapshot | null;
  facts: Facts;
  summary: GlobalSummary;
  now: number;

  view: View;
  statusOpen: boolean;
  pairingOpen: boolean;
  buttonState: Record<string, ButtonRunState>;
  /** Motivo breve de un fallo, mostrado en el propio botón (sin avisos emergentes, ADR-029). */
  buttonNote: Record<string, string>;
  toasts: Toast[];
  confirm: ConfirmRequest | null;
  /** Hueco de app de General que se está configurando (pulsación larga). */
  appPickerSlot: number | null;
  /** Sección elegida en las páginas con secciones (DaVinci, ADR-031): página → sección. */
  activeSections: Record<string, string>;
  /** Página cuya lista de secciones está abierta. */
  sectionPickerPage: string | null;

  hydrate: (data: { settings: PcSettings; deck: DeckConfig; activePageId?: string; device: DeviceCredentials | null; activeSections?: Record<string, string> }) => void;
  setSettings: (settings: PcSettings) => void;
  setDeck: (deck: DeckConfig) => void;
  setAppPickerSlot: (slot: number | null) => void;
  setActiveSection: (pageId: string, sectionId: string) => void;
  setSectionPickerPage: (pageId: string | null) => void;
  setActivePage: (id: string) => void;
  setDevice: (device: DeviceCredentials | null) => void;
  dispatchPc: (event: PcEvent) => void;
  dispatchLink: (event: LinkEvent) => void;
  setSnapshot: (snapshot: ServiceSnapshot | null) => void;
  setView: (view: View) => void;
  setStatusOpen: (open: boolean) => void;
  setPairingOpen: (open: boolean) => void;
  setButtonState: (id: string, state: ButtonRunState | null, note?: string) => void;
  toast: (kind: ToastKind, title: string, detail?: string) => void;
  dismissToast: (id: number) => void;
  askConfirm: (title: string, text: string, verb?: string) => Promise<boolean>;
  closeConfirm: (ok: boolean) => void;
}


let toastSeq = 0;

export const useApp = create<AppState>()((set, get) => {
  /** Recalcula hechos y estado global tras cambiar cualquier capa. */
  const derive = (patch: Partial<AppState>): Partial<AppState> => {
    const s = { ...get(), ...patch };
    // Sin enlace listo, la instantánea no refleja el estado real.
    const snapshot = s.pc.state === 'PC_ONLINE' && s.link.state === 'READY' ? s.snapshot : null;
    const view: SystemView = { pc: s.pc, link: s.link, snapshot };
    return { ...patch, snapshot, facts: buildFacts(view), summary: globalStatus(view) };
  };

  const now = Date.now();
  const pc = initialMachine(now);
  const link = initialLink(now);
  const view: SystemView = { pc, link, snapshot: null };

  return {
    loaded: false,
    settings: DEFAULT_PC_SETTINGS,
    deck: DEFAULT_DECK,
    activePageId: DEFAULT_DECK.pages[0].id,
    device: null,
    pc,
    link,
    snapshot: null,
    facts: buildFacts(view),
    summary: globalStatus(view),
    now,
    view: 'deck',
    statusOpen: false,
    pairingOpen: false,
    buttonState: {},
    buttonNote: {},
    toasts: [],
    confirm: null,
    appPickerSlot: null,
    activeSections: {},
    sectionPickerPage: null,

    hydrate: ({ settings, deck, activePageId, device, activeSections }) => set({
      loaded: true,
      activeSections: activeSections ?? {},
      settings,
      deck,
      device,
      activePageId: activePageId && deck.pages.some((p) => p.id === activePageId) ? activePageId : deck.pages[0].id,
    }),

    setSettings: (settings) => set({ settings }),
    setDeck: (deck) => set({ deck }),
    setAppPickerSlot: (appPickerSlot) => set({ appPickerSlot }),
    setActiveSection: (pageId, sectionId) => set({ activeSections: { ...get().activeSections, [pageId]: sectionId } }),
    setSectionPickerPage: (sectionPickerPage) => set({ sectionPickerPage }),
    setActivePage: (activePageId) => set({ activePageId }),
    setDevice: (device) => set({ device }),

    dispatchPc: (event) => {
      const current = get().pc;
      const next = transition(current, event);
      const patch: Partial<AppState> = event.type === 'TICK' ? { now: event.at } : {};
      if (next === current) {
        if (event.type === 'TICK') set(patch);
        return;
      }
      set(derive({ ...patch, pc: next }));
    },

    dispatchLink: (event) => {
      const current = get().link;
      const next = linkTransition(current, event);
      if (next !== current) set(derive({ link: next }));
    },

    setSnapshot: (snapshot) => set(derive({ snapshot })),
    setView: (view) => set({ view }),
    setStatusOpen: (statusOpen) => set({ statusOpen }),
    setPairingOpen: (pairingOpen) => set({ pairingOpen }),

    setButtonState: (id, state, note) => {
      const next = { ...get().buttonState };
      const notes = { ...get().buttonNote };
      if (state) next[id] = state;
      else delete next[id];
      if (state === 'failed' && note) notes[id] = note;
      else delete notes[id];
      set({ buttonState: next, buttonNote: notes });
    },

    toast: (kind, title, detail) => {
      const id = ++toastSeq;
      set({ toasts: [...get().toasts.slice(-2), { id, kind, title, detail }] });
      setTimeout(() => get().dismissToast(id), kind === 'error' ? 7000 : 4500);
    },
    dismissToast: (id) => set({ toasts: get().toasts.filter((t) => t.id !== id) }),

    askConfirm: (title, text, verb = 'Confirmar') => new Promise<boolean>((resolve) => {
      get().confirm?.resolve(false);
      set({ confirm: { title, text, verb, resolve } });
    }),
    closeConfirm: (ok) => {
      get().confirm?.resolve(ok);
      set({ confirm: null });
    },
  };
});
