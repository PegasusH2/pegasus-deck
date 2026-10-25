# Contrato PWA ↔ PC (protocolo 2)

Fuente única de la API entre la PWA, el simulador (`pwa/sim`) y el servicio de Windows (`pc/`). ADR-007.

| Ruta | Auth | Esquema |
|---|---|---|
| `GET /api/health` | No | `health.schema.json` — solo protocolo y versión |
| `POST /api/pair` | No (limitado por intentos) | `pair.schema.json` (`request` / `response`) |
| `POST /api/actions` | `Authorization: Bearer <token>` | `action.schema.json` (`request` / `response`) |
| `WS /api/events` | Mensaje `auth` en ≤ 3 s | `events.schema.json` (`client` / `server`) con `snapshot.schema.json` |

Reglas comunes:

- Todas las respuestas de error: `{ ok: false, code, message }` con `code` de `common.schema.json`.
- Peticiones con cabecera `Origin` no autorizada → `403 ORIGIN_FORBIDDEN`. `Host` inesperado → `403 HOST_FORBIDDEN`.
- Orden de comprobación de `/api/actions` (ADR-014): auth → JSON (`BAD_JSON`) → estructura (`BAD_PARAMS`) → tipo
  registrado (`UNKNOWN_ACTION`) → parámetros (`BAD_PARAMS`) → precondiciones (`NO_SESSION`, `OBS_NOT_READY`…) → ejecución.
- Quien envía respeta los límites del esquema (ADR-015); quien recibe valida.
- `status: "accepted"` no es éxito: la acción termina con un mensaje WS `operation` (`completed` / `failed`).
- El token nunca viaja en la URL.
- Cambio incompatible ⇒ subir `protocol`. La PWA muestra "servicio incompatible" si no coincide.

## Lista blanca de acciones

`actions.json`: tipos que ejecuta el PC, sus parámetros (`id` / `name` / `percent`), modo de confirmación y casos válidos/inválidos.
ADR-022 añadió `audio.mic.toggleMute`, `media.pause` y `media.play`, y los campos opcionales `mic` y `media` de la instantánea.
La comprueban `pwa/tests/contract/actions.test.ts` y `pc/tests/…/CatalogContractTests.cs`; el simulador la lee directamente.

Diferencia aceptada del simulador: no comprueba `Host` (en desarrollo se usa desde `localhost` y la IP de la LAN).

## Ejemplos

`examples/index.json` asocia cada carpeta con su esquema. Cada carpeta tiene `valid/` (deben pasar) e `invalid/`
(deben fallar). Los usan:

- `pwa/tests/contract/` — esquema JSON (Ajv) + analizadores Zod de la PWA.
- `pwa/tests/contract/sim.test.ts` — respuestas reales del simulador.
- `pc/tests/` — deserialización y validación del servicio .NET.

Al cambiar el contrato: esquema → ejemplo → tipos/analizadores de la PWA → simulador → .NET.
