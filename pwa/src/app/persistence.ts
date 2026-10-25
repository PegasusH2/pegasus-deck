// Configuración guardada en IndexedDB del iPad (API nativa, sin dependencias).
// Única credencial guardada: el token de dispositivo revocable del emparejamiento.

import { DEFAULT_DECK, type DeckConfig } from '../core/deck';
import { migrateDeck } from '../core/migrations';
import { DEFAULT_PC_SETTINGS, pcSettingsSchema, type PcSettings } from '../core/settings';
import { log } from './log';
import { useApp, type DeviceCredentials } from './store';

const DB_NAME = 'pegasus-deck';
const STORE = 'kv';

let dbPromise: Promise<IDBDatabase> | null = null;

function promisify<T>(req: IDBRequest<T>): Promise<T> {
  return new Promise((resolve, reject) => {
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error);
  });
}

function db(): Promise<IDBDatabase> {
  dbPromise ??= new Promise((resolve, reject) => {
    const req = indexedDB.open(DB_NAME, 1);
    req.onupgradeneeded = () => req.result.createObjectStore(STORE);
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error);
  });
  return dbPromise;
}

async function read(key: string): Promise<unknown> {
  try {
    const tx = (await db()).transaction(STORE, 'readonly');
    return await promisify(tx.objectStore(STORE).get(key));
  } catch (err) {
    log('error', 'storage.read', { key, err: String(err) });
    return undefined;
  }
}

async function write(key: string, value: unknown): Promise<void> {
  try {
    const tx = (await db()).transaction(STORE, 'readwrite');
    if (value === undefined) await promisify(tx.objectStore(STORE).delete(key));
    else await promisify(tx.objectStore(STORE).put(value, key));
  } catch (err) {
    log('error', 'storage.write', { key, err: String(err) });
    useApp.getState().toast('error', 'No se pudo guardar la configuración', String(err));
  }
}

export interface LoadedConfig {
  settings: PcSettings;
  deck: DeckConfig;
  activePageId?: string;
  device: DeviceCredentials | null;
  activeSections?: Record<string, string>;
  /** Problemas: se cargó algo distinto de lo guardado. */
  warnings: string[];
  /** Avisos informativos de una migración correcta (p. ej. qué cambió en General). */
  notices: string[];
}

function isSections(v: unknown): v is Record<string, string> {
  return !!v && typeof v === 'object' && Object.values(v as object).every((x) => typeof x === 'string' && x.length <= 40);
}

function isDevice(v: unknown): v is DeviceCredentials {
  const d = v as DeviceCredentials | null;
  return !!d && typeof d.deviceId === 'string' && typeof d.token === 'string' && typeof d.pairedAt === 'number';
}

export async function loadConfig(): Promise<LoadedConfig> {
  const [rawSettings, rawDeck, rawPage, rawDevice, rawSections] = await Promise.all([
    read('settings'), read('deck'), read('activePageId'), read('device'), read('activeSections'),
  ]);
  const warnings: string[] = [];
  const notices: string[] = [];

  let settings = DEFAULT_PC_SETTINGS;
  if (rawSettings !== undefined) {
    const parsed = pcSettingsSchema.safeParse({ ...DEFAULT_PC_SETTINGS, ...(rawSettings as object) });
    if (parsed.success) settings = parsed.data;
    else warnings.push('Los ajustes del PC guardados no eran válidos; se han restaurado los valores por defecto.');
  }

  let deck = DEFAULT_DECK;
  if (rawDeck !== undefined) {
    const result = migrateDeck(rawDeck);
    if (result.ok) {
      deck = result.deck;
      notices.push(...result.warnings);
      if (result.migrated) {
        // Copia de la versión anterior antes de sustituirla.
        await write(`deck.backup-v${(rawDeck as { schemaVersion: number }).schemaVersion}`, rawDeck);
        await write('deck', deck);
        log('info', 'deck.migrated');
      }
    } else {
      await write(`deck.invalid-${Date.now()}`, rawDeck);
      warnings.push(`La configuración de botones guardada no era válida (${result.error}). Se ha guardado una copia y se ha cargado el panel por defecto.`);
    }
  }

  return {
    settings,
    deck,
    activePageId: typeof rawPage === 'string' ? rawPage : undefined,
    device: isDevice(rawDevice) ? rawDevice : null,
    activeSections: isSections(rawSections) ? rawSections : undefined,
    warnings,
    notices,
  };
}

/** Guarda automáticamente ajustes, botones, página activa y emparejamiento. */
export function startAutosave(): () => void {
  return useApp.subscribe((state, prev) => {
    if (!state.loaded) return;
    if (state.settings !== prev.settings) void write('settings', state.settings);
    if (state.deck !== prev.deck) void write('deck', state.deck);
    if (state.activePageId !== prev.activePageId) void write('activePageId', state.activePageId);
    if (state.activeSections !== prev.activeSections) void write('activeSections', state.activeSections);
    if (state.device !== prev.device) void write('device', state.device ?? undefined);
  });
}
