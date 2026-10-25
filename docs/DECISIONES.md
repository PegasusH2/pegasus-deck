# Decisiones de arquitectura (ADR)

Formato: contexto → decisión → consecuencias. Estado: **Aceptada**, **Propuesta** (pendiente de aprobación del usuario) o **Sustituida**.

---

## ADR-001 · Wake-on-LAN mediante Atajo de iOS — Aceptada (2026-10-18)

**Contexto.** Safari no tiene sockets UDP; con el PC apagado no hay servicio que haga de relé; no se quiere hardware extra, servicios de pago ni puertos abiertos.
**Decisión.** La PWA abre `shortcuts://run-shortcut?name=…`; un Atajo con una app WoL gratuita envía el paquete desde el iPad. Alternativa: página WoL del router. Opcional: dispositivo siempre encendido que el usuario ya tenga.
**Consecuencias.** La PWA no puede saber si el paquete salió: el encendido solo se confirma cuando responde el servicio. Requiere configurar el Atajo una vez. **Viabilidad en el iPad real: pendiente de prueba física.**

## ADR-002 · Servicio de Windows + agente de sesión en .NET 8 — Aceptada (2026-10-18)

**Contexto.** Los servicios de Windows (sesión 0) no pueden abrir programas en el escritorio del usuario.
**Decisión.** Servicio (API, auth, `/health` antes del login, apagado) + agente de bandeja (apps, bloqueo, audio, OBS), comunicados por named pipe.
**Consecuencias.** Dos procesos que instalar y vigilar. El estado "Windows iniciado sin sesión" es visible y explica qué botones faltan. **Requiere instalar el SDK de .NET 8 (no está en el equipo).**

## ADR-003 · PWA servida por el PC con HTTPS y CA local — Aceptada (2026-10-18)

**Contexto.** El service worker (abrir la PWA con el PC apagado) exige HTTPS de confianza.
**Decisión.** El servicio sirve PWA y API en el mismo origen `https://<ip>:8443`; certificado firmado por una CA local (mkcert) cuyo certificado raíz se instala en el iPad.
**Consecuencias.** Mismo origen ⇒ sin CORS para la PWA propia. Paso manual en el iPad (instalar y confiar en el perfil).

## ADR-004 · Lista blanca de aplicaciones editable solo en el PC — Aceptada (2026-10-18)

**Decisión.** Rutas de ejecutables solo en `%ProgramData%\PegasusDeck\apps.json`; la PWA trabaja con ids.
**Consecuencias.** Un JSON importado o manipulado no puede ejecutar nada nuevo.

## ADR-005 · Modelo de estados por capas — Aceptada (2026-10-18)

**Contexto.** Los nuevos requisitos piden distinguir `PC_OFFLINE`, `PC_BOOTING`, `PC_ONLINE`, `SERVICE_CONNECTING`, `SERVICE_READY`, `OBS_CONNECTING`, `OBS_READY`, `DEGRADED` y `ERROR`. Una única máquina con todos ellos mezclaría señales independientes (el PC puede estar listo y OBS cerrado).
**Decisión.** Tres máquinas independientes (PC, enlace con el servicio, OBS) + un **estado global derivado** (función pura) con esos 9 valores para la cabecera y los resúmenes. Detalle en `ARQUITECTURA_AMPLIADA.md` §14.1.
**Limitación honesta.** Desde Safari no se puede medir "PC en la red" sin el servicio (no hay ping). `PC_ONLINE` significa "el PC responde por HTTPS (servicio en marcha, Windows iniciado)". Una señal de red independiente exigiría otro dispositivo encendido.

## ADR-006 · Resultado de acción: aceptada vs completada — Aceptada (2026-10-18)

**Decisión.** La API responde `{status: "completed"}` cuando la acción terminó, o `{status: "accepted", operationId}` cuando hay que esperar confirmación (OBS, abrir apps); la confirmación llega por WebSocket (`operation.completed/failed`). La PWA muestra "en curso" hasta la confirmación o el `timeoutMs` de la acción.
**Consecuencias.** Cumple "no confundir petición enviada con acción completada"; las macros solo avanzan con pasos confirmados.

## ADR-007 · Contrato compartido en `contracts/` (JSON Schema) — Aceptada (2026-10-18)

**Decisión.** El contrato PWA↔PC (instantánea, acciones, eventos, errores) se define en JSON Schema con ejemplos; la PWA (Zod), el simulador y .NET se prueban contra los mismos ejemplos.
**Alternativa descartada.** Generar código desde OpenAPI: añade herramientas y complejidad para una API pequeña.

## ADR-008 · Sistema de diseño con tokens CSS desde la fase 2 — Aceptada (2026-10-18)

**Contexto.** El estilo actual de los botones (degradado + halo de color) choca con la nueva guía ("evitar efectos luminosos y degradados decorativos"). Rehacer estilos al final obligaría a tocar todas las pantallas.
**Decisión.** Introducir `tokens.css` y un kit mínimo de componentes al inicio de la fase 2 y construir el editor ya sobre él. La fase 5 queda para pulido y pruebas táctiles.

## ADR-009 · Dependencias — Aceptada (2026-10-18)

| Dependencia | Decisión | Motivo |
|---|---|---|
| react, react-dom | Mantener | Editor con formularios dinámicos; coste de rehacerlo sin framework alto |
| zod | Mantener | Validación de importación, ajustes y parámetros: requisito de seguridad |
| zustand | Mantener | 1 KB; evita prop drilling y re-renderizados del panel |
| lucide-react | Mantener | Iconografía uniforme; solo se incluyen los iconos usados |
| vite-plugin-pwa | Mantener | Genera el service worker con precarga correcta |
| tailwindcss | Mantener, sobre tokens CSS | Utilidades; los valores salen de variables |
| idb | **Eliminada** (fase 2) | Sustituida por ~40 líneas sobre IndexedDB nativo (`pwa/src/app/persistence.ts`) |
| ajv (solo desarrollo) | **Añadida** (fase 2) | Valida en las pruebas que los ejemplos de `contracts/` cumplen los esquemas JSON. No se incluye en la PWA |
| ws, @types/ws (solo desarrollo) | Añadidas (fase 1) | Simulador del PC y pruebas de contrato del WebSocket |
| @playwright/test, vitest, typescript, vite, tailwindcss… (desarrollo) | Mantener | Herramientas de compilación y pruebas |
| Microsoft.Extensions.Hosting.WindowsServices 8.0.1 (.NET) | Añadida (fase 2) | Paquete oficial para ejecutarse como servicio de Windows |
| xunit, Microsoft.NET.Test.Sdk, Microsoft.AspNetCore.Mvc.Testing (.NET, pruebas) | Añadidas (fase 2) | Pruebas unitarias y de integración con el servicio en memoria |
| JsonSchema.Net 7.2.3 (.NET, pruebas) | Añadida (fase 2) | Equivalente a ajv: valida respuestas del servicio con `contracts/schemas` |

npm usa versiones exactas y `package-lock.json`; NuGet usa `packages.lock.json` (`RestorePackagesWithLockFile`).

## ADR-010 · Cuadrícula por huecos — Aceptada (2026-10-18)

**Decisión.** Cada página define `grid {cols 3–8, rows 2–5}` y `slots[]` (id de botón o vacío). La distribución no cambia al deshabilitarse botones; "ocultar" queda como opción explícita y desaconsejada.

## ADR-011 · Emparejamiento iniciado en el PC — Aceptada (2026-10-18)

**Contexto.** El servicio corre en la sesión 0 y no puede mostrar ventanas; el código de emparejamiento debe verlo solo quien tiene acceso físico al PC.
**Decisión.** El código (6 cifras, 2 min, 5 intentos, un solo uso) se genera en el PC: `PegasusDeck.Service pair` (y, desde la fase 3, el menú de la bandeja del agente). `/api/pair` además limita intentos por IP. El PC guarda solo el hash del token.
**Consecuencias.** Emparejar exige estar delante del PC. En desarrollo, el panel del simulador (`/__sim`) genera el código.

## ADR-012 · `/api/health` sin datos y estado solo por WebSocket autenticado — Aceptada (2026-10-18)

**Decisión.** `/api/health` devuelve solo protocolo y versión (sirve para `PC_ONLINE` sin credenciales). La instantánea (sesión, OBS, apps) solo viaja por `/api/events` tras autenticarse; el token va en el primer mensaje, nunca en la URL.
**Consecuencias.** Un dispositivo no emparejado sabe que el PC está encendido, pero nada más.

## ADR-013 · Carpeta de datos protegida por ACL — Aceptada (2026-10-18)

**Contexto.** Revisión de seguridad (hallazgo alto): `%ProgramData%` deja crear archivos a cualquier usuario. Un usuario
sin privilegios podía generar códigos de emparejamiento, fabricar un `pairing.json` permanente o, en la fase 3, tocar la
lista blanca de apps.
**Decisión.** `DataDir` se crea con ACL protegida (SYSTEM y Administradores, sin herencia). El servicio y las órdenes
`pair`/`devices` se niegan a funcionar si el propietario no es SYSTEM/Administradores o si otro grupo puede escribir.
`pairing.json` con caducidad lejana o intentos fuera de rango se descarta. `UnprotectedDataDir` solo se admite en
desarrollo (HTTP local o certificado de pruebas).
**Consecuencias.** Emparejar exige una terminal de administrador (o, en la fase 3, el agente con un canal autorizado).
**Pendiente:** probar la creación protegida con elevación en el PC real.

## ADR-014 · Ficha común de acciones y orden de validación — Aceptada (2026-10-18)

**Decisión.** `contracts/actions.json` es la lista blanca de acciones del PC con casos válidos/inválidos. La comprueban la
PWA (Vitest), .NET (xUnit) y el simulador la lee directamente. Orden de comprobación de `/api/actions`, igual en todas
partes: auth → JSON (`BAD_JSON`) → estructura (`BAD_PARAMS`) → tipo registrado (`UNKNOWN_ACTION`) → parámetros
(`BAD_PARAMS`) → precondiciones (`NO_SESSION`/`OBS_NOT_READY`) → ejecución. También se comprueba que los 17 códigos
de error coinciden en TS, C# y el esquema.

## ADR-015 · Límites del contrato aplicados por quien envía — Aceptada (2026-10-18)

**Decisión.** El servicio pasa toda instantánea por `SnapshotLimits.Clamp` (lo que no cabe en el esquema se omite, no
se recorta). La PWA solo cuenta como latido un mensaje válido; 3 mensajes inválidos seguidos cierran el canal y se
muestra "Reconectando". Así nunca se presentan datos viejos como actuales.

---

## P-1 a P-4 · Aceptadas el 2026-10-18 e implementadas en la fase 3

- **P-1 (opción a):** lo que envía el PC es tolerante a campos nuevos (esquemas sin `additionalProperties: false` en
  respuestas/eventos; Zod en modo `strip`); las peticiones siguen estrictas. Que `/api/health` no filtre datos lo
  comprueba ahora una prueba del servicio (antes lo hacía un ejemplo inválido).
- **P-2:** `IAgentLink` + `ServiceEvents` + `OperationTracker`: el servicio genera el `operationId`, lo registra antes de
  enviar la orden, aplica tiempo máximo (el de la acción + 2 s) y da por fallidas las pendientes si el agente se cae.
  Los cambios de estado se envían a la PWA al momento (agrupados 100 ms).
- **P-3:** `pwa/src/app/actionEngine.ts` (`executeAction` → `completed | failed(code) | timeout | cancelled`, con
  `AbortSignal`) y `localActions.ts` (acciones del iPad registradas por tipo). `executor.ts` queda como adaptador de botones.
- **P-4:** `appsDetection` y `audio` en la instantánea; sin detección, el estado de las apps es *desconocido*.

## ADR-016 · Canal servicio ↔ agente por named pipe con verificación mutua — Aceptada (2026-10-18)

**Decisión (revisada tras las revisiones de seguridad e integraciones de la fase 3).** El servicio es el servidor de
`\\.\pipe\PegasusDeck.Agent` (ACL: SYSTEM, Administradores, usuarios interactivos y la cuenta del servicio; propietario
= cuenta del servicio; `FirstPipeInstance`). Una línea JSON por mensaje (máx. 64 KB), mensajes validados (uno no válido
corta la conexión).
- **El agente confía en el servidor si** está en la **sesión 0** (`GetNamedPipeServerSessionId`; un usuario sin
  privilegios no puede crear procesos ahí) **y** el **propietario del pipe** es una cuenta de servicio (SYSTEM,
  LocalService, NetworkService, `NT SERVICE\…`; no se puede falsificar sin ser administrador). La ruta del ejecutable
  solo se compara si se puede leer: desde un usuario normal Windows no deja consultar procesos de SYSTEM (medido:
  135 de 137 procesos de la sesión 0 de este PC devuelven acceso denegado).
- **El servicio acepta al agente si** su ejecutable es `Pegasus:TrustedAgentPath` (por defecto, junto al servicio) **y**
  está en la **sesión de la consola activa**; si cambia el usuario activo (cambio rápido de usuario), lo desconecta.
- Rechazos con espera creciente (0,25 → 5 s) y registro agrupado.
- `agent.json` no puede cambiar el pipe ni la ruta del servicio; "Emparejar iPad…" eleva siempre el servicio instalado
  junto al agente.
**Consecuencias.** Ni otro usuario ni un proceso sin privilegios pueden hacerse pasar por el servicio (y dictar la lista
blanca) ni por el agente. **Pendiente físico:** verificar con el servicio instalado como servicio de Windows.

## ADR-017 · OBS en el agente; contraseña con DPAPI — Aceptada (2026-10-18)

**Decisión.** El cliente obs-websocket 5 vive en el agente (sesión del usuario, `127.0.0.1:4455`). La contraseña se
guarda en `%LOCALAPPDATA%\PegasusDeck\agent.json` cifrada con DPAPI (CurrentUser) y nunca sale del PC. Solo el evento
de OBS confirma una acción; contraseña incorrecta o RPC incompatible no reintentan hasta que cambie la contraseña o se
reinicie OBS; tras cada reconexión se relee todo el estado.

## ADR-018 · Formato de `apps.json` — Aceptada (2026-10-18)

`[{ id, name, exePath, args[], readyLevel: "process" | "window", alwaysLaunch, allowUnprotectedPath?, detectExePaths? }]`.
Ruta absoluta a un `.exe` local (sin `..` ni rutas de red); prohibidos intérpretes y herramientas que ejecutan órdenes u
otros programas (cmd, PowerShell, wscript, mshta, rundll32, conhost, forfiles, msbuild, msiexec, schtasks, certutil,
wmic, node, python… lista en `AppsFile.cs`); el Explorador no admite argumentos; argumentos fijos sin caracteres de
control. Al lanzar, el agente se niega si un usuario sin privilegios puede modificar el ejecutable o su carpeta, salvo
`allowUnprotectedPath: true` puesto por el administrador. `detectExePaths`: otros ejecutables que cuentan como la app
abierta (lanzadores). Al primer arranque se crea con las apps conocidas
instaladas (OBS, Chrome, DaVinci Resolve, Explorador con `alwaysLaunch`). Lo edita un administrador.

## ADR-020 · Opciones de desarrollo solo en compilaciones Debug — Aceptada (2026-10-18)

`DevelopmentHttp`, `SelfSignedCertificate`, `UnprotectedDataDir`, `TrustedAgentPath="*"` (servicio) y `--insecure-dev`
(agente) no existen en Release: cualquier usuario podría lanzar el ejecutable instalado con ellas. Además
`StartupHookSupport=false` en todos los proyectos (no se puede inyectar código con `DOTNET_STARTUP_HOOKS`).

## ADR-021 · OBS: estado por `outputState` y fallos sin evento — Aceptada (2026-10-18)

En obs-websocket 5.2.3 `outputActive` del evento es false en PAUSED/STARTING/STOPPING: el estado de grabación se calcula
con `outputState`. Pausa con `PauseRecord`/`ResumeRecord`. OBS 29 no emite evento cuando no puede grabar (carpeta,
espacio, codificador) ni cuando no puede pausar (codificador compartido): a los 2 s sin evento se consulta
`GetRecordStatus` y se cierra la operación con un motivo concreto. Cambio de colección de escenas: sin peticiones
durante el cambio y recarga completa al terminar. Solo se habla con el proceso `obs64.exe` de esta sesión y de la ruta
esperada que escucha en el puerto (no se envía la prueba de la contraseña a otro).

## ADR-019 · Audio de Windows — Aceptada (2026-10-18)

Acciones `audio.master.toggleMute` y `audio.master.setVolume {percent}` (parámetro `percent`: entero 0–100), ejecutadas
por el agente con Core Audio sobre la salida predeterminada. Estado `audio {state, muted, volume}` en la instantánea.
Las pruebas automáticas usan un controlador simulado: nunca cambian el audio real del PC.

## ADR-022 · Página General: controles del PC fijos y accesos a apps configurables — Aceptada (2026-10-18)

**Contexto.** El usuario pidió que solo la pestaña General tenga 6 controles nativos (encender, silenciar micrófono,
volumen, silenciar PC, pausar, reanudar) y 6 accesos a apps configurables, sin cambiar nada de las demás pestañas.
Decisiones del usuario: pausar/reanudar = multimedia de Windows; apps con pulsación larga; volumen deslizable estilo
iOS (sustituye a subir/bajar) y la posición libre pasa a «Silenciar PC».

**Decisión.**
- **Panel v3** (`pwa/src/core/deck.ts`): campos opcionales `button.widget = 'slider'` (solo `audio.master.setVolume`) y
  `page.appSlots` (huecos cuya app se elige con pulsación larga; solo `app.open` o vacío). Migración v2 → v3 que
  **solo reescribe la página `general`**: fila 1 = `g-wake, g-mic, g-volume, g-mute-pc, g-pause, g-play`; fila 2 = las
  apps que ya tenía General (copias con id `g-app-N` y el mismo aspecto) y huecos libres; cuadrícula y huecos ≥ 12 se
  conservan. Las demás páginas y todos los botones existentes se devuelven tal cual; se guarda copia `deck.backup-v2`
  y se avisa de los botones que ya no aparecen en ninguna página (siguen guardados).
- **Contrato (aditivo, protocolo 2):** instantánea con `mic {state, muted}` y `media {state: playing|paused|stopped|none|unavailable}`
  opcionales (sin ellos la PWA los trata como no disponibles). Acciones nuevas sin parámetros en la lista blanca:
  `audio.mic.toggleMute`, `media.pause`, `media.play` (respuesta inmediata, 5 s).
- **Agente:** micrófono predeterminado con Core Audio (`eCapture`); multimedia con los controles multimedia de Windows
  (SMTC, `GlobalSystemMediaTransportControlsSessionManager`), pausa y reanudación **por separado** y confirmadas
  leyendo el estado (una app puede aceptar la orden y no cumplirla → `TIMEOUT`). El agente y sus pruebas pasan a
  `net8.0-windows10.0.19041.0` (Windows 10 2004 o posterior); servicio y núcleo no cambian.
- **Accesos configurables:** la hoja solo ofrece las apps de la lista blanca que envía el PC (`apps.json`); nunca rutas.

**Consecuencias.** «Comprobar estado», «Configuración» y «Bloquear Windows» dejan de estar en General (Configuración
y Comprobar siguen en la cabecera y en la hoja de estados; Bloquear queda guardado sin página hasta el editor de la
fase 4). Las E2E del servicio real aíslan su agente del OBS del usuario (`--config` propio y `--obs-port=1`).

## ADR-023 · Menú «Aplicaciones autorizadas…» en el PC — Aceptada (2026-10-18)

**Contexto.** Editar `apps.json` a mano como administrador era incómodo. Opciones presentadas: menú en el PC, iPad con
aprobación en el PC, o solo desde el iPad (desaconsejada: un iPad robado o un token filtrado podría autorizar cualquier
programa). El usuario eligió **el menú en el PC**.

**Decisión.**
- Icono de la bandeja → **Aplicaciones autorizadas…**: lista de las autorizadas (Quitar), programas del menú Inicio
  (búsqueda, «Autorizar seleccionado») y «Buscar .exe…». Antes de pedir permiso se comprueba con las mismas reglas
  (`AppsEditor.Prepare` → `AppsFile.Validate`); si el ejecutable está en una carpeta modificable por usuarios se avisa y
  solo se permite si se acepta expresamente (`allowUnprotectedPath`).
- Se guarda con la orden local `PegasusDeck.Service apps add --exe=… [--name=…] [--allow-unprotected]` /
  `apps remove <id>` lanzada **con UAC** (el agente no tiene permisos sobre la carpeta de datos). La orden vuelve a
  validar, no pierde entradas existentes y no sobrescribe un `apps.json` dañado. Ejecutable elevado: siempre el servicio
  instalado junto al agente; solo en Debug + `--insecure-dev`, el de `--trusted-service` y `--dev-data-dir`.
- **Recarga en caliente:** el servicio comprueba `apps.json` cada 2 s y envía la lista nueva al agente (mensaje `apps`
  del pipe, que el agente revalida). La PWA la ve en la siguiente instantánea; no hace falta reiniciar nada.
- Nada de esto existe en la API de la PWA: el iPad solo elige entre las apps ya autorizadas.

## ADR-024 · Apagar el PC y cerrar todas las ventanas de una app; confirmación en dos pasos — Aceptada (2026-10-18)

**Decisiones del usuario:** el botón «Encender PC» de General pasa a «Apagar PC» con el PC encendido; apagado normal con
20 s de aviso; triple toque en un acceso a app de General = cerrar todas sus ventanas, con confirmación; solo en General.

**Decisión.**
- **Excepción a la regla 6** («un botón no cambia de significado»), solo para este botón: `button.alternate` (panel v4)
  cambia nombre, icono y color (rojo, `power-off`) mientras `pc.online`, y su acción destructiva siempre pide
  confirmación. Migración v3 → v4 que solo añade la variante a `g-wake`.
- **Confirmación en dos pasos en el PC** para las acciones `destructive` de `contracts/actions.json`: la primera
  petición no ejecuta y responde `confirm_required` con un `confirmToken` aleatorio de un solo uso (15 s) ligado al
  dispositivo, al tipo y a los parámetros; la segunda lo presenta. Error nuevo `CONFIRMATION_INVALID` (409). La PWA
  pide confirmación en pantalla y envía el segundo paso automáticamente.
- **`system.shutdown`** lo ejecuta el servicio (funciona sin sesión): `InitiateShutdown` con 20 s de aviso, **sin
  forzar** (las apps con cambios pueden pedir guardar). La PWA pasa a «Apagándose» y espera hasta 60 s.
- **`app.closeAll {appId}`** lo ejecuta el agente: `WM_CLOSE` a las ventanas principales de la app en la sesión (como
  la X); nunca termina procesos. Del Explorador solo carpetas (`CabinetWClass`), nunca la barra de tareas ni el
  escritorio. El resultado dice cuántas se han cerrado de verdad. La instantánea incluye `apps[].windows` (opcional)
  para que la confirmación diga «¿Cerrar las 6 ventanas…?».
- Triple toque: tres toques en menos de 350 ms entre sí; un toque abre la app tras esa espera (dos cuentan como uno).

## ADR-025 · Instalador de Windows; el servicio se ejecuta como LocalSystem — Aceptada (2026-10-18)

**Contexto.** Fase 5 (parte de instalación), adelantada a petición del usuario: que servicio y agente arranquen solos.
Decisiones del usuario: .NET incluido (autocontenido) y cuenta **LocalSystem**.

**Justificación escrita de LocalSystem** (la regla de seguridad pide justificarla):
- El servicio verifica al agente leyendo la ruta de su proceso, que está en la sesión del usuario (ADR-016). Las
  cuentas de pocos privilegios (LocalService, NetworkService, cuentas virtuales) no pueden abrir procesos de otro
  usuario: habría que quitar esa verificación, y entonces cualquier programa que llegara al pipe podría hacerse pasar
  por el agente.
- La carpeta de datos protegida (ADR-013) solo admite SYSTEM y Administradores.
- ~~Apagar el PC necesita SeShutdownPrivilege.~~ Ya no aplica: el apagado se retiró (ADR-032); los dos motivos de arriba siguen.
- Exposición acotada: solo escucha en la IP de la LAN (perfil Privado, subred local), Host y Origin exactos, token por
  dispositivo, lista blanca de acciones con parámetros validados, sin ejecución de texto del cliente, destructivas en
  dos pasos (ADR-024). El agente, que toca el escritorio, sigue sin elevación.

**Decisión.** `pc/install/`:
- `build.ps1`: servicio y agente en **Release** (sin opciones de desarrollo, ADR-020), autocontenidos `win-x64`, con la
  PWA en `wwwroot`. No toca el sistema.
- `install.ps1`: copia a `C:\Program Files\PegasusDeck`, servicio `PegasusDeck` (automático, reinicio si falla),
  tarea «al iniciar sesión» para el grupo Usuarios sin elevación + acceso en el menú Inicio, carpeta de datos protegida
  con el certificado, `appsettings.json` con la IP de la LAN, regla de firewall si falta; arranca y comprueba
  `/api/health`; informa del estado de Wake-on-LAN.
- `update.ps1` (copia de la configuración en `backups\<fecha>` dentro de la carpeta protegida, vuelta atrás si la
  versión nueva no responde), `uninstall.ps1` (los datos solo con `-RemoveData` y confirmación), `status.ps1`
  (solo lectura).
- Todos idempotentes y con `-WhatIf`. Se ejecutan con `powershell -ExecutionPolicy Bypass -File …` (solo para ese
  proceso: no cambia la directiva del sistema).

## ADR-026 · Con una sesión iniciada, el apagado lo pide el agente — Aceptada (2026-10-18)

**Contexto.** Prueba física: el servicio (LocalSystem, sesión 0) recibió `ERROR_SHUTDOWN_USERS_LOGGED_ON` (1191). Desde
la sesión 0 la sesión del usuario cuenta como «otro usuario» y Windows solo permite apagar con `SHUTDOWN_FORCE_OTHERS`,
que cierra sus apps sin dejar guardar (descartado por el usuario en ADR-024). Además se corrigió la estructura
`TOKEN_PRIVILEGES` (el LUID estaba desalineado: «no tiene permiso»).

**Decisión.** El servicio sigue exigiendo los dos pasos; si hay agente con sesión, le reenvía `system.shutdown` y el
agente llama a `InitiateShutdown` desde la sesión del usuario (como «Apagar» del menú Inicio: aviso de 20 s, las apps
pueden pedir guardar). Sin sesión iniciada, lo hace el servicio. `WindowsPower` pasa a `PegasusDeck.Core.Native`.
Si hay otra sesión de Windows abierta (cambio rápido de usuario), se dice al usuario en vez de forzar.

## ADR-027 · Iconos de marca y de juegos en General; «Activar sonido / micrófono» — Aceptada (2026-10-19)

- **Dependencia nueva:** `simple-icons` 16.34.0 (versión exacta; logotipos de marcas en SVG, licencia CC0). Lucide no
  trae marcas. Solo se incluyen en la app los ~16 logos importados. Elegida por el usuario frente a extraer el icono
  del .exe en el PC.
- `core/appIcons.ts`: icono del acceso según la app: marca conocida (Steam, Discord, OBS, Epic, Battle.net, Rockstar,
  Spotify, Twitch…) → su logotipo; juego → mando. Juego = el agente la encuentra en una carpeta de juegos (Steam
  `steamapps\common`, Epic, Battle.net, Rockstar, Ubisoft, EA, Xbox, GOG, Riot; campo opcional `apps[].kind: "game"`
  del contrato) o su nombre coincide con juegos conocidos (Call of Duty, GTA…). Solo en los huecos de app de General.
- Panel v5: variantes de los controles fijos de General: «Silenciar PC» ↔ «Activar sonido» y «Silenciar micrófono» ↔
  «Activar micrófono» según el estado real. Migración v3/v4 → v5 que solo toca esos botones.

## ADR-028 · Pestaña DaVinci por atajos de teclado — Aceptada (2026-10-19)

**Contexto.** La prueba de viabilidad mostró que el DaVinci Resolve gratuito del usuario no acepta control externo y
que la API (Studio) no ofrece Lift, exposición, contraste, pivote, temperatura ni tinte. El usuario eligió atajos.

**Decisión.** Acción `davinci.command {command}` con una **lista cerrada** de 18 órdenes
(`contracts/resolve-commands.json`); las teclas viven solo en el PC (`PegasusDeck.Core/Resolve/ResolveCommands.cs`,
mapa por defecto de Resolve en Windows). El agente solo pulsa si Resolve está autorizado y abierto, lo ha podido traer
al frente y la ventana activa es suya justo antes (SendInput); nunca texto libre ni teclas arbitrarias. El resultado
dice «enviado», no «hecho»: Resolve no confirma nada. Pestaña nueva «DaVinci» detrás de OBS (panel v6); no cambia
ninguna otra. **Pendiente:** verificar cada atajo en el Resolve del usuario (Ctrl+Alt+K).

## ADR-029 · General: 18 casillas, arrastrar y soltar, acciones sin avisos, Pausar/Reanudar único — Aceptada (2026-10-19)

- `page.editable` (solo General): las 18 casillas se configuran con una pulsación larga y **se suelta sin mover**
  (apps autorizadas o controles del PC que no estén ya en la página; nunca duplicados) y se reordenan **manteniendo
  pulsado y arrastrando**; al soltar se intercambian las dos casillas (nada se pierde ni se duplica) y el orden se
  guarda en el iPad. Indicación sutil: el botón arrastrado se atenúa y el destino se marca con un borde. Una pulsación
  larga nunca ejecuta la acción.
- Acciones **sin avisos emergentes** (todas, incluidas las del sistema): bien → nada; mal → el propio botón tiembla y
  muestra el motivo unos segundos. Se mantienen las confirmaciones de acciones destructivas (regla de seguridad), el
  motivo al tocar un botón no disponible, los avisos de estado (banners) y los de emparejamiento y configuración.
- «Pausar» y «Reanudar» en un único botón con variante según el estado real de la multimedia; la casilla del antiguo
  «Reanudar» queda libre (el botón sigue guardado). Migración v5 → v6 solo de General más la pestaña DaVinci.

## ADR-030 · Apps de Microsoft Store / MSIX en la lista blanca — Aceptada (2026-10-19)

**Contexto.** El usuario quiere abrir apps de la Store (Claude, Forza…). No tienen un .exe que se pueda lanzar
directamente y su carpeta (`C:\Program Files\WindowsApps\<paquete>_<versión>…`) cambia con cada actualización.

**Decisión.** `apps.json` admite `aumid` (AppUserModelID, validado: `Familia_IdEditor!IdApp`, sin argumentos). El
agente:
- **Abre** la app con `IApplicationActivationManager.ActivateApplication(aumid)`: la API de Windows para apps
  empaquetadas, sin shell, sin Explorador y sin argumentos.
- **Detecta** si está abierta por cualquier proceso dentro de la carpeta **actual** del paquete (se busca por su familia
  con `PackageManager`, caché de 30 s): sigue funcionando tras actualizar y con juegos de Xbox que arrancan otro .exe.
- No exige la comprobación de permisos del ejecutable: Windows protege los paquetes firmados.
- «Aplicaciones autorizadas…» lista también las apps de la Store instaladas para el usuario (las del menú Inicio, sin
  marcos ni paquetes del sistema), marcadas «· Microsoft Store». Se añaden con la misma orden local y UAC
  (`apps add --exe=… --aumid=…`).

## ADR-031 · DaVinci por secciones con los atajos del usuario; escenas de OBS automáticas — Aceptada (2026-10-19)

**Hallazgo.** Los botones de página de DaVinci no funcionaban aunque las teclas eran correctas (Shift+4/6/8): el agente
enviaba solo el código virtual y, con teclado español, Qt (Resolve) interpreta mal Shift+número. Además el usuario
usa un preset de teclado propio («Keys samu») y algunas órdenes no coincidían con las guías (p. ej. «corrección del
clip anterior» es Shift+=).

**Decisión.**
- El agente lee el **preset de teclado activo del Resolve del usuario** (`config.user.xml` → `CustomKeyboardPreset`,
  `keyboard.preset.xml`, solo lectura) y usa su atajo para cada orden de la lista cerrada; si no se puede leer, usa
  el por defecto. Si la orden no tiene atajo en su Resolve, el botón lo dice (sin pulsar nada).
- Las teclas se envían con **código físico** (scan code) y según la **distribución de teclado** de la ventana de
  Resolve (los signos se traducen con `VkKeyScanEx`).
- Pestaña DaVinci con **secciones** (Páginas, Edición, Color, Entrega, Photo): filas 1–2 según la sección; en la fila 3
  la primera casilla elige la sección y el resto son controles fijos (marcador, clip anterior/siguiente, reproducir,
  guardar). La sección elegida se recuerda en el iPad. Photo: Like = «toma buena» (M) y Dislike = «rechazada» (X).
- **Límites honestos:** Resolve no tiene orden de teclado para la cola de render ni para iniciar el render (Entrega =
  página Entrega y exportar proyecto), y la página Photo no tiene atajo para abrirse en el preset del usuario.
- Pestaña OBS: la fila 1 se mantiene; desde la casilla 6, **botones de las escenas reales** de la colección activa
  (hasta 12), con la activa marcada y sincronizada cuando se cambia en OBS; sin OBS conectado, la primera casilla lo
  indica. Sin notificaciones. Panel v7: migración que solo cambia DaVinci y OBS.

## ADR-032 · Fuera encender y apagar el PC desde Pegasus Deck — Aceptada (2026-10-20)

**Contexto.** El encendido dependía de terceros (app Atajos de iOS + una app de Wake-on-LAN, o la página del router):
Safari no puede enviar UDP. El usuario no quiere depender de eso y pide quitar la opción de encender **y** de apagar.

**Decisión.**
- Se retiran `system.wake` y `system.shutdown` del catálogo de la PWA, de `contracts/actions.json`, del simulador,
  del servicio y del agente. El servicio responde `UNKNOWN_ACTION` aunque llegue con confirmación. Se borran
  `WindowsPower` (InitiateShutdown, SeShutdownPrivilege) y los módulos `wake.ts`/`wol.ts`.
- La máquina del PC queda en `UNKNOWN` / `PC_OFFLINE` / `PC_ONLINE`: solo observa si responde (sin «Arrancando»,
  «Apagándose» ni error de Wake-on-LAN). Configuración ya no tiene la sección de Wake-on-LAN, el asistente del Atajo
  ni el modo de energía; los valores guardados se ignoran.
- Panel **v8**: los botones con esas acciones se borran de cualquier configuración anterior (también importada) y
  **su casilla queda vacía**; nada se mueve (distribución estable). Un JSON v8 que las pida se rechaza entero.
- Sigue habiendo acciones destructivas con dos pasos (`app.closeAll`): el mecanismo de confirmación no cambia.
- En las apps abiertas ya no se dibuja el punto ni el borde verde; la escena activa de OBS sí se sigue marcando.

**Consecuencias.** Menos superficie: el servicio ya no puede apagar el equipo. LocalSystem sigue justificado por la
verificación del agente y la carpeta protegida (ADR-025). La parte de apagado de ADR-024 y ADR-026 queda sustituida;
el triple toque para cerrar ventanas (ADR-024) se mantiene. El instalador ya no informa de Wake-on-LAN.

## ADR-033 · Sin emparejar no se muestra el panel — Aceptada (2026-10-21)

**Contexto.** Sin emparejar, el panel enseñaba todos los botones bloqueados («fuera de servicio») y un aviso arriba.
El usuario prefiere no ver botones que no puede usar.

**Decisión.** Si el iPad no tiene token, o el PC lo ha rechazado (revocado o de otro PC), en lugar de las pestañas y
la cuadrícula se muestra un único aviso con icono: «Este iPad no está emparejado» (o «El PC ya no reconoce este
iPad»), cómo obtener el código en el PC y el botón **Emparejar**. La cabecera (estado y Configuración) se mantiene.
Se retira el aviso `pair-banner`. Con el iPad emparejado y el PC apagado, el panel sigue mostrándose con los
botones deshabilitados y su motivo (regla de distribución estable).

## ADR-034 · Cambiar la IP del PC desde la bandeja — Aceptada (2026-10-25)

**Contexto.** El router dio al PC otra IP (192.168.1.24 en lugar de .23). El servicio, configurado para escuchar
solo en la .23, se cerraba al arrancar (`SocketException 10049`) y el iPad no conectaba. El usuario quiere poder
indicar la IP de forma rápida desde Pegasus Deck en el PC.

**Decisión.**
- Menú de la bandeja **«Dirección IP del PC…»**: muestra la dirección a la que se conecta el iPad, si el PC aún la
  tiene, las IP actuales del PC (un clic para elegir) y un campo para escribirla.
- Solo se acepta una **IPv4 privada que tenga ahora este PC** (nunca 0.0.0.0 ni una pública): se valida en la
  ventana y otra vez en la orden elevada. Se sigue sin escuchar en todas las interfaces.
- El cambio lo hace `PegasusDeck.Service address set <ip>` con UAC (mismo patrón que emparejar y apps): actualiza
  `ListenAddresses`/`AllowedHosts` en la configuración instalada y en su copia protegida (el resto no cambia), emite
  un certificado para la IP con la **CA de mkcert del usuario** (la que ya confía el iPad; RSA 2048, serverAuth,
  820 días) si el actual no la incluye, y reinicia el servicio. Si no arranca, restaura configuración y certificado.
- El agente comprueba cada minuto si el PC sigue teniendo la IP configurada y, si no, avisa una vez con un globo
  que abre la ventana.

**Hallazgo en la prueba física.** mkcert se había ejecutado desde una terminal dentro de una app empaquetada
(MSIX): Windows guardó la CA en `%LOCALAPPDATA%\Packages\<app>\LocalCache\Local\mkcert`, que fuera de esa app no se
ve en `%LOCALAPPDATA%\mkcert`. La CA se busca en este orden: `CAROOT`, la carpeta normal y las carpetas virtuales de
los paquetes. La orden elevada devuelve el motivo exacto a la ventana (`--result-file`).

**Consecuencias.** La app del iPad está ligada a la dirección (origen): tras cambiar la IP hay que abrir la nueva en
Safari, añadirla a la pantalla de inicio y emparejar. Sin la CA de mkcert en el PC no se puede crear el certificado
(se avisa y no se cambia nada). Reservar la IP en el router sigue siendo lo recomendable para no tener que cambiarla.

