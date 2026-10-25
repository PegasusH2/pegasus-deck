---
name: pegasus-windows
description: Automatización de Windows en Pegasus Deck (servicio vs agente de sesión, lista blanca de apps, detección eficiente de procesos/ventanas, volumen, bloqueo, apagado). Usar al implementar o revisar código en pc/ o acciones de categoría system/apps.
---

# Automatización de Windows

## Quién hace qué

| Necesita… | Proceso | Motivo |
|---|---|---|
| Responder antes del login, API, apagar/reiniciar | Servicio (sesión 0) | Disponible sin usuario |
| Abrir/enfocar apps, `LockWorkStation`, audio, OBS | Agente (sesión del usuario) | Un servicio **no** puede mostrar ventanas en el escritorio del usuario |

Si el agente no está conectado, el servicio responde `409 NO_SESSION` y la PWA muestra "Sin sesión de Windows".

## Lista blanca de aplicaciones

- Fichero en el PC: `%ProgramData%\PegasusDeck\apps.json` (carpeta protegida; lo edita un administrador, nunca la PWA).
- Formato real (ADR-018): `id`, `name`, `exePath`, `args` (fijos), `readyLevel` (`process` | `window`), `alwaysLaunch`.
  Validación en `pc/src/PegasusDeck.Core/Apps/AppsFile.cs` (intérpretes de órdenes prohibidos).
- El servicio lo envía al agente al conectarse; el agente lo valida de nuevo.
- Lanzar con `ProcessStartInfo { FileName = exePath, UseShellExecute = false, ArgumentList = args fijos }`. **Nunca** `cmd /c`, `powershell -Command`, ni concatenar texto del cliente.
- Antes de lanzar: el ejecutable existe y su ruta coincide con la registrada (opcional: firma Authenticode).

## Detección de aplicaciones

- Instantánea de procesos cada 2 s **filtrada a la lista blanca** (comparar ruta completa del ejecutable, no solo nombre).
- `window`: `EnumWindows` + ventana visible sin propietario del PID + `IsHungAppWindow == false`. Hoy se sondea (2 s; 0,5 s
  mientras se espera a que una app se abra). `SetWinEventHook` queda como optimización futura.
- `probe` (no implementado): para OBS la "disponibilidad real" es el cliente obs-websocket (`obs.ready`), no la app.
- Implementación: `pc/src/PegasusDeck.Agent/Apps/AppsMonitor.cs`. Pruebas: `AgentLinkTests` (usa una copia sin ventana
  del agente como app de pruebas: nunca lances apps reales del usuario en pruebas automáticas).
- Solo se publica `ready` al alcanzar el `readyLevel` configurado. El estado se envía al servicio solo cuando cambia
  (agrupado 150 ms) y como latido cada 10 s.
- Nada de WMI en bucle ni consultas que requieran administrador.

## Acciones de sistema

- Bloquear: `LockWorkStation()` desde el agente.
- Volumen: Core Audio (`IAudioEndpointVolume`) desde el agente. Micrófono: el mismo con `eCapture` (ADR-022).
- Multimedia (pausar/reanudar): controles multimedia de Windows (SMTC) en el agente, por separado y confirmados leyendo
  el estado; el agente usa `net8.0-windows10.0.19041.0` (ADR-022).
- Atajos de teclado (si se añaden): `SendInput` con combinaciones de una lista cerrada; nunca texto libre.
- Apagar/reiniciar: servicio, `InitiateShutdown` con aviso; requiere confirmación en dos pasos (ver `pegasus-security`).
- Reiniciar el servicio: salida controlada con código ≠ 0 y recuperación del SCM; no `sc stop/start` desde el propio servicio.

## Errores

Cada handler devuelve `{ ok, code, message }` con códigos estables (`NO_SESSION`, `APP_NOT_ALLOWED`, `APP_NOT_FOUND`, `TIMEOUT`, `BUSY`…). Procesos colgados: timeout por acción y cancelación; nunca bloquear el hilo del servicio.
