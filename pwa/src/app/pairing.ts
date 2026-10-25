// Emparejamiento del iPad con el PC: código de 6 cifras → token de dispositivo.

import { pairRequestSchema } from '../core/contract';
import { ApiError, pairDevice } from './api';
import { log } from './log';
import { useApp } from './store';

export type PairResult = { ok: true } | { ok: false; error: string };

export async function pairWithCode(code: string, deviceName: string): Promise<PairResult> {
  const req = pairRequestSchema.safeParse({ code: code.trim(), deviceName });
  if (!req.success) return { ok: false, error: req.error.issues[0]?.message ?? 'Datos no válidos' };
  try {
    const { deviceId, token } = await pairDevice(req.data.code, req.data.deviceName);
    useApp.getState().setDevice({ deviceId, token, deviceName: req.data.deviceName, pairedAt: Date.now() });
    log('info', 'pairing.ok', { deviceId });
    return { ok: true };
  } catch (err) {
    const e = err instanceof ApiError ? err : new ApiError('NETWORK', String(err));
    log('warn', 'pairing.failed', { errorCode: e.code });
    return { ok: false, error: e.message };
  }
}

/** Olvida el emparejamiento en este iPad (el PC lo revoca desde su lado). */
export function forgetDevice(): void {
  useApp.getState().setDevice(null);
  log('info', 'pairing.forgotten');
}
