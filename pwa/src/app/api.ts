// Cliente HTTP de la API del PC (mismo origen que la PWA). Valida todas las
// respuestas con el contrato y traduce errores a mensajes comprensibles.

import {
  actionResponseSchema,
  apiErrorSchema,
  healthSchema,
  pairResponseSchema,
  type ActionResponse,
  type ErrorCode,
  type Health,
} from '../core/contract';
import type { ActionParams } from '../core/actions';

export class ApiError extends Error {
  constructor(
    readonly code: ErrorCode | 'NETWORK' | 'BAD_RESPONSE',
    message: string,
    readonly status = 0,
  ) {
    super(message);
  }

  /** Fallos que tiene sentido reintentar (red, tiempo, error interno). */
  get transient(): boolean {
    return this.code === 'NETWORK' || this.code === 'TIMEOUT' || this.code === 'BUSY' || this.status >= 500;
  }
}

async function call(path: string, init: RequestInit & { timeoutMs: number; token?: string }): Promise<{ status: number; body: unknown }> {
  const headers: Record<string, string> = { Accept: 'application/json' };
  if (init.body) headers['Content-Type'] = 'application/json';
  if (init.token) headers.Authorization = `Bearer ${init.token}`;
  let res: Response;
  try {
    res = await fetch(path, { ...init, headers, cache: 'no-store', signal: AbortSignal.timeout(init.timeoutMs) });
  } catch (err) {
    const timeout = (err as Error).name === 'TimeoutError';
    throw new ApiError(timeout ? 'TIMEOUT' : 'NETWORK', timeout ? 'El PC no ha respondido a tiempo.' : 'No hay conexión con el PC.');
  }
  const body: unknown = await res.json().catch(() => null);
  return { status: res.status, body };
}

function toError(status: number, body: unknown): ApiError {
  const err = apiErrorSchema.safeParse(body);
  if (err.success) return new ApiError(err.data.code, err.data.message, status);
  return new ApiError('BAD_RESPONSE', `Respuesta inesperada del PC (HTTP ${status}).`, status);
}

export async function getHealth(timeoutMs: number): Promise<Health> {
  const { status, body } = await call('/api/health', { method: 'GET', timeoutMs });
  const parsed = healthSchema.safeParse(body);
  if (status !== 200 || !parsed.success) throw toError(status, body);
  return parsed.data;
}

export async function pairDevice(code: string, deviceName: string): Promise<{ deviceId: string; token: string }> {
  const { status, body } = await call('/api/pair', { method: 'POST', body: JSON.stringify({ code, deviceName }), timeoutMs: 8000 });
  const parsed = pairResponseSchema.safeParse(body);
  if (!parsed.success) throw toError(status, body);
  if (!parsed.data.ok) throw new ApiError(parsed.data.code, parsed.data.message, status);
  return { deviceId: parsed.data.deviceId, token: parsed.data.token };
}

export type ActionOk = Exclude<ActionResponse, { ok: false }>;

export async function postAction(type: string, params: ActionParams, token: string, timeoutMs: number, confirmToken?: string): Promise<ActionOk> {
  const payload = confirmToken ? { type, params, confirmToken } : { type, params };
  const { status, body } = await call('/api/actions', { method: 'POST', body: JSON.stringify(payload), token, timeoutMs });
  const parsed = actionResponseSchema.safeParse(body);
  if (!parsed.success) throw toError(status, body);
  if (!parsed.data.ok) throw new ApiError(parsed.data.code, parsed.data.message, status);
  return parsed.data;
}
