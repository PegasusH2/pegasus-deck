---
name: pegasus-obs
description: Integración de Pegasus Deck con OBS mediante obs-websocket 5.x (autenticación, peticiones, eventos, reconexión, sincronización de estado). Usar al implementar o revisar cualquier acción, estado o condición de OBS.
---

# Integración con OBS

## Dónde vive

El cliente obs-websocket está en el **agente de sesión** (`pc/PegasusDeck.Agent`), conectado a `ws://127.0.0.1:4455`.
La PWA nunca habla con OBS ni conoce su contraseña: recibe el estado del servicio y envía acciones del catálogo.

## Protocolo (obs-websocket 5.x, incluido en OBS 28+)

- Handshake: `Hello (op 0)` → `Identify (op 1)` con `authentication` = base64(sha256(base64(sha256(password + salt)) + challenge)) y `eventSubscriptions` → `Identified (op 2)`.
- Peticiones: `Request (op 6)` con `requestType`, `requestId`, `requestData` → `RequestResponse (op 7)` (`requestStatus.result`, `code`).
- Eventos: `Event (op 5)`. Suscribir como mínimo General, Scenes, Inputs, Outputs.
- Verifica capacidades con `GetVersion` (`obsWebSocketVersion`, `availableRequests`) antes de ofrecer una acción.

## Peticiones y eventos que usamos

| Acción Pegasus | Petición | Confirmación (evento) |
|---|---|---|
| `obs.recording.start/stop` | `StartRecord` / `StopRecord` | `RecordStateChanged` (`OBS_WEBSOCKET_OUTPUT_STARTED` / `_STOPPED`) |
| `obs.recording.pause` | `PauseRecord` / `ResumeRecord` según el estado | `RecordStateChanged` (`_PAUSED` / `_RESUMED`) |
| `obs.streaming.start/stop` | `StartStream` / `StopStream` | `StreamStateChanged` |
| `obs.scene.set` | `SetCurrentProgramScene` (`sceneName`) | `CurrentProgramSceneChanged` |
| `obs.input.toggleMute` | `ToggleInputMute` (`inputName`) | `InputMuteStateChanged` |
| Estado inicial | `GetRecordStatus`, `GetStreamStatus`, `GetCurrentProgramScene`, `GetSceneList`, `GetInputList` | — |

Comprueba nombres exactos en la documentación del protocolo de la versión instalada (`GetVersion`) antes de usarlos.

## Semántica real comprobada (obs-websocket 5.2.3 / OBS 29.1.3, ADR-021)

- `outputActive` en `RecordStateChanged`/`StreamStateChanged` solo es true en STARTED/RESUMED/RECONNECTED: el estado se
  calcula con `outputState` (PAUSED = grabando y en pausa). `GetRecordStatus.outputActive` sí es true en pausa.
- `StartRecord`/`PauseRecord` responden "Success" aunque OBS no grabe/pause (solo muestra un aviso en el PC): verificar
  con `GetRecordStatus` si no llega el evento en ~2 s.
- Durante `CurrentSceneCollectionChanging` no hacer peticiones; recargar todo en `CurrentSceneCollectionChanged`.
- Pruebas: `pc/tests/.../FakeObs.cs` imita esta semántica; no usar nunca el OBS real del usuario en pruebas automáticas.

## Reglas

- **Petición enviada ≠ acción completada.** `RequestResponse` correcto = *aceptada*. Solo el evento de salida confirma. La PWA muestra "Iniciando grabación…" hasta el evento o hasta `timeoutMs` (por defecto 10 s) → error "OBS no confirmó".
- Estados del agente con OBS: `unavailable` (proceso no abierto) · `connecting` · `ready` · `auth_error` · `incompatible`. Contraseña incorrecta no reintenta en bucle.
- Reconexión con espera exponencial (1, 2, 4… máx. 15 s) mientras el proceso `obs64.exe` exista; si el proceso desaparece, `unavailable` sin reintentar.
- Al reconectar, volver a pedir el estado completo: nunca confiar en el estado anterior.
- Escenas y fuentes que no existen ⇒ condición `obs.source.exists`/`obs.scene.exists` falsa y botón deshabilitado con motivo.
- Simulador: `pwa/sim/server.mjs` imita este comportamiento; mantenerlo alineado al cambiar el contrato.
