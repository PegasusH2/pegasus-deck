// El simulador cumple el contrato y las reglas de seguridad (auth, origen, lista blanca).
import { spawn, type ChildProcess } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { afterAll, beforeAll, beforeEach, describe, expect, it } from 'vitest';
import WebSocket from 'ws';
import { CONTRACTS, errorsOf, examples, validator } from './contract';

const PORT = 8791;
const BASE = `http://127.0.0.1:${PORT}`;
let sim: ChildProcess;

async function control(op: string, extra: Record<string, unknown> = {}) {
  const r = await fetch(`${BASE}/__sim/control`, { method: 'POST', body: JSON.stringify({ op, ...extra }) });
  return r.json() as Promise<Record<string, unknown>>;
}

async function api(pathname: string, init: RequestInit = {}) {
  const r = await fetch(BASE + pathname, init);
  return { status: r.status, body: await r.json() as unknown };
}

function expectSchema(ref: string, value: unknown) {
  const v = validator(ref);
  expect(v(value), errorsOf(v)).toBe(true);
}

beforeAll(async () => {
  sim = spawn(process.execPath, [path.resolve(__dirname, '../../sim/server.mjs')], {
    env: { ...process.env, SIM_PORT: String(PORT), SIM_UDP_PORT: '40091' },
    stdio: 'ignore',
  });
  for (let i = 0; i < 50; i++) {
    try { await fetch(`${BASE}/__sim/state`); return; } catch { await new Promise((r) => setTimeout(r, 100)); }
  }
  throw new Error('El simulador no arrancó');
});

afterAll(() => { sim.kill(); });
beforeEach(async () => { await control('reset', { power: 'on' }); });

describe('simulador: contrato y seguridad', () => {
  it('health: solo protocolo y versión', async () => {
    const r = await api('/api/health');
    expect(r.status).toBe(200);
    expectSchema('health.schema.json', r.body);
  });

  it('con el PC apagado no responde', async () => {
    await control('powerOff');
    await expect(fetch(`${BASE}/api/health`)).rejects.toThrow();
  });

  it('emparejamiento: código de un solo uso y errores con formato del contrato', async () => {
    const bad = await api('/api/pair', { method: 'POST', body: JSON.stringify({ code: '000000', deviceName: 'x' }) });
    expect(bad.status).toBe(401);
    expectSchema('pair.schema.json#/$defs/response', bad.body);

    const { code } = await control('pairingCode') as { code: string };
    const ok = await api('/api/pair', { method: 'POST', body: JSON.stringify({ code, deviceName: 'iPad' }) });
    expect(ok.status).toBe(200);
    expectSchema('pair.schema.json#/$defs/response', ok.body);

    const reuse = await api('/api/pair', { method: 'POST', body: JSON.stringify({ code, deviceName: 'iPad' }) });
    expect(reuse.status).toBe(401);
  });

  it('acciones: sin token → 401; con token → respuestas del contrato', async () => {
    const noAuth = await api('/api/actions', { method: 'POST', body: JSON.stringify({ type: 'windows.lock', params: {} }) });
    expect(noAuth.status).toBe(401);
    expectSchema('action.schema.json#/$defs/response', noAuth.body);

    const { token } = await control('issueToken') as { token: string };
    const auth = { Authorization: `Bearer ${token}` };
    const done = await api('/api/actions', { method: 'POST', headers: auth, body: JSON.stringify({ type: 'windows.lock', params: {} }) });
    expect(done.body).toMatchObject({ ok: true, status: 'completed' });
    expectSchema('action.schema.json#/$defs/response', done.body);

    const accepted = await api('/api/actions', { method: 'POST', headers: auth, body: JSON.stringify({ type: 'app.open', params: { appId: 'obs' } }) });
    expect(accepted.body).toMatchObject({ ok: true, status: 'accepted' });
    expectSchema('action.schema.json#/$defs/response', accepted.body);
  });

  it('lista blanca: acción, app y parámetros no autorizados se rechazan', async () => {
    const { token } = await control('issueToken') as { token: string };
    const auth = { Authorization: `Bearer ${token}` };
    const cases: [unknown, number, string][] = [
      [{ type: 'system.exec', params: { cmd: 'calc' } }, 400, 'UNKNOWN_ACTION'],
      [{ type: 'app.open', params: { appId: 'cmd' } }, 403, 'APP_NOT_ALLOWED'],
      [{ type: 'app.open', params: { appId: 'obs', args: '/c calc' } }, 400, 'BAD_PARAMS'],
      [{ type: 'obs.recording.start', params: {} }, 409, 'OBS_NOT_READY'],
    ];
    for (const [body, status, code] of cases) {
      const r = await api('/api/actions', { method: 'POST', headers: auth, body: JSON.stringify(body) });
      expect(r.status, JSON.stringify(body)).toBe(status);
      expect(r.body).toMatchObject({ ok: false, code });
      expectSchema('action.schema.json#/$defs/response', r.body);
    }
  });

  it('mismo orden de comprobación y códigos que el servicio .NET (ejemplos de contracts/)', async () => {
    const { token } = await control('issueToken') as { token: string };
    const auth = { Authorization: `Bearer ${token}` };
    // Peticiones inválidas del contrato: nunca UNKNOWN_ACTION salvo tipo bien formado y no registrado.
    for (const [name, body] of examples('action.request', 'invalid')) {
      const r = await api('/api/actions', { method: 'POST', headers: auth, body: JSON.stringify(body) });
      expect(r.status, name).toBe(400);
      expect((r.body as { code: string }).code, name).toBe('BAD_PARAMS');
    }
    // Ficha de acciones: válidas pasan la validación (pueden fallar por precondiciones), inválidas → BAD_PARAMS.
    const specs = JSON.parse(fs.readFileSync(path.join(CONTRACTS, 'actions.json'), 'utf8')) as Record<string, { valid: unknown[]; invalid: unknown[] }>;
    for (const [type, spec] of Object.entries(specs).filter(([k]) => !k.startsWith('$'))) {
      for (const params of spec.valid) {
        const r = await api('/api/actions', { method: 'POST', headers: auth, body: JSON.stringify({ type, params }) });
        expect(['BAD_PARAMS', 'UNKNOWN_ACTION'], `${type} ${JSON.stringify(params)}`).not.toContain((r.body as { code?: string }).code);
      }
      for (const params of spec.invalid) {
        const r = await api('/api/actions', { method: 'POST', headers: auth, body: JSON.stringify({ type, params }) });
        expect((r.body as { code?: string }).code, `${type} ${JSON.stringify(params)}`).toBe('BAD_PARAMS');
      }
    }
  });

  it('origen no autorizado → 403', async () => {
    const r = await api('/api/health', { headers: { Origin: 'http://evil.example' } });
    expect(r.status).toBe(403);
    expect(r.body).toMatchObject({ code: 'ORIGIN_FORBIDDEN' });
  });

  it('WebSocket: token inválido → error + cierre 4401; válido → ready y operation conformes', async () => {
    const bad = new WebSocket(`ws://127.0.0.1:${PORT}/api/events`);
    const badResult = await new Promise<{ msg: unknown; code: number }>((resolve) => {
      let msg: unknown = null;
      bad.on('open', () => bad.send(JSON.stringify({ type: 'auth', token: 'x'.repeat(43) })));
      bad.on('message', (m) => { msg = JSON.parse(String(m)); });
      bad.on('close', (code) => resolve({ msg, code }));
    });
    expect(badResult.code).toBe(4401);
    expectSchema('events.schema.json#/$defs/server', badResult.msg);

    const { token } = await control('issueToken') as { token: string };
    const ws = new WebSocket(`ws://127.0.0.1:${PORT}/api/events`);
    const messages: { type: string }[] = [];
    await new Promise<void>((resolve) => {
      ws.on('open', () => ws.send(JSON.stringify({ type: 'auth', token })));
      ws.on('message', (m) => {
        const msg = JSON.parse(String(m));
        messages.push(msg);
        if (msg.type === 'ready') {
          void fetch(`${BASE}/api/actions`, {
            method: 'POST', headers: { Authorization: `Bearer ${token}` },
            body: JSON.stringify({ type: 'app.open', params: { appId: 'chrome' } }),
          });
        }
        if (msg.type === 'operation') resolve();
      });
    });
    ws.close();
    expect(messages[0].type).toBe('ready');
    for (const m of messages) expectSchema('events.schema.json#/$defs/server', m);
    expect(messages.at(-1)).toMatchObject({ type: 'operation', data: { status: 'completed' } });
  });
});
