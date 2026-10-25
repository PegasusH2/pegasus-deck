// Seguimiento de acciones "aceptadas" por el PC hasta que llega su confirmación
// por WebSocket (ADR-006). Una acción aceptada NO es una acción completada.

import type { ErrorCode, OperationResult } from '../core/contract';
import { log } from './log';

export type OperationOutcome =
  | { status: 'completed'; message: string }
  | { status: 'failed'; code: ErrorCode | 'NETWORK'; message: string }
  | { status: 'timeout' }
  | { status: 'cancelled' };

interface Pending {
  label: string;
  resolve: (o: OperationOutcome) => void;
  timer: ReturnType<typeof setTimeout>;
}

const pending = new Map<string, Pending>();
/** Operaciones que ya no se esperan (tiempo agotado, canceladas): si se confirman tarde, se avisa. */
const expired = new Map<string, string>();
const MAX_EXPIRED = 50;
/** Confirmaciones que llegaron antes que la respuesta HTTP (OBS puede ser más rápido que la red). */
const early = new Map<string, { result: OperationResult; at: number }>();
const MAX_EARLY = 50;
const EARLY_TTL_MS = 30_000;

function toOutcome(result: OperationResult): OperationOutcome {
  return result.status === 'completed'
    ? { status: 'completed', message: result.message }
    : { status: 'failed', code: result.code ?? 'INTERNAL', message: result.message };
}

function expire(operationId: string, label: string): void {
  expired.set(operationId, label);
  if (expired.size > MAX_EXPIRED) expired.delete(expired.keys().next().value!);
}

export function trackOperation(operationId: string, label: string, timeoutMs: number, signal?: AbortSignal): Promise<OperationOutcome> {
  const already = early.get(operationId);
  if (already) {
    early.delete(operationId);
    return Promise.resolve(toOutcome(already.result));
  }
  return new Promise((resolve) => {
    const finish = (o: OperationOutcome) => {
      clearTimeout(timer);
      signal?.removeEventListener('abort', onAbort);
      pending.delete(operationId);
      resolve(o);
    };
    const onAbort = () => {
      expire(operationId, label);
      log('info', 'operation.cancelled', { operationId, label });
      finish({ status: 'cancelled' });
    };
    const timer = setTimeout(() => {
      expire(operationId, label);
      log('warn', 'operation.timeout', { operationId, label });
      finish({ status: 'timeout' });
    }, timeoutMs);
    if (signal?.aborted) {
      onAbort();
      return;
    }
    signal?.addEventListener('abort', onAbort, { once: true });
    pending.set(operationId, { label, resolve: finish, timer });
  });
}

export function onOperationResult(result: OperationResult): void {
  const op = pending.get(result.operationId);
  if (op) {
    log(result.status === 'completed' ? 'info' : 'warn', 'operation.result', { operationId: result.operationId, status: result.status, errorCode: result.code });
    op.resolve(toOutcome(result));
    return;
  }
  const late = expired.get(result.operationId);
  if (late) {
    expired.delete(result.operationId);
    log('info', 'operation.late', { operationId: result.operationId, status: result.status });
    // Sin aviso (ADR-029): el estado real ya llega por la instantánea.
    return;
  }
  // Desconocida: puede que la respuesta HTTP aún no haya llegado. Se guarda un tiempo.
  const now = Date.now();
  for (const [id, e] of early) if (now - e.at > EARLY_TTL_MS) early.delete(id);
  if (early.size >= MAX_EARLY) early.delete(early.keys().next().value!);
  early.set(result.operationId, { result, at: now });
}

/** Si se pierde la conexión, las operaciones pendientes no se pueden confirmar. */
export function failAllPending(reason: string): void {
  for (const [id, op] of [...pending]) {
    expire(id, op.label);
    op.resolve({ status: 'failed', code: 'NETWORK', message: reason });
  }
}
