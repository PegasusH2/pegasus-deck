// Registro de diagnóstico en memoria (últimas entradas). Enmascara cualquier
// campo que pueda contener secretos: nunca se guardan tokens ni códigos.

export type LogLevel = 'debug' | 'info' | 'warn' | 'error';

export interface LogEntry {
  at: number;
  level: LogLevel;
  event: string;
  data?: unknown;
}

const MAX_ENTRIES = 200;
const isSecretKey = (k: string) => /token|authorization|password|secret/i.test(k) || /^(code|pairingCode)$/i.test(k);
const entries: LogEntry[] = [];

export function redact(value: unknown, depth = 0): unknown {
  if (depth > 4 || value === null || typeof value !== 'object') return value;
  if (Array.isArray(value)) return value.slice(0, 20).map((v) => redact(v, depth + 1));
  const out: Record<string, unknown> = {};
  for (const [k, v] of Object.entries(value as Record<string, unknown>)) {
    out[k] = isSecretKey(k) && typeof v === 'string' ? '***' : redact(v, depth + 1);
  }
  return out;
}

export function log(level: LogLevel, event: string, data?: unknown): void {
  const entry: LogEntry = { at: Date.now(), level, event, data: data === undefined ? undefined : redact(data) };
  entries.push(entry);
  if (entries.length > MAX_ENTRIES) entries.shift();
  if (level === 'error') console.error(`[pegasus] ${event}`, entry.data ?? '');
  else if (level === 'warn') console.warn(`[pegasus] ${event}`, entry.data ?? '');
  else if (import.meta.env.DEV) console.debug(`[pegasus] ${event}`, entry.data ?? '');
}

export function recentLog(): readonly LogEntry[] {
  return entries;
}
