// Motor de acciones (P-3): ejecuta un tipo del catálogo con sus parámetros y devuelve
// un resultado tipado, sin tocar la interfaz. Lo usan los botones y lo usarán las
// macros (fase 4), que necesitan el código de error y poder cancelar.

import { getAction, validateParams, type ActionParams } from '../core/actions';
import type { ErrorCode } from '../core/contract';
import { ApiError, postAction } from './api';
import { connection } from './connection';
import { LOCAL_HANDLERS } from './localActions';
import { log } from './log';
import { trackOperation } from './operations';
import { useApp } from './store';

export type ActionOutcome =
  | { status: 'completed'; message: string }
  | { status: 'failed'; code: ErrorCode | 'NETWORK' | 'BAD_RESPONSE'; message: string }
  | { status: 'timeout'; message: string }
  | { status: 'cancelled'; message: string };

export interface ExecuteOptions {
  /** Tiempo máximo hasta la confirmación (por defecto, el de la acción). */
  timeoutMs?: number;
  /** Reintentos ante fallos transitorios; solo se aplican a acciones idempotentes. */
  retry?: { attempts: number; delayMs: number };
  signal?: AbortSignal;
  /** Texto para avisos de confirmación tardía. */
  label?: string;
}

const CANCELLED: ActionOutcome = { status: 'cancelled', message: 'Cancelada.' };

function sleep(ms: number, signal?: AbortSignal): Promise<void> {
  return new Promise((resolve) => {
    const t = setTimeout(resolve, ms);
    signal?.addEventListener('abort', () => { clearTimeout(t); resolve(); }, { once: true });
  });
}

export async function executeAction(type: string, rawParams: unknown, opts: ExecuteOptions = {}): Promise<ActionOutcome> {
  const def = getAction(type);
  if (!def) return { status: 'failed', code: 'UNKNOWN_ACTION', message: `Acción desconocida: ${type}` };
  const check = validateParams(type, rawParams);
  if (!check.ok) return { status: 'failed', code: 'BAD_PARAMS', message: check.error };
  if (opts.signal?.aborted) return CANCELLED;

  if (def.executor !== 'service') {
    const handler = LOCAL_HANDLERS[type];
    if (!handler) return { status: 'failed', code: 'UNKNOWN_ACTION', message: `Sin implementación local: ${type}` };
    return handler(check.params, opts.signal);
  }
  return runOnService(type, check.params, def.idempotent, def.timeoutMs, opts);
}

async function runOnService(type: string, params: ActionParams, idempotent: boolean, defaultTimeout: number, opts: ExecuteOptions): Promise<ActionOutcome> {
  const token = useApp.getState().device?.token;
  if (!token) return { status: 'failed', code: 'AUTH_REQUIRED', message: 'Este iPad no está emparejado con el PC.' };

  const timeoutMs = opts.timeoutMs ?? defaultTimeout;
  const maxAttempts = idempotent ? 1 + (opts.retry?.attempts ?? 0) : 1;

  for (let attempt = 1; ; attempt++) {
    if (opts.signal?.aborted) return CANCELLED;
    try {
      let res = await postAction(type, params, token, Math.min(timeoutMs, 10_000));
      if (res.status === 'confirm_required') {
        // Acción destructiva (ADR-024): el usuario ya la ha confirmado en la interfaz; se envía el segundo paso.
        if (opts.signal?.aborted) return CANCELLED;
        res = await postAction(type, params, token, Math.min(timeoutMs, 10_000), res.confirmToken);
        if (res.status === 'confirm_required') return { status: 'failed', code: 'CONFIRMATION_INVALID', message: 'El PC no ha aceptado la confirmación.' };
      }
      if (res.status === 'completed') return { status: 'completed', message: res.message };

      // Aceptada: hay que esperar la confirmación del PC.
      const outcome = await trackOperation(res.operationId, opts.label ?? type, timeoutMs, opts.signal);
      switch (outcome.status) {
        case 'completed':
          return outcome;
        case 'failed':
          return outcome;
        case 'cancelled':
          return { status: 'cancelled', message: 'Se ha dejado de esperar la confirmación; la acción puede completarse igualmente.' };
        case 'timeout':
          return { status: 'timeout', message: `El PC aceptó la orden pero no confirmó el resultado en ${Math.round(timeoutMs / 1000)} s. Puede completarse más tarde.` };
      }
    } catch (err) {
      const e = err instanceof ApiError ? err : new ApiError('NETWORK', String(err));
      log('warn', 'action.error', { type, errorCode: e.code, attempt });
      if (e.code === 'AUTH_REQUIRED') {
        useApp.getState().dispatchLink({ type: 'AUTH_REJECTED', at: Date.now() });
      }
      if (e.transient && attempt < maxAttempts && !opts.signal?.aborted) {
        await sleep(opts.retry?.delayMs ?? 0, opts.signal);
        continue;
      }
      if (e.code === 'NETWORK' || e.code === 'TIMEOUT') void connection.probeNow();
      return e.code === 'TIMEOUT'
        ? { status: 'timeout', message: e.message }
        : { status: 'failed', code: e.code, message: e.message };
    }
  }
}
