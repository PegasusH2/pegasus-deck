# Pegasus Deck — Arquitectura ampliada (propuesta, pendiente de aprobación)

> Estado: §0–§11 aprobadas el 2026-10-18 (decisiones en `DECISIONES.md`). §12 sustituida por las fases de §14.6.
> §14 (revisión v2 tras la auditoría) **pendiente de aprobación**. Progreso real: `PROGRESO.md`.

## 0. Punto de partida

La carpeta del proyecto (`D:\Samuel\Documentos\streamdeck`) está **vacía**: no hay código ni
documentación de la arquitectura original. Las carpetas cercanas `PEGASUSONE` y
`pegasuslinked` pertenecen a otros productos (Coach/Tracker) y no las uso.

Por tanto, esta propuesta **supone** la arquitectura base que se deduce del enunciado y del
mockup:

| Pieza supuesta | Qué hace |
|---|---|
| PWA en el iPad | Panel de botones, páginas, perfiles, editor. |
| Servicio local en Windows ("Pegasus Deck") | API local en la LAN que ejecuta acciones registradas. |
| OBS WebSocket v5 | Control de escenas, grabación, streaming y fuentes. |
| Reglas de seguridad | Sin comandos arbitrarios, API protegida, solo LAN. |

Si la arquitectura original definía otra cosa (stack, puerto, forma de autenticación, cómo se
sirve la PWA), hay que pasármela y ajusto este documento antes de implementar.

---

## 1. Wake-on-LAN: quién envía el paquete cuando el PC está apagado

### 1.1 Limitaciones técnicas (confirmadas)

1. **Una PWA no puede enviar UDP.** Safari/WebKit solo expone HTTP(S), WebSocket y WebRTC.
   No hay API de sockets UDP (Direct Sockets es solo de Chrome para apps aisladas y no existe
   en WebKit). WebRTC no permite construir un paquete UDP arbitrario hacia un puerto concreto.
   Conclusión: **la PWA no puede enviar el paquete mágico, ni con trucos.**
2. **Con el PC apagado, el servicio de Windows no existe.** No puede hacer de relé.
3. **iOS exige permisos especiales para difundir (broadcast).** Desde iOS 14, una app necesita
   el permiso de "Red local" y Apple restringe el envío broadcast/multicast a apps con un
   permiso específico (*multicast networking entitlement*). Las apps de WoL de la App Store lo
   tienen; herramientas genéricas (Scriptable, a-Shell, etc.) no lo garantizan.
4. **Atajos (Shortcuts) de iOS no tiene una acción nativa de UDP/WoL.** Solo hace peticiones
   HTTP. Necesita apoyarse en otra app que aporte una acción de Atajos.

### 1.2 Alternativas gratuitas sin hardware extra

| Opción | Cómo funciona | Coste | Fiabilidad | Comentario |
|---|---|---|---|---|
| **A. Atajo de iOS + app WoL gratuita** *(recomendada)* | La PWA abre `shortcuts://run-shortcut?name=Pegasus%20Encender%20PC`. El atajo usa la acción "Wake" de una app WoL gratuita de la App Store, que envía el paquete mágico desde el propio iPad. | 0 € | Alta | El iPad es el único dispositivo que seguro está encendido. La PWA no puede saber si el paquete salió: solo sabe que **pidió** ejecutar el atajo. |
| B. WoL integrado en el router | Muchos routers (FRITZ!Box, ASUS, OpenWrt, algunos de operador) tienen botón "Wake on LAN" en su web. La PWA abre esa página. | 0 € | Alta | Depende del modelo. La PWA **no** guardará la contraseña del router. Sirve de plan B manual. |
| C. Dispositivo siempre encendido que ya tengas | NAS, Raspberry Pi, Home Assistant… expone un endpoint HTTP local que envía el paquete. | 0 € si ya existe | Alta | Solo si ya lo tienes; no se requiere. |

**Recomendación:** opción A como principal y B como alternativa documentada.

> Pendiente de verificar en tu iPad: qué app WoL gratuita instalar (comprobaré en la App Store
> que tenga acción para Atajos y que sea gratuita en el momento de configurar). No doy un
> nombre concreto hasta comprobarlo.

### 1.3 Flujo completo de "Encender PC"

```
iPad (PWA)                         iOS Atajos + app WoL            PC
   │ pulsa "Encender PC"
   │ estado: PC_OFFLINE → PC_BOOTING (subestado WAKE_REQUESTED)
   │ abre shortcuts://run-shortcut?name=...
   │ ───────────────────────────────▶ envía paquete mágico UDP ───▶ NIC despierta
   │ (el usuario vuelve a la PWA)                                     │ arranca Windows
   │ mensaje: "Solicitud de encendido enviada. Esperando al PC…"    │
   │ sondeo cada 3 s ◀──────────────────────────────────────────── /health del servicio
   │ responde → PC_ONLINE + SERVICE_READY
   │ sin respuesta en 180 s (configurable) → ERROR(WOL_TIMEOUT) + botón "Reintentar"
```

- El mensaje nunca dice "PC encendido" hasta que el PC **responde**.
- `x-success` no se usa para volver: abriría Safari, no la PWA instalada. El usuario vuelve
  con el gesto de cambio de app; la PWA retoma el sondeo al volver a primer plano
  (`visibilitychange`).
- Parámetros configurables: MAC, dirección de difusión (por defecto `255.255.255.255`, o la de
  subred, p. ej. `192.168.1.255`), puerto UDP (9 por defecto, 7 alternativo), nombre del atajo,
  tiempo máximo de arranque, intervalo de sondeo.
- La PWA genera un **asistente** que te dice exactamente qué atajo crear con esos parámetros
  (la MAC/broadcast/puerto se copian al portapapeles para pegarlos en la acción de la app WoL).

### 1.4 Requisitos del equipo para que Wake-on-LAN funcione

**Red**
- PC conectado por **cable Ethernet**. Despertar desde apagado por Wi‑Fi casi nunca está
  soportado.
- Reserva DHCP en el router para que el PC tenga IP fija (p. ej. `192.168.1.100`).
- iPad y PC en la misma subred. Sin redirección de puertos ni UPnP: nada se abre a Internet.

**BIOS/UEFI** (los nombres cambian según fabricante)
- Activar: *Wake on LAN*, *Power On by PCI-E / PCI*, *Resume by PME*.
- Desactivar: *ErP / EuP Ready* y *Deep Sleep* (cortan la alimentación de la tarjeta de red).
- Comprobación: con el PC apagado, la luz del puerto Ethernet debe seguir encendida.

**Tarjeta de red (Administrador de dispositivos → adaptador)**
- Pestaña *Administración de energía*: "Permitir que este dispositivo reactive el equipo" y
  "Permitir solo un paquete mágico".
- Pestaña *Opciones avanzadas*: *Wake on Magic Packet* = Activado; *Shutdown Wake-On-Lan* =
  Activado (Intel/Realtek); *Energy Efficient Ethernet* / *Green Ethernet* = Desactivado
  si da problemas.
- Usar el driver del fabricante (Intel/Realtek), no siempre el genérico de Windows.

**Windows**
- Desactivar **Inicio rápido** (Panel de control → Opciones de energía → "Elegir el
  comportamiento de los botones"). Con Inicio rápido, "Apagar" es en realidad una hibernación
  híbrida y algunos drivers no dejan la tarjeta preparada.
- Diagnóstico: `powercfg /a` (estados de suspensión disponibles) y
  `powercfg /devicequery wake_armed` (la tarjeta debe aparecer).

**Alimentación**
- El PC debe seguir enchufado. Si se corta la corriente (regleta apagada, apagón), WoL no
  funciona hasta que se encienda a mano (salvo opción BIOS "Restore on AC power loss").

### 1.5 Apagado, suspensión e hibernación

| Estado | ACPI | Compatibilidad WoL | Notas |
|---|---|---|---|
| Suspensión | S3 (o S0ix "Modern Standby") | La mejor | En S0ix (portátiles modernos) el comportamiento depende del fabricante. |
| Hibernación | S4 | Buena si el driver deja la tarjeta preparada | Requiere "Permitir que reactive el equipo". |
| Apagado | S5 | Depende de BIOS + *Shutdown Wake-On-Lan* | Es el caso más delicado; Inicio rápido desactivado. |
| Sin corriente | G3 | Imposible | — |

La PWA no puede distinguir en qué estado estaba el PC; el asistente pedirá elegir cuál usas y
mostrará el tiempo de arranque esperado (suspensión ≈ segundos; apagado ≈ 30–90 s).

---

## 2. Componentes en Windows

Windows aísla los servicios en la *sesión 0*: un servicio **no puede abrir programas
gráficos en el escritorio del usuario**. Por eso hay dos procesos:

| Componente | Tipo | Arranca | Responsabilidades |
|---|---|---|---|
| **Pegasus Service** | Servicio de Windows (inicio automático) | Antes del inicio de sesión | API HTTPS en la LAN, autenticación y emparejamiento, `/health`, estado de Windows, reinicio/apagado con confirmación, cola de órdenes hacia el agente, registro de errores. |
| **Pegasus Agent** | App de bandeja en la sesión del usuario | Al iniciar sesión (Programador de tareas, "al iniciar sesión", sin privilegios elevados) | Abrir aplicaciones autorizadas, enfocarlas, `LockWorkStation`, detección de aplicaciones, cliente de OBS WebSocket, audio. |

- Comunicación servicio ↔ agente: **named pipe** local con ACL limitada al SID del usuario.
- La PWA habla **solo con el servicio**. El servicio reenvía al agente lo que requiere sesión.
- OBS se controla **desde el agente** (OBS WebSocket en `127.0.0.1:4455`). La contraseña de
  OBS queda en el PC y nunca llega a la PWA.

### 2.1 Cómo se sirve la PWA (punto crítico)

Para que el botón "Encender PC" funcione con el PC apagado, la PWA debe cargar **sin el PC**.
Eso exige un *service worker*, y un service worker exige **HTTPS** (contexto seguro).

Propuesta (todo local, gratis):
1. El servicio sirve la PWA y la API por `https://<ip-del-pc>:8443`.
2. Se genera una **CA local** (mkcert) en el PC. Su certificado raíz se instala en el iPad
   como perfil y se marca como de confianza (Ajustes → General → Información → Ajustes de
   confianza de certificados). La clave privada de la CA no sale del PC.
3. El service worker cachea la aplicación. Con el PC apagado, la PWA abre desde la caché y
   muestra `PC_OFFLINE` con "Encender PC" disponible.
4. Las apps añadidas a la pantalla de inicio no sufren el borrado de datos a los 7 días de
   Safari, pero aun así habrá exportación JSON como copia de seguridad.

Alternativa descartada por ahora: alojar la PWA en un hosting HTTPS gratuito. Funcionaría, pero
añade una dependencia externa y sigue necesitando HTTPS de confianza en el servicio local.

### 2.2 Stack propuesto

| Parte | Propuesta | Motivo |
|---|---|---|
| PWA | TypeScript + React + Vite + Tailwind, Zustand para estado, IndexedDB (idb) | Coherente con tus otros proyectos Pegasus. |
| Servicio y agente | **.NET 8** (Worker Service + WinForms para la bandeja) | Servicio de Windows, named pipes, Win32 y detección de procesos nativos y sencillos. |
| Pruebas | Vitest (lógica), Playwright con WebKit (E2E), xUnit (PC), servidor simulado del PC | Se pueden probar los 13 casos sin un PC real. |

> Si el proyecto original ya fijaba otro stack (p. ej. Node.js), lo adapto.

---

## 3. Máquina de estados

Los estados pedidos no son excluyentes (OBS listo implica servicio listo, que implica PC
accesible). Por eso el modelo es **una máquina principal del PC + subestados independientes**,
y el estado global mostrado se deriva de ellos.

### 3.1 Máquina principal del PC

Estados: `UNKNOWN` (al abrir), `PC_OFFLINE`, `PC_BOOTING`, `PC_ONLINE`, `PC_SHUTTING_DOWN`,
`ERROR`.

| Desde | Evento | Hacia | Guarda |
|---|---|---|---|
| UNKNOWN | primer sondeo OK | PC_ONLINE | — |
| UNKNOWN | primer sondeo falla ×2 | PC_OFFLINE | — |
| PC_OFFLINE | `WAKE_REQUESTED` | PC_BOOTING | — |
| PC_OFFLINE | sondeo OK | PC_ONLINE | (encendido a mano) |
| PC_BOOTING | sondeo OK | PC_ONLINE | — |
| PC_BOOTING | supera tiempo máximo | ERROR(`WOL_TIMEOUT`) | — |
| PC_ONLINE | 3 sondeos fallidos seguidos | PC_OFFLINE | histéresis anti‑parpadeo |
| PC_ONLINE | `SHUTDOWN_CONFIRMED` | PC_SHUTTING_DOWN | — |
| PC_SHUTTING_DOWN | sondeo falla | PC_OFFLINE | — |
| ERROR | `RETRY` | PC_BOOTING / UNKNOWN | según el error |
| cualquiera | sondeo OK | PC_ONLINE | salvo PC_SHUTTING_DOWN durante 10 s |

Transiciones no listadas se ignoran y se registran (no se aplican cambios contradictorios).

### 3.2 Subestados independientes

| Señal | Cómo se detecta | Estados |
|---|---|---|
| Red / PC accesible | Respuesta del servicio. Sin servicio, **una PWA no puede hacer ping** (ICMP no existe en el navegador): solo hay una prueba heurística de tiempos de conexión marcada como "aproximada". | `online` / `offline` / `unknown` |
| Windows iniciado | El servicio responde `/health` (arranca antes del login). | `booted` |
| Sesión de usuario | El agente está conectado al servicio. | `session_ready` / `no_session` |
| Servicio Pegasus | `/health` + WebSocket de eventos abierto. | `SERVICE_READY` / `disconnected` |
| OBS | El agente tiene WebSocket identificado con OBS. | `OBS_READY` / `closed` / `auth_error` / `unknown` |
| App X | Ver §7. | `not_installed` / `installed` / `starting` / `running` / `APP_READY` / `unknown` |

**Invariantes:** si cae el servicio, OBS y las apps pasan a `unknown` (no a "cerrado": no lo
sabemos). Si el PC pasa a `PC_OFFLINE`, todo lo de debajo pasa a `unknown`.

### 3.3 Actualización en tiempo real

- Servicio → PWA por **WebSocket** (`wss://…/events`) con eventos (`obs.connected`,
  `app.state`, `recording.started`…). Al reconectar, la PWA pide una instantánea completa.
- Sondeo HTTP a `/health` solo mientras no hay WebSocket (cada 3 s en BOOTING, cada 10 s en
  OFFLINE, pausado con la PWA en segundo plano).
- Los botones se recalculan con cada cambio de estado; nunca hace falta recargar.

---

## 4. Modelo de configuración (JSON, versionado)

```jsonc
{
  "schemaVersion": 1,
  "pc": { "mac": "00:11:22:33:44:55", "broadcast": "255.255.255.255", "udpPort": 9,
          "host": "192.168.1.100", "port": 8443, "bootTimeoutSec": 180,
          "wakeMethod": "shortcut", "shortcutName": "Pegasus Encender PC" },
  "profiles": [
    { "id": "general", "name": "General", "priority": 0, "activation": null, "pages": ["p-general"] },
    { "id": "obs", "name": "OBS", "priority": 20,
      "activation": { "fact": "obs.ready" }, "pages": ["p-obs"] }
  ],
  "profileMode": { "mode": "auto", "pinned": null, "stableForSec": 3 },
  "pages": [{ "id": "p-general", "name": "General", "columns": 6, "buttons": ["b1", "b2"] }],
  "buttons": [{
    "id": "b2", "name": "Iniciar grabación", "icon": "record", "color": "#e5484d",
    "action": { "type": "obs.recording.start", "params": {} },
    "availability": { "all": [
      { "fact": "pc.online" }, { "fact": "service.ready" },
      { "fact": "obs.ready" }, { "not": { "fact": "obs.recording" } } ] },
    "whenUnavailable": "disable",          // disable | hide | message
    "confirm": null,                       // { "text": "...", "requireHold": false }
    "onError": "notify"                    // notify | retry(n) | ignore
  }]
}
```

- Se guarda en **IndexedDB** en el iPad. Exportar/importar JSON validado con esquema (Zod),
  con migraciones por `schemaVersion`.
- El JSON **no** contiene contraseñas, tokens ni rutas de ejecutables (las rutas viven en el PC,
  ver §8).

---

## 5. Catálogo de acciones (extensible)

> Actualizado: la definición vigente está en §14.3 y en `pwa/src/core/actions.ts` (ejecutores `pwa | wake | service`,
> `completion`, `idempotent`, parámetros declarados). La lista blanca del PC está en `contracts/actions.json` (ADR-014).

Cada acción se declara una vez en un registro:

```ts
interface ActionDefinition<P> {
  type: string;                 // "system.wol", "obs.scene.set", ...
  category: "system" | "apps" | "obs" | "macro" | "local";
  label: string; icon: string;
  params: ZodSchema<P>;         // genera el formulario del editor y valida
  requires: Condition;          // condición mínima implícita (se combina con la del botón)
  executor: "pwa" | "shortcut" | "service" | "agent";
  destructive?: boolean;        // fuerza confirmación
  run(params: P, ctx): Promise<ActionResult>;   // { ok, message, data? }
}
```

| Categoría | Acciones iniciales | Ejecutor |
|---|---|---|
| Sistema | WoL · Comprobar disponibilidad · Estado de Windows · Reiniciar servicio Pegasus · Bloquear Windows · Apagar/Reiniciar (confirmación obligatoria) | shortcut · pwa · service · service · agent · service |
| Aplicaciones | Abrir app autorizada · Comprobar si está abierta · Enfocar ventana · Ejecutar acción cuando esté lista | agent |
| OBS | Cambiar escena · Iniciar/detener grabación · Pausar grabación · Iniciar/detener streaming · Silenciar/activar fuente · Mostrar/ocultar fuente · Volumen | agent (OBS WS) |
| Macros | Secuencia · Pausa · Esperar app · Esperar OBS · Esperar condición · Cancelar | pwa (orquestador) |
| Local | Cambiar página/perfil · Ir a configuración | pwa |

Notas:
- **Reiniciar servicio**: el servicio termina de forma controlada con código de error y el
  Administrador de servicios de Windows lo vuelve a arrancar (opciones de recuperación). No
  hace falta dar permisos extra.
- **Bloquear Windows**: `LockWorkStation` desde el agente (API documentada de Windows).
- **Apagar/Reiniciar**: confirmación en la PWA **y** confirmación en dos pasos en el servidor
  (la primera llamada devuelve un código de un solo uso válido 15 s; la segunda lo presenta).
- En el PC hay un registro espejo: el servidor solo acepta `type`s conocidos, valida los
  parámetros con su propio esquema y rechaza todo lo demás.

### 5.1 Macros

Pasos: `action`, `delay(ms)`, `waitFor(condition, timeoutSec)`. Cada paso tiene `onError`
(`stop` | `continue` | `retry(n)`). La macro se puede cancelar; los pasos pendientes se
descartan y se muestra un resumen final (pasos OK / fallidos / cancelados).

Limitación: iOS congela la PWA en segundo plano. Las macros que encienden el PC y esperan
(p. ej. "WoL → esperar servicio → abrir OBS → escena Juego") se reanudan al volver a la PWA.
Las macros que solo usan el PC se pueden enviar enteras al servicio para que sigan aunque el
iPad se bloquee (fase 3).

---

## 6. Condiciones de disponibilidad

Árbol lógico con `all` (AND), `any` (OR), `not` y hojas `fact`:

| Hecho (`fact`) | Significado |
|---|---|
| `always` | Siempre disponible |
| `pc.online` · `pc.offline` · `pc.booting` | Estado del PC |
| `service.ready` · `session.ready` | Servicio / sesión de usuario |
| `obs.ready` · `obs.recording` · `obs.streaming` | OBS |
| `obs.scene` (`eq`) · `obs.source.exists` (`name`) | Escena actual / fuente existente |
| `app.<id>.state` (`in: [...]`) | Estado de una aplicación |
| `profile.active` (`eq`) | Perfil activo |

El evaluador devuelve `{ ok, reasons[] }`. Las razones se muestran en el botón (p. ej.
"OBS no está conectado") y en una hoja al mantenerlo pulsado. Si un hecho es `unknown`, el
botón queda deshabilitado con razón "Estado desconocido" (no se asume nada).

---

## 7. Detección de aplicaciones (en el agente)

Niveles de comprobación configurables por aplicación, de menos a más fiable:

| Nivel | Comprobación | Estado que permite declarar |
|---|---|---|
| `installed` | Existe el ejecutable registrado | `installed` |
| `process` | Proceso con esa **ruta** (no solo nombre) en la sesión del usuario | `running` |
| `window` | Ventana principal visible y que responde (`IsHungAppWindow` = false) | `APP_READY` si es el nivel configurado |
| `probe` | Sonda específica: OBS → handshake WebSocket; DaVinci Resolve → API de scripting local (si está habilitada); otras → puerto o fichero | `APP_READY` |

- Solo se declara `APP_READY` cuando se alcanza el nivel configurado.
- Eficiencia: instantánea de procesos cada 2 s filtrada solo por las apps autorizadas, más
  eventos de ventana (`SetWinEventHook`) para reaccionar sin sondear. Sin WMI con privilegios.
- Configuración por app: id, nombre, ruta del ejecutable, argumentos fijos, método/nivel de
  detección, perfil asociado y acciones permitidas.

---

## 8. Seguridad

- **Lista blanca de aplicaciones en el PC**: las rutas de ejecutables se configuran en el
  agente (ventana de configuración de la bandeja). La PWA solo ve `{id, nombre, icono}` y
  pide "abrir `davinci`", nunca una ruta. Así un botón no puede ejecutar nada arbitrario
  aunque se manipule el JSON.
- **Emparejamiento**: la primera vez, el PC muestra un código de 6 dígitos; la PWA lo
  introduce y recibe un **token de dispositivo** revocable (no es una contraseña tuya). Es la
  única credencial guardada en el iPad, y se puede revocar desde el PC.
- **Red**: el servicio escucha solo en la IP de la LAN; regla de firewall de Windows limitada al
  perfil *Privado* y a la *subred local*. Sin redirección de puertos, sin UPnP, sin túneles.
- CORS limitado al origen de la PWA; límite de peticiones; validación de todos los parámetros
  en el servidor.
- Logs rotativos sin tokens ni contraseñas (se enmascaran).
- El servicio corre con la cuenta de menor privilegio posible; solo apagar/reiniciar requiere
  el privilegio de apagado.

---

## 9. Perfiles contextuales

- Modos: **automático**, **manual** y **fijado** (pin).
- En automático, el perfil candidato es el de mayor **prioridad** cuya condición de activación
  se cumple. Solo se cambia si el candidato se mantiene estable `stableForSec` (3 s por
  defecto) → no hay parpadeo al abrir dos apps a la vez.
- Opción "pegajoso": mientras la condición del perfil actual se cumpla, no se cambia aunque
  aparezca otra app de prioridad igual.
- Un cambio manual fija el perfil hasta que el usuario vuelva a automático.
- Si ningún perfil aplica → `General`.

---

## 10. Arranque automático

| Qué | Cuándo está disponible | Cómo |
|---|---|---|
| Servicio | Antes de iniciar sesión | Servicio de Windows, inicio automático (con consentimiento en el instalador). |
| Agente | Tras iniciar sesión | Tarea programada "al iniciar sesión" del usuario, sin elevación. |
| OBS | Cuando lo abra el usuario o el agente | Opcional: "abrir OBS al iniciar sesión" en la configuración del agente. |
| Apps del usuario | Cuando se abren | Solo detección. |

La PWA muestra "Windows iniciado — esperando inicio de sesión" cuando el servicio responde
pero el agente no; los botones que requieren sesión explican esa razón.

---

## 11. Pruebas obligatorias

Todas se ejecutan contra un **simulador del PC** (servidor de pruebas que imita servicio,
agente y OBS y permite forzar estados), más pruebas unitarias de la lógica:

| # | Prueba | Tipo |
|---|---|---|
| 1 | PC apagado → botones dependientes deshabilitados con razón | E2E (WebKit) |
| 2 | WoL: se construye bien el paquete (6×FF + 16×MAC) y se abre el atajo correcto | Unitaria + E2E |
| 3 | Tras WoL se espera en PC_BOOTING y se respeta el tiempo máximo / reintento | Unitaria (máquina) + E2E |
| 4 | El servicio aparece → SERVICE_READY | E2E |
| 5 | Agente conectado → botones de Windows habilitados | E2E |
| 6 | OBS conecta → botones de OBS habilitados | E2E |
| 7 | OBS cierra → botones de OBS deshabilitados sin recargar | E2E |
| 8 | App abierta → cambio de perfil tras estabilización | Unitaria + E2E |
| 9 | Configuración sobrevive a recarga de la PWA | E2E |
| 10 | Evaluador de condiciones (AND/OR/NOT, unknown) | Unitaria |
| 11 | Un error se muestra sin bloquear la app | E2E |
| 12 | Cambio a perfil manual y fijado | E2E |
| 13 | Apagar/Reiniciar exigen confirmación (PWA y servidor) | E2E + xUnit |

Además: prueba manual guiada de WoL real (apagado, suspensión, hibernación) con checklist.

---

## 12. Plan de implementación incremental (sustituido)

Sustituido por las cinco fases de §14.6. Lo ya construido está en `PROGRESO.md`.

## 13. Decisiones iniciales (resueltas)

Aprobadas el 2026-10-18: Atajo + app WoL (ADR-001), .NET 8 (ADR-002), PWA servida por el PC con HTTPS local
(ADR-003), lista blanca en el PC (ADR-004). La "arquitectura original" (prompt maestro) sigue sin estar en el
repositorio; este documento es la referencia.

---

## 14. Revisión v2 tras la auditoría (propuesta)

### 14.1 Estados por capas

Tres máquinas independientes alimentadas por señales reales, más un estado global derivado:

| Capa | Estados | Señal | Tiempos |
|---|---|---|---|
| PC | `UNKNOWN`, `PC_OFFLINE`, `PC_BOOTING`, `PC_ONLINE`, `PC_SHUTTING_DOWN`, `ERROR` | `/health` por HTTPS | Sondeo §3.3; arranque máx. `bootTimeoutSec`; 3 fallos ⇒ offline |
| Enlace con el servicio | `DISCONNECTED`, `CONNECTING`, `READY`, `AUTH_REQUIRED`, `INCOMPATIBLE` | Autenticación + WebSocket de eventos | Handshake máx. 5 s; latido 5 s, muerto a los 12 s; reconexión 1→2→4…15 s |
| OBS (lo informa el agente) | `UNAVAILABLE`, `CONNECTING`, `READY`, `AUTH_ERROR`, `INCOMPATIBLE` | Proceso + obs-websocket identificado | Reconexión 1→15 s solo si el proceso existe |
| Sesión de Windows | `NO_SESSION`, `READY` | Agente conectado al servicio | — |
| Apps | `not_installed` … `ready` | Detección del agente (§7) | Instantánea cada 2 s |

**Estado global** (función pura, el primero que se cumple):

1. PC en `ERROR`, o enlace `AUTH_REQUIRED`/`INCOMPATIBLE` → **`ERROR`** (con motivo y acción: emparejar, actualizar…)
2. PC `UNKNOWN`/`PC_OFFLINE` → **`PC_OFFLINE`** ("Comprobando…" si `UNKNOWN`)
3. PC `PC_BOOTING` → **`PC_BOOTING`**
4. PC `PC_ONLINE` y enlace `DISCONNECTED` → **`PC_ONLINE`** (responde, aún sin canal de eventos)
5. Enlace `CONNECTING` → **`SERVICE_CONNECTING`**
6. Enlace `READY` y (sin sesión, OBS `AUTH_ERROR`, o detección de apps caída) → **`DEGRADED`** + lista de motivos
7. OBS `CONNECTING` → **`OBS_CONNECTING`**
8. OBS `READY` → **`OBS_READY`**
9. En otro caso → **`SERVICE_READY`**

Las condiciones de los botones siguen usando los **hechos** de cada capa (no el estado global), así perder OBS
solo deshabilita lo que depende de OBS.

**Limitación:** sin otro dispositivo encendido, Safari no puede distinguir "PC en la red" de "servicio en marcha".
`PC_ONLINE` = el PC responde por HTTPS.

### 14.2 Intento de encendido

`WakeAttempt { id, method, requestedAt, status: requested | pending | confirmed | failed | cancelled, attempts }`,
separado de la máquina del PC. Nuevo evento `CANCEL_WAIT`: `PC_BOOTING → PC_OFFLINE` con el intento en
`cancelled` y el texto "Has dejado de esperar. No sabemos si el PC se ha encendido." El sondeo normal sigue y lo
detectará si arranca.

### 14.3 Contrato de un botón

```jsonc
{
  "id": "rec-start", "name": "Iniciar grabación", "icon": "record", "color": "red",   // color = token de paleta
  "action": { "type": "obs.recording.start", "params": {} },
  "availability": { "not": { "fact": "obs.recording" } },     // + requisitos implícitos de la acción
  "whenUnavailable": "disable",                                // disable (por defecto) | hide
  "confirm": null,                                             // o { "text": "…", "verb": "Detener" }
  "timeoutMs": 10000,                                          // por defecto, el de la acción
  "retry": { "attempts": 0, "delayMs": 0 },                    // solo acciones idempotentes
  "feedback": { "success": "Grabando", "failure": null }       // null = mensaje del servicio
}
```

La acción declara además `completion: "response" | "event"` (ADR-006) y su esquema de parámetros, que el editor
usa para generar el formulario.

### 14.4 Páginas, perfiles y cuadrícula

- `Page { id, name, icon, grid {cols, rows}, slots: (buttonId | null)[] }` (ADR-010).
- `Profile { id, name, pageIds[], activation: Condition | null, priority }`; modo `auto | manual | pinned` (§9).
- Cabecera: `Perfil · Página` siempre visible.

### 14.5 Estructura de carpetas

```
streamdeck/
├─ CLAUDE.md                 reglas generales y cuándo usar cada skill
├─ .claude/skills/           9 skills pegasus-*
├─ .claude/agents/           5 agentes de revisión
├─ docs/                     ARQUITECTURA_AMPLIADA · DECISIONES · PROGRESO
├─ contracts/                JSON Schema + ejemplos (ADR-007)
├─ pwa/
│  ├─ src/core/              lógica pura: estados, condiciones, catálogo, esquemas, macros, migraciones
│  ├─ src/app/               conexión, auth, ejecutor, motor de macros, persistencia, perfiles, logs
│  ├─ src/ui/kit/            componentes base sobre tokens
│  ├─ src/ui/                pantallas: panel, editor, ajustes, estados, asistente de primer uso
│  ├─ sim/                   simulador del PC
│  └─ tests/{unit,contract,e2e}
└─ pc/
   ├─ PegasusDeck.Core/      contratos, validación, registro de acciones
   ├─ PegasusDeck.Service/   servicio Windows (API HTTPS, auth, health, apagado)
   ├─ PegasusDeck.Agent/     bandeja: apps, OBS, audio, bloqueo
   ├─ install/               install/update/uninstall/status .ps1
   └─ tests/                 xUnit
```

### 14.6 Fases (numeración nueva)

| Fase | Contenido | Cierre verificable |
|---|---|---|
| 1 · Auditoría y diseño | Este documento, skills, agentes, ADR | Aprobación del usuario |
| 2 · Núcleo | Estados v2 + `WakeAttempt` con cancelar; contrato en `contracts/`; resultado aceptada/completada con timeouts y reintentos; tokens + kit; .NET: Core + Service con `/health`, emparejamiento, token, WS, HTTPS, comprobación de `Origin`/`Host`; quitar `idb`; E2E a 1080×810 | Unitarias, contrato, E2E y xUnit en verde; PWA servida por el servicio real en el PC |
| 3 · Integración | Agente: OBS real, abrir/enfocar apps, bloqueo, audio, detección de apps; WoL de punta a punta | Pruebas con OBS real; prueba física de WoL documentada |
| 4 · Personalización | Editor visual (formularios por esquema, condiciones por bloques, vista previa), páginas/cuadrícula, perfiles automáticos, macros, importar/exportar | Criterios 4, 5, 6, 9, 11 |
| 5 · Pulido y validación | Revisión táctil en Safari del iPad, accesibilidad, rendimiento, seguridad, instalador y documentación | Los 14 criterios de aceptación revisados en `PROGRESO.md` |
