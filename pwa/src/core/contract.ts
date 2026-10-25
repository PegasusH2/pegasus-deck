// Analizadores del contrato PWA ↔ PC (protocolo 2). Fuente: contracts/schemas.
// Todo lo que llega del servicio pasa por aquí antes de usarse.
// P-1: lo que envía el PC se analiza en modo tolerante (los campos desconocidos se
// descartan); lo que envía la PWA es estricto.

import { z } from 'zod';

export const PROTOCOL = 2;

export const ERROR_CODES = [
  'AUTH_REQUIRED', 'ORIGIN_FORBIDDEN', 'HOST_FORBIDDEN', 'RATE_LIMITED',
  'PAIRING_INVALID', 'BAD_JSON', 'BAD_PARAMS', 'UNKNOWN_ACTION',
  'NO_SESSION', 'OBS_NOT_READY', 'APP_NOT_ALLOWED', 'APP_NOT_FOUND',
  'NOT_FOUND', 'CONFLICT', 'TIMEOUT', 'BUSY', 'INTERNAL', 'CONFIRMATION_INVALID',
] as const;

export type ErrorCode = (typeof ERROR_CODES)[number];

/** Objeto tolerante: descarta campos desconocidos (para lo que envía el PC). */
const tolerant = <T extends z.ZodRawShape>(shape: T) => z.object(shape).strip();

const errorCode = z.enum(ERROR_CODES);
const operationId = z.string().regex(/^[a-f0-9]{16}$/);
const message = z.string().max(300);

export const apiErrorSchema = tolerant({ ok: z.literal(false), code: errorCode, message });
export type ApiError = z.infer<typeof apiErrorSchema>;

const serviceInfo = tolerant({ name: z.literal('pegasus-deck'), version: z.string().max(40) });

export const healthSchema = tolerant({ protocol: z.number().int().min(1), service: serviceInfo });
export type Health = z.infer<typeof healthSchema>;

export const obsStates = ['unavailable', 'connecting', 'ready', 'auth_error', 'incompatible'] as const;
export const appStates = ['not_installed', 'installed', 'starting', 'running', 'ready'] as const;
export const mediaStates = ['playing', 'paused', 'stopped', 'none', 'unavailable'] as const;

export const snapshotSchema = tolerant({
  protocol: z.literal(PROTOCOL),
  service: serviceInfo,
  windows: tolerant({
    booted: z.boolean(),
    hostname: z.string().max(63),
    uptimeSec: z.number().int().min(0),
  }),
  session: tolerant({ ready: z.boolean() }),
  obs: tolerant({
    state: z.enum(obsStates),
    recording: z.boolean(),
    recordingPaused: z.boolean(),
    streaming: z.boolean(),
    scene: z.string().max(100).nullable(),
    scenes: z.array(z.string().max(100)).max(200),
    inputs: z.array(tolerant({ name: z.string().max(100), muted: z.boolean() })).max(200),
  }),
  apps: z.array(tolerant({
    id: z.string().regex(/^[a-z0-9][a-z0-9-]{0,39}$/),
    name: z.string().max(60),
    state: z.enum(appStates),
    /** ADR-024: ventanas abiertas que se pueden cerrar (opcional). */
    windows: z.number().int().min(0).max(500).optional(),
    /** ADR-027: juego (por su carpeta de instalación), para el icono. */
    kind: z.enum(['game']).optional(),
  })).max(100),
  /** P-4: si el agente informa de verdad del estado de las apps. */
  appsDetection: z.enum(['ok', 'unavailable']),
  audio: tolerant({
    state: z.enum(['ready', 'unavailable']),
    muted: z.boolean(),
    volume: z.number().int().min(0).max(100),
  }),
  /** ADR-022: micrófono y multimedia de Windows. Opcionales: si faltan, no disponibles. */
  mic: tolerant({
    state: z.enum(['ready', 'unavailable']),
    muted: z.boolean(),
  }).default({ state: 'unavailable', muted: false }),
  media: tolerant({
    state: z.enum(mediaStates),
  }).default({ state: 'unavailable' }),
});
export type ServiceSnapshot = z.infer<typeof snapshotSchema>;
export type ObsState = ServiceSnapshot['obs']['state'];
export type AppState = ServiceSnapshot['apps'][number]['state'];

// --- Lo que envía la PWA: estricto ---------------------------------------------

export const pairRequestSchema = z.object({
  code: z.string().regex(/^[0-9]{6}$/, 'El código tiene 6 cifras'),
  deviceName: z.string().trim().min(1, 'Pon un nombre').max(40)
    // Sin caracteres de control ni de formato: el nombre se muestra en la consola del PC.
    .regex(/^[^\p{Cc}\p{Cf}\p{Zl}\p{Zp}]+$/u, 'El nombre tiene caracteres no permitidos'),
}).strict();

const paramValue = z.union([z.string().max(200), z.number(), z.boolean()]);

export const actionRequestSchema = z.object({
  type: z.string().max(60).regex(/^[a-z]+(\.[a-zA-Z]+)+$/),
  params: z.record(paramValue).refine((p) => Object.keys(p).length <= 10, 'Demasiados parámetros'),
  /** ADR-024: segundo paso de una acción destructiva. */
  confirmToken: z.string().regex(/^[A-Za-z0-9_-]{20,64}$/).optional(),
}).strict();

export const clientMessageSchema = z.object({ type: z.literal('auth'), token: z.string().min(1).max(200) }).strict();

// --- Respuestas y mensajes del PC: tolerantes ------------------------------------

export const pairResponseSchema = z.union([
  tolerant({
    ok: z.literal(true),
    deviceId: z.string().regex(/^[a-f0-9]{16}$/),
    token: z.string().regex(/^[A-Za-z0-9_-]{43,100}$/),
  }),
  apiErrorSchema,
]);

export const actionResponseSchema = z.union([
  tolerant({ ok: z.literal(true), status: z.literal('completed'), message }),
  tolerant({ ok: z.literal(true), status: z.literal('accepted'), operationId, message: message.optional() }),
  // ADR-024: acción destructiva; no se ha ejecutado hasta repetirla con el token.
  tolerant({
    ok: z.literal(true), status: z.literal('confirm_required'),
    confirmToken: z.string().regex(/^[A-Za-z0-9_-]{20,64}$/), expiresInSec: z.number().int().min(1).max(60), message,
  }),
  apiErrorSchema,
]);
export type ActionResponse = z.infer<typeof actionResponseSchema>;

export const operationResultSchema = tolerant({
  operationId,
  status: z.enum(['completed', 'failed']),
  code: errorCode.optional(),
  message,
});
export type OperationResult = z.infer<typeof operationResultSchema>;

export const serverMessageSchema = z.discriminatedUnion('type', [
  tolerant({ type: z.literal('ready'), data: snapshotSchema }),
  tolerant({ type: z.literal('snapshot'), data: snapshotSchema }),
  tolerant({ type: z.literal('operation'), data: operationResultSchema }),
  tolerant({ type: z.literal('error'), code: errorCode, message }),
]);
export type ServerMessage = z.infer<typeof serverMessageSchema>;

/** Código WebSocket con el que el servidor cierra por autenticación. */
export const WS_CLOSE_AUTH = 4401;
