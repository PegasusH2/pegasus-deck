// Acciones que se resuelven en el iPad (ejecutor "pwa"), registradas
// por tipo. Añadir una acción local = añadir una entrada aquí (hay una prueba que
// comprueba que cada acción local del catálogo tiene su handler).

import type { ActionParams } from '../core/actions';
import { connection } from './connection';
import type { ActionOutcome } from './actionEngine';
import { useApp } from './store';

export type LocalHandler = (params: ActionParams, signal?: AbortSignal) => Promise<ActionOutcome>;

export const LOCAL_HANDLERS: Record<string, LocalHandler> = {
  'system.check': async () => {
    const ok = await connection.probeNow();
    if (ok) return { status: 'completed', message: 'El PC responde.' };
    return {
      status: 'failed',
      code: 'NETWORK',
      message: 'El PC no responde: está apagado, en suspensión o el servicio no se está ejecutando.',
    };
  },

  'system.status': async () => {
    useApp.getState().setStatusOpen(true);
    return { status: 'completed', message: '' };
  },

  'local.audioOutputs': async () => {
    useApp.getState().setOutputPickerOpen(true);
    return { status: 'completed', message: '' };
  },

  'local.settings': async () => {
    useApp.getState().setView('settings');
    return { status: 'completed', message: '' };
  },

  'local.page': async (params) => {
    const app = useApp.getState();
    const pageId = String(params.pageId ?? '');
    if (!app.deck.pages.some((p) => p.id === pageId)) return { status: 'failed', code: 'NOT_FOUND', message: `Página no encontrada: ${pageId}` };
    app.setActivePage(pageId);
    return { status: 'completed', message: '' };
  },
};
