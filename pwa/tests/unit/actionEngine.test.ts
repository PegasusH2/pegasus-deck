// Motor de acciones (P-3): resultados tipados, cancelación y reintentos seguros.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { executeAction } from '../../src/app/actionEngine';
import { LOCAL_HANDLERS } from '../../src/app/localActions';
import { onOperationResult } from '../../src/app/operations';
import { useApp } from '../../src/app/store';
import { listActions } from '../../src/core/actions';

type Reply = { status: number; body: unknown } | 'network-error';

function mockFetch(replies: Reply[]) {
  const calls: unknown[] = [];
  vi.stubGlobal('fetch', vi.fn(async (_url: string, init: RequestInit) => {
    calls.push(JSON.parse(String(init.body)));
    const r = replies.shift() ?? 'network-error';
    if (r === 'network-error') throw new TypeError('Failed to fetch');
    return new Response(JSON.stringify(r.body), { status: r.status });
  }));
  return calls;
}

beforeEach(() => {
  useApp.setState({ device: { deviceId: 'a1b2c3d4e5f60718', token: 't'.repeat(43), deviceName: 'iPad', pairedAt: 0 } });
});
afterEach(() => vi.unstubAllGlobals());

describe('motor de acciones', () => {
  it('cada acción local del catálogo tiene su handler', () => {
    for (const a of listActions().filter((d) => d.executor !== 'service')) {
      expect(LOCAL_HANDLERS[a.type], a.type).toBeTypeOf('function');
    }
  });

  it('acción desconocida o parámetros no válidos: fallo con código, sin llamar al PC', async () => {
    const calls = mockFetch([]);
    expect(await executeAction('system.exec', {})).toMatchObject({ status: 'failed', code: 'UNKNOWN_ACTION' });
    expect(await executeAction('app.open', { appId: '../cmd' })).toMatchObject({ status: 'failed', code: 'BAD_PARAMS' });
    expect(calls).toHaveLength(0);
  });

  it('sin emparejar: AUTH_REQUIRED', async () => {
    useApp.setState({ device: null });
    expect(await executeAction('windows.lock', {})).toMatchObject({ status: 'failed', code: 'AUTH_REQUIRED' });
  });

  it('aceptada + confirmación por evento → completed', async () => {
    mockFetch([{ status: 200, body: { ok: true, status: 'accepted', operationId: 'c0ffee0000000001' } }]);
    const p = executeAction('obs.recording.start', {});
    await vi.waitFor(() => expect(fetch).toHaveBeenCalled());
    await new Promise((r) => setTimeout(r, 0));
    onOperationResult({ operationId: 'c0ffee0000000001', status: 'completed', message: 'Grabación iniciada.' });
    await expect(p).resolves.toEqual({ status: 'completed', message: 'Grabación iniciada.' });
  });

  it('el fallo confirmado conserva el código de error (lo necesitan las macros)', async () => {
    mockFetch([{ status: 200, body: { ok: true, status: 'accepted', operationId: 'c0ffee0000000002' } }]);
    const p = executeAction('obs.scene.set', { scene: 'Juego' });
    await vi.waitFor(() => expect(fetch).toHaveBeenCalled());
    await new Promise((r) => setTimeout(r, 0));
    onOperationResult({ operationId: 'c0ffee0000000002', status: 'failed', code: 'NOT_FOUND', message: 'No existe.' });
    await expect(p).resolves.toEqual({ status: 'failed', code: 'NOT_FOUND', message: 'No existe.' });
  });

  it('se puede cancelar la espera de la confirmación', async () => {
    mockFetch([{ status: 200, body: { ok: true, status: 'accepted', operationId: 'c0ffee0000000003' } }]);
    const ctrl = new AbortController();
    const p = executeAction('obs.recording.start', {}, { signal: ctrl.signal });
    await vi.waitFor(() => expect(fetch).toHaveBeenCalled());
    await new Promise((r) => setTimeout(r, 0));
    ctrl.abort();
    await expect(p).resolves.toMatchObject({ status: 'cancelled' });
  });

  it('el tiempo máximo se respeta sin dar la acción por hecha', async () => {
    mockFetch([{ status: 200, body: { ok: true, status: 'accepted', operationId: 'c0ffee0000000004' } }]);
    await expect(executeAction('obs.recording.start', {}, { timeoutMs: 50 })).resolves.toMatchObject({ status: 'timeout' });
  });

  it('reintenta fallos transitorios solo en acciones idempotentes', async () => {
    const lockCalls = mockFetch(['network-error', { status: 200, body: { ok: true, status: 'completed', message: 'Bloqueada.' } }]);
    await expect(executeAction('windows.lock', {}, { retry: { attempts: 2, delayMs: 0 } })).resolves.toMatchObject({ status: 'completed' });
    expect(lockCalls).toHaveLength(2);

    const recCalls = mockFetch(['network-error', { status: 200, body: { ok: true, status: 'completed', message: 'x' } }]);
    await expect(executeAction('obs.recording.start', {}, { retry: { attempts: 2, delayMs: 0 } })).resolves.toMatchObject({ status: 'failed', code: 'NETWORK' });
    expect(recCalls).toHaveLength(1);
  });

  it('no reintenta errores definitivos (4xx)', async () => {
    const calls = mockFetch([{ status: 409, body: { ok: false, code: 'NO_SESSION', message: 'Sin sesión.' } }]);
    await expect(executeAction('windows.lock', {}, { retry: { attempts: 3, delayMs: 0 } })).resolves.toMatchObject({ status: 'failed', code: 'NO_SESSION' });
    expect(calls).toHaveLength(1);
  });
});
