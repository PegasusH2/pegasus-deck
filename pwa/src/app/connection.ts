// Conexión con el PC, en dos capas independientes:
//  1. Sondeo HTTP de /api/health (sin auth) → máquina del PC.
//  2. WebSocket /api/events autenticado → máquina del enlace + instantánea + operaciones.
// La API está en el mismo origen que la PWA; con el PC apagado la PWA se abre
// desde la caché del service worker.

import { PROTOCOL, serverMessageSchema, WS_CLOSE_AUTH } from '../core/contract';
import { backoffMs, HANDSHAKE_TIMEOUT_MS } from '../core/linkMachine';
import type { PcState } from '../core/pcMachine';
import { getHealth } from './api';
import { log } from './log';
import { failAllPending, onOperationResult } from './operations';
import { useApp } from './store';

const PROBE_TIMEOUT_MS = 2500;
/** Sin mensajes en este tiempo el WebSocket se da por muerto (el servidor envía latido cada 5 s). */
const WS_STALE_MS = 12_000;

const POLL_MS: Record<PcState, number> = {
  UNKNOWN: 500,
  PC_OFFLINE: 10_000,
  PC_ONLINE: 2_000, // sin enlace; con enlace listo se usa ONLINE_WITH_LINK_MS
};
const ONLINE_WITH_LINK_MS = 15_000;

class PcConnection {
  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private tickTimer: ReturnType<typeof setInterval> | null = null;
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null;
  private handshakeTimer: ReturnType<typeof setTimeout> | null = null;
  private ws: WebSocket | null = null;
  private wsLastMessage = 0;
  private wsInvalid = 0;
  private probing: Promise<boolean> | null = null;
  private running = false;
  private unsubscribe: (() => void) | null = null;

  start(): void {
    if (this.running) return;
    this.running = true;
    document.addEventListener('visibilitychange', this.onVisibility);
    this.tickTimer = setInterval(this.onTick, 1000);
    // Si el PC deja de estar online, el enlace cae; si cambia el token, reconectar.
    this.unsubscribe = useApp.subscribe((s, prev) => {
      if (prev.pc.state === 'PC_ONLINE' && s.pc.state !== 'PC_ONLINE') {
        s.dispatchLink({ type: 'PC_GONE', at: Date.now() });
        this.closeSocket('pc-gone');
        failAllPending('El PC ha dejado de responder antes de confirmar la acción.');
      }
      if (s.device !== prev.device) {
        this.closeSocket('token-changed');
        s.dispatchLink({ type: 'TOKEN_CHANGED', at: Date.now() });
        this.maybeConnect();
      }
    });
    void this.probeNow();
  }

  stop(): void {
    this.running = false;
    document.removeEventListener('visibilitychange', this.onVisibility);
    if (this.tickTimer) clearInterval(this.tickTimer);
    this.unsubscribe?.();
    this.clearPoll();
    this.closeSocket('stop');
  }

  /** Comprueba ya el estado del PC ("Comprobar estado", volver a primer plano…). */
  async probeNow(): Promise<boolean> {
    this.clearPoll();
    const ok = await this.probe();
    this.schedule();
    return ok;
  }

  private probe(): Promise<boolean> {
    // Si ya hay un sondeo en marcha, se espera su resultado en vez de duplicarlo.
    this.probing ??= this.doProbe().finally(() => { this.probing = null; });
    return this.probing;
  }

  private async doProbe(): Promise<boolean> {
    const app = useApp.getState();
    try {
      const health = await getHealth(PROBE_TIMEOUT_MS);
      app.dispatchPc({ type: 'PROBE_OK', at: Date.now() });
      const { link } = useApp.getState();
      if (health.protocol !== PROTOCOL) {
        app.dispatchLink({ type: 'PROTOCOL_MISMATCH', at: Date.now(), protocol: health.protocol });
        this.closeSocket('protocol');
      } else if (link.state === 'INCOMPATIBLE') {
        app.dispatchLink({ type: 'TOKEN_CHANGED', at: Date.now() }); // el PC se actualizó: reintentar
      }
      this.maybeConnect();
      return true;
    } catch {
      app.dispatchPc({ type: 'PROBE_FAIL', at: Date.now() });
      return false;
    }
  }

  private schedule(): void {
    if (!this.running || document.visibilityState === 'hidden') return;
    this.clearPoll();
    const { pc, link } = useApp.getState();
    const delay = pc.state === 'PC_ONLINE' && link.state === 'READY' ? ONLINE_WITH_LINK_MS : POLL_MS[pc.state];
    this.pollTimer = setTimeout(async () => {
      await this.probe();
      this.schedule();
    }, delay);
  }

  private clearPoll(): void {
    if (this.pollTimer) clearTimeout(this.pollTimer);
    this.pollTimer = null;
  }

  /** Abre el canal de eventos si el PC está online y el enlace lo permite. */
  private maybeConnect(): void {
    const app = useApp.getState();
    if (!this.running || this.ws || this.reconnectTimer) return;
    if (app.pc.state !== 'PC_ONLINE' || app.link.state !== 'DISCONNECTED') return;
    if (!app.device) {
      app.dispatchLink({ type: 'NO_TOKEN', at: Date.now() });
      return;
    }
    const delay = app.link.attempts > 0 ? backoffMs(app.link) : 0;
    this.reconnectTimer = setTimeout(() => {
      this.reconnectTimer = null;
      this.openSocket();
    }, delay);
  }

  private openSocket(): void {
    const app = useApp.getState();
    const token = app.device?.token;
    if (!token || app.pc.state !== 'PC_ONLINE' || app.link.state !== 'DISCONNECTED') return;

    app.dispatchLink({ type: 'CONNECT', at: Date.now() });
    const proto = location.protocol === 'https:' ? 'wss:' : 'ws:';
    const ws = new WebSocket(`${proto}//${location.host}/api/events`);
    this.ws = ws;
    this.wsLastMessage = Date.now();
    this.wsInvalid = 0;
    this.handshakeTimer = setTimeout(() => {
      if (this.ws === ws && useApp.getState().link.state === 'CONNECTING') {
        log('warn', 'link.handshake-timeout');
        ws.close();
      }
    }, HANDSHAKE_TIMEOUT_MS);

    ws.onopen = () => {
      if (this.ws !== ws) return;
      // El token va en el primer mensaje, nunca en la URL.
      ws.send(JSON.stringify({ type: 'auth', token }));
    };

    ws.onmessage = (msg) => {
      if (this.ws !== ws) return;
      let raw: unknown;
      try {
        raw = JSON.parse(String(msg.data));
      } catch {
        log('warn', 'link.bad-json');
        return;
      }
      const parsed = serverMessageSchema.safeParse(raw);
      if (!parsed.success) {
        // Un mensaje no válido no es un latido: no se muestran datos viejos como actuales.
        log('warn', 'link.bad-message', { issue: parsed.error.issues[0]?.message, path: parsed.error.issues[0]?.path.join('.') });
        if (++this.wsInvalid >= 3) ws.close();
        return;
      }
      this.wsLastMessage = Date.now();
      this.wsInvalid = 0;
      const s = useApp.getState();
      const m = parsed.data;
      switch (m.type) {
        case 'ready':
          if (this.handshakeTimer) clearTimeout(this.handshakeTimer);
          s.dispatchLink({ type: 'CONNECTED', at: Date.now() });
          s.setSnapshot(m.data);
          s.dispatchPc({ type: 'PROBE_OK', at: Date.now() });
          log('info', 'link.ready');
          this.schedule();
          break;
        case 'snapshot':
          s.setSnapshot(m.data);
          s.dispatchPc({ type: 'PROBE_OK', at: Date.now() });
          break;
        case 'operation':
          onOperationResult(m.data);
          break;
        case 'error':
          log('warn', 'link.server-error', { errorCode: m.code });
          if (m.code === 'AUTH_REQUIRED') s.dispatchLink({ type: 'AUTH_REJECTED', at: Date.now() });
          break;
      }
    };

    ws.onclose = (ev) => {
      if (this.ws !== ws) return;
      this.ws = null;
      if (this.handshakeTimer) clearTimeout(this.handshakeTimer);
      const s = useApp.getState();
      if (ev.code === WS_CLOSE_AUTH) {
        s.dispatchLink({ type: 'AUTH_REJECTED', at: Date.now() });
        log('warn', 'link.auth-rejected');
        return;
      }
      s.dispatchLink({ type: 'LOST', at: Date.now() });
      failAllPending('Se ha perdido la conexión con el PC antes de confirmar la acción.');
      log('info', 'link.lost', { wsCode: ev.code });
      // ¿Se ha ido el PC o solo el canal? Comprobarlo ya; si sigue online se reconecta con espera.
      if (this.running) void this.probeNow();
    };

    ws.onerror = () => ws.close();
  }

  private closeSocket(reason: string): void {
    if (this.reconnectTimer) clearTimeout(this.reconnectTimer);
    this.reconnectTimer = null;
    if (this.handshakeTimer) clearTimeout(this.handshakeTimer);
    const ws = this.ws;
    this.ws = null;
    if (ws) {
      log('debug', 'link.close', { reason });
      ws.close();
    }
  }

  private onTick = (): void => {
    useApp.getState().dispatchPc({ type: 'TICK', at: Date.now() });
    if (this.ws && Date.now() - this.wsLastMessage > WS_STALE_MS) {
      log('warn', 'link.stale');
      this.ws.close();
    }
  };

  private onVisibility = (): void => {
    if (document.visibilityState === 'visible') {
      void this.probeNow();
    } else {
      // iOS congela la PWA en segundo plano: no tiene sentido mantener temporizadores.
      this.clearPoll();
    }
  };
}

export const connection = new PcConnection();
