// Simulador del PC con Windows para desarrollo y pruebas (protocolo 2, ver contracts/).
//   GET  /api/health   -> { protocol, service }   (sin auth; solo si el PC está "encendido")
//   POST /api/pair     -> token de dispositivo a cambio de un código de 6 cifras
//   POST /api/actions  -> acciones registradas (Bearer; lista blanca + validación)
//   WS   /api/events   -> mensaje 'auth' → 'ready' + instantáneas + 'operation'
// También escucha paquetes mágicos UDP reales y ofrece un panel en /__sim.
//
// Variables: SIM_PORT (8787), SIM_UDP_PORT (40009), SIM_MAC, SIM_BOOT_DELAY_MS (8000),
// SIM_SESSION_DELAY_MS (3000), SIM_START_ON=1, SIM_ALLOWED_ORIGINS (lista separada por comas).

import crypto from 'node:crypto';
import dgram from 'node:dgram';
import fs from 'node:fs';
import http from 'node:http';
import { WebSocketServer } from 'ws';

const PROTOCOL = 2;
const PORT = Number(process.env.SIM_PORT ?? 8787);
const UDP_PORT = Number(process.env.SIM_UDP_PORT ?? 40009);
const MAC = (process.env.SIM_MAC ?? '00:11:22:33:44:55').toUpperCase();
const BOOT_DELAY_MS = Number(process.env.SIM_BOOT_DELAY_MS ?? 8000);
const SESSION_DELAY_MS = Number(process.env.SIM_SESSION_DELAY_MS ?? 3000);
const ALLOWED_ORIGINS = new Set([
  'http://localhost:5173', 'https://localhost:5173', 'http://127.0.0.1:5173',
  'http://localhost:4173', 'http://127.0.0.1:4173', 'http://127.0.0.1:4180', 'http://localhost:4180',
  ...(process.env.SIM_ALLOWED_ORIGINS ?? '').split(',').map((s) => s.trim()).filter(Boolean),
]);

// Lista blanca común (contracts/actions.json): el simulador valida igual que el servicio .NET.
const ACTION_SPECS = Object.fromEntries(Object.entries(
  JSON.parse(fs.readFileSync(new URL('../../contracts/actions.json', import.meta.url), 'utf8')),
).filter(([k]) => !k.startsWith('$')));
// Órdenes de DaVinci Resolve (ADR-028): lista cerrada común.
const RESOLVE_COMMANDS = Object.keys(JSON.parse(fs.readFileSync(new URL('../../contracts/resolve-commands.json', import.meta.url), 'utf8'))).filter((k) => !k.startsWith('$'));
const PARAM_RULES = {
  resolveCommand: (v) => typeof v === 'string' && RESOLVE_COMMANDS.includes(v),
  id: (v) => typeof v === 'string' && /^[a-z0-9][a-z0-9-]{0,39}$/.test(v),
  name: (v) => typeof v === 'string' && v.length <= 100 && v.trim().length > 0,
  percent: (v) => Number.isInteger(v) && v >= 0 && v <= 100,
};

/**
 * Orden de comprobación de /api/actions (igual que en .NET, ver contracts/README.md):
 * estructura → tipo registrado → parámetros. Devuelve [status, code, message] o null.
 */
function validateAction(body) {
  const okStructure = body && typeof body === 'object' && !Array.isArray(body)
    && Object.keys(body).every((k) => k === 'type' || k === 'params' || k === 'confirmToken')
    && (body.confirmToken === undefined || (typeof body.confirmToken === 'string' && /^[A-Za-z0-9_-]{20,64}$/.test(body.confirmToken)))
    && typeof body.type === 'string' && body.type.length <= 60 && /^[a-z]+(\.[a-zA-Z]+)+$/.test(body.type)
    && body.params && typeof body.params === 'object' && !Array.isArray(body.params)
    && Object.keys(body.params).length <= 10
    && Object.values(body.params).every((v) => (typeof v === 'string' && v.length <= 200) || typeof v === 'number' || typeof v === 'boolean');
  if (!okStructure) return [400, 'BAD_PARAMS', 'Petición no válida.'];
  const spec = Object.hasOwn(ACTION_SPECS, body.type) ? ACTION_SPECS[body.type] : null;
  if (!spec) return [400, 'UNKNOWN_ACTION', 'Acción no registrada.'];
  for (const k of Object.keys(body.params)) {
    if (!Object.hasOwn(spec.params, k)) return [400, 'BAD_PARAMS', `Parámetro no permitido: ${k}`];
  }
  for (const [k, kind] of Object.entries(spec.params)) {
    if (!PARAM_RULES[kind](body.params[k])) return [400, 'BAD_PARAMS', `Parámetro «${k}» no válido.`];
  }
  return null;
}

const APPS = [
  { id: 'obs', name: 'OBS Studio' },
  { id: 'chrome', name: 'Google Chrome' },
  { id: 'davinci', name: 'DaVinci Resolve' },
  { id: 'explorer', name: 'Explorador de archivos' },
];

// --- Estado --------------------------------------------------------------------

function freshState(power = 'off') {
  return {
    power, // off | booting | on
    bootedAt: power === 'on' ? Date.now() : null,
    session: power === 'on',
    obs: {
      state: 'unavailable', recording: false, recordingPaused: false, streaming: false,
      scene: 'Juego', scenes: ['Juego', 'Charla', 'Pausa'],
      inputs: [{ name: 'Mic/Aux', muted: false }, { name: 'Audio del escritorio', muted: false }],
    },
    // explorer.exe siempre está en marcha en Windows (escritorio y barra de tareas), como lo ve el agente real.
    apps: Object.fromEntries(APPS.map((a) => [a.id, a.id === 'explorer' ? 'ready' : 'installed'])),
    audio: { muted: false, volume: 40 },
    mic: { muted: false },
    // ADR-035: salidas de audio activas (ids opacos, como los del agente) y la predeterminada.
    outputs: [{ id: 'out-altavoces', name: 'Altavoces (Realtek)' }, { id: 'out-cascos', name: 'Auriculares USB' }, { id: 'out-hdmi', name: 'Monitor HDMI' }],
    defaultOutput: 'out-altavoces',
    media: 'playing', // playing | paused | stopped | none (ADR-022)
    windows: { explorer: 0 },
    resolveCommands: [], // órdenes recibidas para DaVinci Resolve (para las pruebas) // ventanas del Explorador abiertas (ADR-024); el resto: 1 si está abierta
    locked: false,
  };
}

let state = freshState(process.env.SIM_START_ON === '1' ? 'on' : 'off');
/** Comportamiento ajustable para pruebas. */
// Fallos que puede provocar el panel /__sim (los mismos que el agente real sabe detectar).
const DEFAULT_BEHAVIOUR = { confirmDelayMs: 300, dropConfirmations: false, blockEvents: false, audioUnavailable: false, micUnavailable: false, pauseUnsupported: false, silentRecordFailure: false };
let behaviour = { ...DEFAULT_BEHAVIOUR };
/** Dispositivos emparejados: hash sha256 del token → datos. Sobreviven a apagados (como en el PC real). */
const devices = new Map();
let pairing = null; // { code, expiresAt, attempts }
const pairAttemptsByIp = new Map();
const timers = new Set();
/** Segundo paso de las acciones destructivas: token → { key, expires }. */
const confirmations = new Map();

function later(ms, fn) {
  const t = setTimeout(() => { timers.delete(t); fn(); }, ms);
  timers.add(t);
}

function clearTimers() {
  for (const t of timers) clearTimeout(t);
  timers.clear();
}

const sha256 = (s) => crypto.createHash('sha256').update(s).digest();
const randomHex = (bytes) => crypto.randomBytes(bytes).toString('hex');

function issueToken(deviceName) {
  const token = crypto.randomBytes(32).toString('base64url');
  const deviceId = randomHex(8);
  devices.set(sha256(token).toString('hex'), { deviceId, deviceName, pairedAt: Date.now() });
  return { deviceId, token };
}

function deviceFor(token) {
  if (typeof token !== 'string' || token.length > 200) return null;
  const hash = sha256(token);
  for (const [stored, device] of devices) {
    // Comparación en tiempo constante.
    if (crypto.timingSafeEqual(Buffer.from(stored, 'hex'), hash)) return device;
  }
  return null;
}

function snapshot() {
  const session = state.session;
  const obsReady = session && state.obs.state === 'ready';
  return {
    protocol: PROTOCOL,
    service: { name: 'pegasus-deck', version: '0.2.0-sim' },
    windows: {
      booted: true,
      hostname: 'PEGASUS-PC',
      uptimeSec: Math.round((Date.now() - (state.bootedAt ?? Date.now())) / 1000),
    },
    session: { ready: session },
    obs: {
      ...state.obs,
      state: session ? state.obs.state : 'unavailable',
      recording: obsReady && state.obs.recording,
      recordingPaused: obsReady && state.obs.recordingPaused,
      streaming: obsReady && state.obs.streaming,
      scene: obsReady ? state.obs.scene : null,
      scenes: obsReady ? state.obs.scenes : [],
      inputs: obsReady ? state.obs.inputs : [],
    },
    // Sin sesión no hay agente que detecte apps.
    apps: session ? APPS.map((a) => ({ ...a, state: state.apps[a.id], windows: windowsOf(a.id) })) : [],
    appsDetection: session ? 'ok' : 'unavailable',
    audio: session && !behaviour.audioUnavailable ? { state: 'ready', ...state.audio } : { state: 'unavailable', muted: false, volume: 0 },
    mic: session && !behaviour.micUnavailable ? { state: 'ready', ...state.mic } : { state: 'unavailable', muted: false },
    media: { state: session ? state.media : 'unavailable' },
    outputs: session && !behaviour.audioUnavailable
      ? { state: 'ready', devices: state.outputs.map((o) => ({ ...o, default: o.id === state.defaultOutput })) }
      : { state: 'unavailable', devices: [] },
  };
}

// --- Encendido / apagado ----------------------------------------------------

function powerOn({ immediate = false } = {}) {
  if (state.power !== 'off') return;
  clearTimers();
  state = freshState('booting');
  log(`Arrancando (${immediate ? 'inmediato' : `${BOOT_DELAY_MS} ms`})`);
  const up = () => {
    state.power = 'on';
    state.bootedAt = Date.now();
    log('Windows iniciado; servicio disponible (sin sesión)');
    broadcast();
    later(immediate ? 0 : SESSION_DELAY_MS, () => {
      state.session = true;
      log('Sesión de usuario iniciada; agente conectado');
      broadcast();
    });
  };
  if (immediate) up(); else later(BOOT_DELAY_MS, up);
}

function powerOff() {
  clearTimers();
  state = freshState('off');
  log('Apagado');
  for (const ws of wss.clients) ws.terminate();
}

// --- Acciones (lista blanca) ------------------------------------------------

class ActionError extends Error {
  constructor(status, code, message) {
    super(message);
    this.status = status;
    this.code = code;
  }
}

// Textos y códigos iguales que el agente real (pc/src/PegasusDeck.Agent): la PWA se prueba con lo que verá.
function requireSession() {
  if (!state.session) throw new ActionError(409, 'NO_SESSION', 'No hay una sesión de Windows con el agente de Pegasus Deck en marcha.');
}

function requireObs() {
  requireSession();
  if (state.obs.state !== 'ready') throw new ActionError(409, 'OBS_NOT_READY', 'OBS no está conectado.');
}

function requireAudio() {
  requireSession();
  if (behaviour.audioUnavailable) throw new ActionError(404, 'NOT_FOUND', 'No hay ninguna salida de audio activa.');
}

function requireMic() {
  requireSession();
  if (behaviour.micUnavailable) throw new ActionError(404, 'NOT_FOUND', 'No hay ningún micrófono activo.');
}

function requireMedia(expected, verb) {
  requireSession();
  if (state.media === 'none') throw new ActionError(409, 'CONFLICT', 'No hay nada reproduciéndose en el PC.');
  if (!expected.includes(state.media)) throw new ActionError(409, 'CONFLICT', `No se puede ${verb}: la reproducción no está en ese estado.`);
}

function stringParam(params, name, max = 200) {
  const v = params?.[name];
  if (typeof v !== 'string' || v.length === 0 || v.length > max) {
    throw new ActionError(400, 'BAD_PARAMS', `Parámetro «${name}» no válido.`);
  }
  return v;
}

function noExtraParams(params, allowed) {
  for (const k of Object.keys(params)) {
    if (!allowed.includes(k)) throw new ActionError(400, 'BAD_PARAMS', `Parámetro no permitido: ${k}`);
  }
}

/** Acción aceptada que se confirma después (como hace el agente con los eventos de OBS). */
function accepted(apply, message, failCode = null) {
  const operationId = randomHex(8);
  if (!behaviour.dropConfirmations) {
    later(behaviour.confirmDelayMs, () => {
      if (state.power !== 'on') return;
      let result;
      try {
        result = { operationId, status: 'completed', message: apply() };
      } catch (err) {
        result = { operationId, status: 'failed', code: err.code ?? failCode ?? 'INTERNAL', message: err.message };
      }
      broadcast();
      send({ type: 'operation', data: result });
    });
  }
  return { status: 'accepted', operationId, message };
}

const appName = (id) => APPS.find((a) => a.id === id).name;

function openApp(id) {
  if (id === 'explorer') { // alwaysLaunch
    state.windows.explorer += 1;
    return { status: 'completed', message: `Se ha pedido abrir una ventana nueva de ${appName(id)}.` };
  }
  if (state.apps[id] === 'ready' || state.apps[id] === 'running') {
    return { status: 'completed', message: `${appName(id)} traída al frente.` };
  }
  const already = state.apps[id] === 'starting';
  if (!already) {
    state.apps[id] = 'starting';
    if (id === 'obs') state.obs.state = 'connecting';
    later(400, () => { state.apps[id] = 'running'; broadcast(); });
  }
  return accepted(() => {
    state.apps[id] = 'ready';
    if (id === 'obs') state.obs.state = 'ready';
    return `${appName(id)} lista.`;
  }, already ? `${appName(id)} se está abriendo…` : `Abriendo ${appName(id)}…`);
}

function windowsOf(id) {
  if (id === 'explorer') return state.windows.explorer;
  return state.apps[id] === 'ready' || state.apps[id] === 'running' ? 1 : 0;
}

function closeApp(id) {
  state.apps[id] = 'installed';
  if (id === 'obs') {
    state.obs = { ...state.obs, state: 'unavailable', recording: false, recordingPaused: false, streaming: false };
  }
}

const ACTIONS = {
  'app.open': (p) => {
    noExtraParams(p, ['appId']);
    requireSession();
    const id = stringParam(p, 'appId', 40);
    if (!APPS.some((a) => a.id === id)) throw new ActionError(403, 'APP_NOT_ALLOWED', 'Aplicación no autorizada en este PC.');
    return openApp(id);
  },
  'audio.master.toggleMute': () => {
    requireAudio();
    state.audio.muted = !state.audio.muted;
    return { status: 'completed', message: state.audio.muted ? 'Audio silenciado.' : 'Audio activado.' };
  },
  'audio.master.setVolume': (p) => {
    requireAudio();
    state.audio.volume = p.percent;
    return { status: 'completed', message: `Volumen: ${p.percent} %.` };
  },
  'audio.mic.toggleMute': (p) => {
    noExtraParams(p, []);
    requireMic();
    state.mic.muted = !state.mic.muted;
    return { status: 'completed', message: state.mic.muted ? 'Micrófono silenciado.' : 'Micrófono activado.' };
  },
  'audio.output.set': (p) => {
    noExtraParams(p, ['device']);
    requireAudio();
    const target = state.outputs.find((o) => o.id === p.device);
    if (!target) throw new ActionError(404, 'NOT_FOUND', 'Esa salida de audio ya no está conectada.');
    state.defaultOutput = target.id;
    return { status: 'completed', message: `Ahora suena por ${target.name}.` };
  },
  'media.pause': (p) => {
    noExtraParams(p, []);
    requireMedia(['playing'], 'pausar');
    state.media = 'paused';
    return { status: 'completed', message: 'Reproducción en pausa.' };
  },
  'media.play': (p) => {
    noExtraParams(p, []);
    requireMedia(['paused', 'stopped'], 'reanudar');
    state.media = 'playing';
    return { status: 'completed', message: 'Reproducción reanudada.' };
  },
  'app.closeAll': (p) => {
    requireSession();
    const id = p.appId;
    if (!APPS.some((a) => a.id === id)) throw new ActionError(403, 'APP_NOT_ALLOWED', 'Aplicación no autorizada en este PC.');
    const n = windowsOf(id);
    if (n === 0) throw new ActionError(409, 'CONFLICT', `${appName(id)} no tiene ventanas abiertas.`);
    if (id === 'explorer') state.windows.explorer = 0;
    else closeApp(id);
    return { status: 'completed', message: n === 1 ? `Cerrada 1 ventana de ${appName(id)}.` : `Cerradas ${n} ventanas de ${appName(id)}.` };
  },
  'davinci.command': (p) => {
    requireSession();
    if (!['ready', 'running'].includes(state.apps.davinci)) throw new ActionError(409, 'CONFLICT', 'DaVinci Resolve no está abierto.');
    state.resolveCommands.push(p.command);
    return { status: 'completed', message: 'Enviado a DaVinci Resolve.' };
  },
  'windows.lock': (p) => {
    noExtraParams(p, []);
    requireSession();
    state.locked = true;
    return { status: 'completed', message: 'Sesión bloqueada.' };
  },
  'obs.recording.start': (p) => {
    noExtraParams(p, []);
    requireObs();
    if (state.obs.recording) throw new ActionError(409, 'CONFLICT', 'La grabación ya está en curso.');
    return accepted(() => {
      requireObs();
      if (behaviour.silentRecordFailure) {
        throw new ActionError(409, 'CONFLICT', 'OBS no ha podido iniciar la grabación (revisa el aviso en OBS: carpeta, espacio libre o codificador).');
      }
      state.obs.recording = true; state.obs.recordingPaused = false;
      return 'Grabación iniciada.';
    }, 'Iniciando grabación…');
  },
  'obs.recording.stop': (p) => {
    noExtraParams(p, []);
    requireObs();
    if (!state.obs.recording) throw new ActionError(409, 'CONFLICT', 'No hay ninguna grabación en curso.');
    return accepted(() => { requireObs(); state.obs.recording = false; state.obs.recordingPaused = false; return 'Grabación detenida.'; }, 'Deteniendo grabación…');
  },
  'obs.recording.pause': (p) => {
    noExtraParams(p, []);
    requireObs();
    if (!state.obs.recording) throw new ActionError(409, 'CONFLICT', 'No hay ninguna grabación en curso.');
    const resume = state.obs.recordingPaused;
    return accepted(() => {
      requireObs();
      if (!resume && behaviour.pauseUnsupported) {
        throw new ActionError(409, 'CONFLICT', 'OBS no permite pausar la grabación con el codificador configurado (usa uno propio para la grabación).');
      }
      state.obs.recordingPaused = !resume;
      return resume ? 'Grabación reanudada.' : 'Grabación en pausa.';
    }, resume ? 'Reanudando grabación…' : 'Pausando grabación…');
  },
  'obs.streaming.start': (p) => {
    noExtraParams(p, []);
    requireObs();
    if (state.obs.streaming) throw new ActionError(409, 'CONFLICT', 'El streaming ya está activo.');
    return accepted(() => { requireObs(); state.obs.streaming = true; return 'Streaming iniciado.'; }, 'Iniciando streaming…');
  },
  'obs.streaming.stop': (p) => {
    noExtraParams(p, []);
    requireObs();
    if (!state.obs.streaming) throw new ActionError(409, 'CONFLICT', 'No hay ninguna emisión activa.');
    return accepted(() => { requireObs(); state.obs.streaming = false; return 'Streaming detenido.'; }, 'Deteniendo streaming…');
  },
  'obs.scene.set': (p) => {
    noExtraParams(p, ['scene']);
    requireObs();
    const scene = stringParam(p, 'scene', 100);
    if (state.obs.scene === scene) return { status: 'completed', message: 'Ya era la escena activa.' };
    if (!state.obs.scenes.includes(scene)) throw new ActionError(404, 'NOT_FOUND', `La escena «${scene}» no existe en OBS.`);
    return accepted(() => { requireObs(); state.obs.scene = scene; return `Escena: ${scene}.`; }, 'Cambiando de escena…');
  },
  'obs.input.toggleMute': (p) => {
    noExtraParams(p, ['input']);
    requireObs();
    const name = stringParam(p, 'input', 100);
    const input = state.obs.inputs.find((i) => i.name === name);
    if (!input) throw new ActionError(404, 'NOT_FOUND', `La fuente «${name}» no existe en OBS.`);
    return accepted(() => { requireObs(); input.muted = !input.muted; return input.muted ? `${name} silenciado.` : `${name} activado.`; }, 'Cambiando la fuente…');
  },
};

// --- Control del simulador (solo desarrollo) ----------------------------------

const CONTROL = {
  reset: (b) => {
    clearTimers();
    state = freshState(b.power === 'on' ? 'on' : 'off');
    behaviour = { ...DEFAULT_BEHAVIOUR };
    devices.clear();
    pairing = null;
    pairAttemptsByIp.clear();
    for (const ws of wss.clients) ws.terminate();
  },
  wake: () => powerOn(),
  powerOn: () => powerOn({ immediate: true }),
  powerOff: () => powerOff(),
  session: (b) => { if (state.power === 'on') state.session = !!b.value; },
  openApp: (b) => { if (state.session && state.apps[b.id]) { openApp(b.id); } },
  closeApp: (b) => { if (state.apps[b.id]) closeApp(b.id); },
  obsAuthError: () => { if (state.session) { state.apps.obs = 'running'; state.obs.state = 'auth_error'; } },
  obsIncompatible: () => { if (state.session) { state.apps.obs = 'running'; state.obs.state = 'incompatible'; } },
  pairingCode: () => {
    pairing = { code: String(crypto.randomInt(0, 1_000_000)).padStart(6, '0'), expiresAt: Date.now() + 120_000, attempts: 0 };
    log(`Código de emparejamiento: ${pairing.code} (2 min)`);
    return { code: pairing.code };
  },
  issueToken: (b) => issueToken(typeof b.deviceName === 'string' ? b.deviceName : 'pruebas'),
  revokeAll: () => { devices.clear(); for (const ws of wss.clients) ws.close(4401, 'revoked'); },
  dropEvents: () => { for (const ws of wss.clients) ws.terminate(); },
  behaviour: (b) => { behaviour = { ...behaviour, ...b }; },
  // Cambios hechos directamente en OBS (ADR-031): escena activa y lista de escenas.
  obsScene: (b) => { if (state.obs.scenes.includes(b.value)) state.obs.scene = b.value; },
  obsScenes: (b) => { if (Array.isArray(b.value) && b.value.every((x) => typeof x === 'string')) { state.obs.scenes = b.value; if (!b.value.includes(state.obs.scene)) state.obs.scene = b.value[0] ?? null; } },
  media: (b) => { if (['playing', 'paused', 'stopped', 'none'].includes(b.value)) state.media = b.value; },
  // Salida predeterminada cambiada en Windows (sin el iPad), o una salida que se desconecta (ADR-035).
  defaultOutput: (b) => { if (state.outputs.some((o) => o.id === b.value)) state.defaultOutput = b.value; },
  unplugOutput: (b) => { state.outputs = state.outputs.filter((o) => o.id !== b.value); if (state.defaultOutput === b.value) state.defaultOutput = state.outputs[0]?.id ?? null; },
  explorerWindows: (b) => { if (state.session && Number.isInteger(b.value) && b.value >= 0) state.windows.explorer = b.value; },
};

// --- HTTP / WebSocket / UDP --------------------------------------------------

function log(msg) {
  console.log(`[sim ${new Date().toLocaleTimeString()}] ${msg}`);
}

function json(res, status, body) {
  res.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-store' });
  res.end(JSON.stringify(body));
}

const fail = (res, status, code, message) => json(res, status, { ok: false, code, message });

function readBody(req) {
  return new Promise((resolve, reject) => {
    let data = '';
    req.on('data', (c) => {
      data += c;
      if (data.length > 16_384) { reject(new Error('too large')); req.destroy(); }
    });
    req.on('end', () => {
      try { resolve(data ? JSON.parse(data) : {}); } catch (e) { reject(e); }
    });
  });
}

function originAllowed(req) {
  const origin = req.headers.origin;
  return origin === undefined || ALLOWED_ORIGINS.has(origin);
}

function bearer(req) {
  const h = req.headers.authorization ?? '';
  return h.startsWith('Bearer ') ? h.slice(7) : null;
}

function rateLimited(ip) {
  const now = Date.now();
  const recent = (pairAttemptsByIp.get(ip) ?? []).filter((t) => now - t < 60_000);
  recent.push(now);
  pairAttemptsByIp.set(ip, recent);
  return recent.length > 10;
}

const server = http.createServer(async (req, res) => {
  const url = new URL(req.url ?? '/', 'http://sim');

  if (url.pathname.startsWith('/__sim')) {
    if (req.method === 'GET' && url.pathname === '/__sim') {
      res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
      res.end(CONTROL_PAGE);
      return;
    }
    if (req.method === 'GET' && url.pathname === '/__sim/state') {
      return json(res, 200, { ...state, behaviour, devices: [...devices.values()], mac: MAC, udpPort: UDP_PORT });
    }
    if (req.method === 'POST' && url.pathname === '/__sim/control') {
      try {
        const body = await readBody(req);
        const fn = Object.hasOwn(CONTROL, body.op) ? CONTROL[body.op] : null;
        if (!fn) return json(res, 400, { ok: false, message: 'op desconocida' });
        const result = fn(body) ?? {};
        broadcast();
        return json(res, 200, { ok: true, power: state.power, ...result });
      } catch {
        return json(res, 400, { ok: false, message: 'JSON no válido' });
      }
    }
    return json(res, 404, { ok: false });
  }

  // Con el PC apagado o arrancando no hay nadie al otro lado: se corta la conexión.
  if (state.power !== 'on') {
    req.socket.destroy();
    return;
  }

  if (!originAllowed(req)) return fail(res, 403, 'ORIGIN_FORBIDDEN', 'Origen no autorizado.');

  if (req.method === 'GET' && url.pathname === '/api/health') {
    return json(res, 200, { protocol: PROTOCOL, service: { name: 'pegasus-deck', version: '0.2.0-sim' } });
  }

  if (req.method === 'POST' && url.pathname === '/api/pair') {
    if (rateLimited(req.socket.remoteAddress ?? '?')) return fail(res, 429, 'RATE_LIMITED', 'Demasiados intentos. Espera un minuto.');
    let body;
    try { body = await readBody(req); } catch { return fail(res, 400, 'BAD_JSON', 'JSON no válido.'); }
    const code = typeof body?.code === 'string' ? body.code : '';
    const name = typeof body?.deviceName === 'string' ? body.deviceName.trim() : '';
    if (!/^[0-9]{6}$/.test(code) || name.length < 1 || name.length > 40 || /[\p{Cc}\p{Cf}\p{Zl}\p{Zp}]/u.test(name)) {
      return fail(res, 400, 'BAD_PARAMS', 'Código o nombre no válidos.');
    }
    if (!pairing || Date.now() > pairing.expiresAt || pairing.attempts >= 5) {
      return fail(res, 401, 'PAIRING_INVALID', 'Código incorrecto o caducado. Genera uno nuevo en el PC.');
    }
    pairing.attempts++;
    const ok = crypto.timingSafeEqual(Buffer.from(code), Buffer.from(pairing.code));
    if (!ok) return fail(res, 401, 'PAIRING_INVALID', 'Código incorrecto o caducado. Genera uno nuevo en el PC.');
    pairing = null; // un solo uso
    const issued = issueToken(name);
    log(`Dispositivo emparejado: ${name} (${issued.deviceId})`);
    return json(res, 200, { ok: true, ...issued });
  }

  if (req.method === 'POST' && url.pathname === '/api/actions') {
    if (!deviceFor(bearer(req))) return fail(res, 401, 'AUTH_REQUIRED', 'Token no válido o revocado.');
    let body;
    try { body = await readBody(req); } catch { return fail(res, 400, 'BAD_JSON', 'JSON no válido.'); }
    const invalid = validateAction(body);
    if (invalid) return fail(res, ...invalid);
    if (ACTION_SPECS[body.type].destructive) {
      const key = `${bearer(req)}\n${body.type}\n${JSON.stringify(Object.entries(body.params).sort())}`;
      if (!body.confirmToken) {
        const token = crypto.randomBytes(32).toString('base64url');
        confirmations.set(token, { key, expires: Date.now() + 15_000 });
        return json(res, 200, { ok: true, status: 'confirm_required', confirmToken: token, expiresInSec: 15, message: 'Confirma la acción.' });
      }
      const pending = confirmations.get(body.confirmToken);
      confirmations.delete(body.confirmToken);
      if (!pending || pending.expires < Date.now() || pending.key !== key) {
        return fail(res, 409, 'CONFIRMATION_INVALID', 'La confirmación ha caducado o no es válida. Vuelve a intentarlo.');
      }
    }
    try {
      const result = ACTIONS[body.type](body.params);
      log(`Acción ${body.type} -> ${result.status}`);
      broadcast();
      return json(res, 200, { ok: true, ...result });
    } catch (err) {
      if (err instanceof ActionError) return fail(res, err.status, err.code, err.message);
      throw err;
    }
  }

  fail(res, 404, 'NOT_FOUND', 'No encontrado.');
});

const wss = new WebSocketServer({ noServer: true });

server.on('upgrade', (req, socket, head) => {
  if (state.power !== 'on' || !req.url?.startsWith('/api/events') || behaviour.blockEvents) {
    socket.destroy();
    return;
  }
  if (!originAllowed(req)) {
    socket.end('HTTP/1.1 403 Forbidden\r\n\r\n');
    return;
  }
  wss.handleUpgrade(req, socket, head, (ws) => {
    ws.authed = false;
    const authTimer = setTimeout(() => {
      if (!ws.authed) {
        ws.send(JSON.stringify({ type: 'error', code: 'AUTH_REQUIRED', message: 'Falta autenticación.' }));
        ws.close(4401, 'auth');
      }
    }, 3000); // igual que el servicio .NET
    ws.on('message', (raw) => {
      if (ws.authed) return; // el cliente no envía nada más
      clearTimeout(authTimer);
      let msg;
      try { msg = JSON.parse(String(raw)); } catch { msg = null; }
      if (msg?.type !== 'auth' || !deviceFor(msg.token)) {
        ws.send(JSON.stringify({ type: 'error', code: 'AUTH_REQUIRED', message: 'Token no válido o revocado.' }));
        ws.close(4401, 'auth');
        return;
      }
      ws.authed = true;
      ws.send(JSON.stringify({ type: 'ready', data: snapshot() }));
    });
    ws.on('close', () => clearTimeout(authTimer));
  });
});

function send(message) {
  const msg = JSON.stringify(message);
  for (const ws of wss.clients) if (ws.authed && ws.readyState === ws.OPEN) ws.send(msg);
}

function broadcast() {
  if (state.power === 'on') send({ type: 'snapshot', data: snapshot() });
}

// Latido: instantánea periódica (la PWA da el canal por muerto si no recibe nada en 12 s).
setInterval(broadcast, 5000).unref();

// Paquetes mágicos reales (para probar con una app WoL apuntando al puerto UDP_PORT).
const udp = dgram.createSocket({ type: 'udp4', reuseAddr: true });
udp.on('message', (msg, rinfo) => {
  const mac = parseMagicPacket(msg);
  if (!mac) return log(`UDP ignorado de ${rinfo.address} (no es un paquete mágico)`);
  if (mac !== MAC) return log(`Paquete mágico para otra MAC (${mac})`);
  log(`Paquete mágico recibido de ${rinfo.address}`);
  powerOn();
});
udp.on('error', (err) => log(`UDP no disponible: ${err.message}`));
udp.bind(UDP_PORT, '0.0.0.0');

function parseMagicPacket(data) {
  if (![102, 106, 108].includes(data.length)) return null;
  for (let i = 0; i < 6; i++) if (data[i] !== 0xff) return null;
  const mac = data.subarray(6, 12);
  for (let i = 1; i < 16; i++) {
    if (!data.subarray(6 + i * 6, 12 + i * 6).equals(mac)) return null;
  }
  return [...mac].map((b) => b.toString(16).padStart(2, '0').toUpperCase()).join(':');
}

server.listen(PORT, '127.0.0.1', () => {
  log(`Simulador en http://127.0.0.1:${PORT} · panel /__sim · UDP ${UDP_PORT} · MAC ${MAC} · PC ${state.power}`);
});

const CONTROL_PAGE = `<!doctype html><html lang="es"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1"><title>Simulador del PC</title>
<style>
body{font-family:system-ui,sans-serif;background:#0a1020;color:#e2e8f0;margin:0;padding:20px;max-width:760px}
h1{font-size:20px}button{background:#1b2847;color:#fff;border:1px solid #334;border-radius:10px;padding:10px 14px;margin:4px;font-size:15px}
pre{background:#050913;padding:12px;border-radius:10px;font-size:12px;overflow:auto}section{margin:14px 0}
#code{font:700 28px ui-monospace,monospace;letter-spacing:.3em;color:#22c55e;margin-left:8px}
</style></head><body>
<h1>Simulador del PC · Pegasus Deck</h1>
<section><b>Energía:</b>
<button onclick="op('wake')">Paquete mágico (arranque lento)</button>
<button onclick="op('powerOn')">Encender ya</button>
<button onclick="op('powerOff')">Apagar</button></section>
<section><b>Emparejamiento:</b>
<button onclick="pair()">Generar código</button><span id="code"></span>
<button onclick="op('revokeAll')">Revocar todos</button></section>
<section><b>Sesión:</b>
<button onclick="op('session',{value:true})">Iniciar sesión</button>
<button onclick="op('session',{value:false})">Cerrar sesión</button></section>
<section><b>Apps:</b>
${APPS.map((a) => `<button onclick="op('openApp',{id:'${a.id}'})">Abrir ${a.name}</button><button onclick="op('closeApp',{id:'${a.id}'})">Cerrar ${a.name}</button>`).join('')}
<button onclick="op('obsAuthError')">OBS: contraseña incorrecta</button>
<button onclick="op('obsIncompatible')">OBS: versión incompatible</button></section>
<section><b>Fallos:</b>
<button onclick="op('dropEvents')">Cortar canal de eventos</button>
<button onclick="op('behaviour',{dropConfirmations:true})">OBS no confirma</button>
<button onclick="op('behaviour',{silentRecordFailure:true})">OBS no puede grabar</button>
<button onclick="op('behaviour',{pauseUnsupported:true})">OBS no puede pausar</button>
<button onclick="op('behaviour',{audioUnavailable:true})">Sin salida de audio</button>
<button onclick="op('behaviour',{micUnavailable:true})">Sin micrófono</button>
<button onclick="op('behaviour',{dropConfirmations:false,confirmDelayMs:300,silentRecordFailure:false,pauseUnsupported:false,audioUnavailable:false,micUnavailable:false})">Todo normal</button></section>
<section><b>Explorador:</b>
<button onclick="op('explorerWindows',{value:6})">6 ventanas abiertas</button>
<button onclick="op('explorerWindows',{value:0})">Ninguna</button></section>
<section><b>Multimedia:</b>
<button onclick="op('media',{value:'playing'})">Reproduciendo</button>
<button onclick="op('media',{value:'paused'})">En pausa</button>
<button onclick="op('media',{value:'none'})">Nada</button></section>
<pre id="s"></pre>
<script>
async function op(o,extra){const r=await fetch('/__sim/control',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({op:o,...extra})});load();return r.json()}
async function pair(){const r=await op('pairingCode');document.getElementById('code').textContent=r.code||''}
async function load(){document.getElementById('s').textContent=JSON.stringify(await (await fetch('/__sim/state')).json(),null,2)}
load();setInterval(load,1500);
</script></body></html>`;
