# Progreso de Pegasus Deck

Última actualización: 2026-10-25 (cambiar la IP desde la bandeja, ADR-034). Fases según `ARQUITECTURA_AMPLIADA.md` §14.6.

## Resumen

| Fase | Estado |
|---|---|
| 1 · Auditoría y diseño | **Aprobada** (ADR-005 a ADR-010 aceptadas el 2026-10-18) |
| 2 · Núcleo | **Terminada** (PWA + servicio .NET + revisión de seguridad y arquitectura). Pendiente físico: ACL con elevación e instalación en el iPad |
| 3 · Integración | **Terminada en software** (agente de sesión, pipe verificado, apps, bloqueo, audio, OBS, P-1…P-4, dos revisiones). **Pendiente físico:** ver lista abajo |
| 4 · Personalización | No iniciada (el modelo de datos v2 ya está preparado) |
| 5 · Pulido y validación | **Instalador hecho** (ADR-025), pendiente de ejecutarlo en el PC. Resto (revisión táctil, accesibilidad, rendimiento, seguridad, docs) sin empezar |

---

## Cambiar la IP del PC desde la bandeja (ADR-034, 2026-10-25)

| Comprobación | Resultado |
|---|---|
| Causa: el PC pasó a 192.168.1.24 y el servicio (en .23) se cerraba al arrancar (`SocketException 10049`) | Diagnosticado en el registro de eventos |
| Validación: solo IPv4 privada y de este PC; nada de 0.0.0.0, públicas ni texto raro | OK (.NET) |
| Solo cambia ListenAddresses/AllowedHosts; certificado nuevo firmado por la CA de mkcert y válido para la IP | OK (.NET, cadena verificada) |
| Sin CA o IP rechazada: no se toca nada; si el servicio no arranca, se restaura todo | OK (.NET) |
| Ventana «Dirección IP del PC…» | OK (render de la ventana) |
| Prueba física 1: «no existe …\mkcert\rootCA.pem» — la CA estaba en la carpeta virtual de la app desde la que se ejecutó mkcert | Corregido: también se busca ahí (.NET) |
| .NET **214/214** · servicio real **3/3** (la PWA no cambia) | OK |
| **Pendiente físico:** aplicar la IP con UAC, reinicio real del servicio y conectar el iPad a la dirección nueva | Pruébalo tú tras `update.ps1` |

---

## Pantalla «Este iPad no está emparejado» (ADR-033, 2026-10-21)

| Comprobación | Resultado |
|---|---|
| Sin token: ni pestañas ni botones; aviso con icono y botón Emparejar que abre el diálogo | OK (E2E + vista en navegador a 1080×810) |
| Token revocado en el PC: «El PC ya no reconoce este iPad» y Emparejar | OK (E2E simulador y servicio real) |
| Tras emparejar, el panel vuelve sin recargar | OK (E2E) |
| .NET **202/202** · unitarias **191/191** · E2E **26/26** · servicio real **3/3** | OK |
| **Pendiente físico:** verlo en el iPad (p. ej. tras «Olvidar» en Configuración) | Pruébalo tú tras `update.ps1` |

---

## Sin encender ni apagar el PC; apps sin marca verde (ADR-032, 2026-10-20)

| Comprobación | Resultado |
|---|---|
| Encender/Apagar fuera del catálogo, del contrato, del simulador, del servicio y del agente | OK (unitarias, contrato, .NET: `UNKNOWN_ACTION`) |
| Panel v8: el botón desaparece y su casilla queda vacía; el resto no se mueve; JSON v8 con esas acciones rechazado | OK (unitarias) |
| Configuración sin Wake-on-LAN; el PC encendido a mano se detecta solo | OK (E2E) |
| Apps abiertas sin punto ni borde verde; la escena activa de OBS sí se marca | OK (E2E) |
| .NET **202/202** · unitarias **191/191** · E2E **26/26** · servicio real **3/3** | OK |
| **Pendiente físico:** en el iPad, la casilla del antiguo botón queda libre y las apps abiertas no llevan marca | Pruébalo tú tras `update.ps1` |

---

## DaVinci por secciones y escenas de OBS (ADR-031, 2026-10-19)

| Comprobación | Resultado |
|---|---|
| Lectura del preset activo real de Resolve en este PC («Keys samu») | OK (.NET, solo lectura) |
| Envío con código físico y distribución de teclado | Implementado; **pendiente físico** (pulsar en tu Resolve) |
| Secciones DaVinci + fila fija; sección recordada | OK (unitarias + E2E) |
| Photo Like/Dislike | OK (E2E con simulador); **pendiente físico** en la página Photo |
| OBS: escenas reales, activa marcada, sincronización al cambiar en OBS, desconexión | OK (E2E con simulador); **pendiente físico** con tu OBS |
| .NET **202/202** · unitarias **208/208** · E2E **28/28** · servicio real **3/3** | OK |

---

## Pestaña DaVinci y General editable (ADR-028/029, 2026-10-19)

| Comprobación | Resultado |
|---|---|
| 18 casillas de General configurables; controles nunca duplicados; vaciar no pierde controles | OK (unitarias + E2E) |
| Arrastrar y soltar: intercambia, también a casillas libres; soltar no ejecuta; orden persistente tras recargar | OK (E2E) |
| Acciones sin avisos emergentes; fallo visible en el propio botón | OK (E2E) |
| Pausar/Reanudar en un botón según el estado real | OK (E2E) |
| Pestaña DaVinci: 18 órdenes, deshabilitadas sin Resolve, lista cerrada en PWA/servicio/agente/simulador | OK (unitarias, .NET, E2E) |
| .NET **191/191** · PWA unitarias **204/204** · E2E **27/27** · E2E servicio real **3/3** | OK |
| **Pendiente físico:** cada atajo en tu Resolve; arrastrar con el dedo en el iPad | Pruébalo tú |
| Apps de Microsoft Store (ADR-030): AUMID validado, detección por carpeta actual del paquete, listado en «Aplicaciones autorizadas…» | Hecho (.NET 199/199); **pendiente físico:** añadir Claude/Forza y abrirlas desde el iPad |

---

## Prueba de viabilidad: página de color de DaVinci Resolve (2026-10-19)

| Comprobación | Resultado |
|---|---|
| Edición instalada | DaVinci Resolve 21.0.0.28 **gratuita** (sin «Studio») |
| `fuscript.exe` (intérprete oficial) con Resolve abierto | Arranca, pero `Resolve()` devuelve nada: **sin control externo** |
| Python 3.11 + `fusionscript.dll` | Se cae al cargar la librería (antes de conectar) |
| Lo que ofrecería la API con Studio | Solo CDL por nodo (Slope≈Gain, Offset, Power≈Gamma, Saturación), activar/desactivar nodo, restablecer. **Sin** Lift, exposición, contraste, pivote, temperatura ni tinte; sin leer valores |
| Conclusión provisional | No viable con la edición gratuita. Pendiente: comprobar la preferencia «Scripting externo» (Local); si no existe, hace falta Studio |
| Herramienta | `tools/resolve_probe.py` (fase 1 solo lectura; `--write` solo en proyecto de prueba) |

---

## Instalador de Windows (fase 5, ADR-025, 2026-10-18)

| Comprobación | Resultado |
|---|---|
| `build.ps1`: Release autocontenido (servicio + agente + PWA, 210 MB) | OK |
| La compilación Release rechaza las opciones de desarrollo | OK |
| Sintaxis de los 6 scripts; `install -WhatIf`, `uninstall -WhatIf` y `status` sin cambiar nada | OK |
| `install.ps1` real: servicio LocalSystem, tarea del agente, carpeta protegida, arranque y `/api/health` | **Pendiente: lo ejecutas tú** |
| Verificación real servicio ↔ agente en Release (sesión 0 + propietario del pipe, ADR-016) | **Pendiente físico** (primera vez que se puede probar) |
| Reinicio del PC: servicio antes de iniciar sesión, agente al iniciar sesión, iPad reconecta sin emparejar | **Pendiente físico** |
| `update.ps1` y `uninstall.ps1` reales | Pendiente |
| Instalado en el PC real; HTTPS, servicio LocalSystem y agente en marcha (`status.ps1`) | **OK (2026-10-18)** |
| Apagar desde el iPad: falló por permiso (estructura corregida) y por «otros usuarios» (ahora lo pide el agente, ADR-026) | Corregido; **pendiente de volver a probar** |

---

## Apagar PC y triple toque para cerrar ventanas (ADR-024, 2026-10-18)

| Comprobación | Resultado |
|---|---|
| «Encender PC» ↔ «Apagar PC» según el estado; confirmación; cancelar no apaga; tras apagar vuelve a «Encender» | OK (unitarias + E2E) |
| Dos pasos en el servidor: sin token no apaga; token de otro dispositivo, caducado o repetido ⇒ `CONFIRMATION_INVALID` | OK (.NET con apagado simulado) |
| Cerrar ventanas: primer paso no cierra; segundo cierra una ventana real de pruebas con WM_CLOSE; sin ventanas ⇒ se dice | OK (.NET, pipe real) |
| Triple toque en General: confirma el número de ventanas, cancela, cierra; un toque abre; en Aplicaciones no hay triple toque | OK (E2E) |
| Migración v3 → v4: solo cambia `g-wake` | OK (unitarias) |
| Ventana de código de emparejamiento sin consola, icono de Pegasus en la bandeja con punto de estado, sin desenfoque en la parte superior del iPad | Compilado; **pendiente físico** |
| .NET **180/180** · PWA unitarias **197/197** · E2E **25/25** | OK |
| **Pendiente físico:** apagar el PC de verdad (no se ha probado: apagaría tu PC), triple toque en el iPad, E2E con el servicio real tras reiniciarlo | Pruébalo tú |

---

## Menú «Aplicaciones autorizadas…» en el PC (ADR-023, 2026-10-18)

Bandeja de Windows → icono de Pegasus Deck → **Aplicaciones autorizadas…**: ver, quitar y añadir (programas del menú
Inicio o «Buscar .exe…»). Pide permiso de administrador (UAC) y el iPad ve el cambio en unos segundos.

| Comprobación | Resultado |
|---|---|
| Reglas al añadir (sin intérpretes, rutas absolutas, sin duplicados, `apps.json` dañado intacto, entradas no válidas conservadas) | OK (.NET) |
| Órdenes `apps add/remove` y códigos de salida | OK (.NET) |
| Recarga en caliente servicio → agente → estado del iPad; app quitada ⇒ `APP_NOT_ALLOWED` | OK (.NET, pipe real) |
| Lectura del menú Inicio de este PC (solo lectura) | OK |
| .NET | **169/169** · PWA E2E **23/23** · E2E servicio real **3/3** |
| Arreglado de paso: al soltar rápido, el deslizador de volumen podía enviar el penúltimo valor | OK |
| **Pendiente físico:** abrir la ventana, autorizar y quitar una app con UAC, verla en el iPad | Pruébalo tú |

---

## Página General v3 (ADR-022, 2026-10-18)

Pedido: solo la pestaña General cambia; el resto debe conservar diseño, contenido, configuración y comportamiento.

### Implementado

- General: fila 1 con **Encender PC** (Wake-on-LAN), **Silenciar micrófono** (micrófono de Windows), **Volumen**
  (deslizador estilo iOS: arrastrar arriba/abajo, relleno con el nivel, flechas del teclado), **Silenciar PC**,
  **Pausar** y **Reanudar** (multimedia de Windows, con estado real); fila 2 con 6 huecos de app: **mantener pulsado**
  abre la lista de apps autorizadas del PC (o «Dejar el hueco vacío»). Fila 3 vacía, como antes.
- Estados: encender solo con el PC apagado; el resto solo con PC encendido + sesión; pausar solo si algo suena,
  reanudar solo si está en pausa (motivo «Nada reproduciéndose» / «Nada en pausa»); micrófono y silencio con indicador
  de activo; apps abiertas con indicador; sin micrófono/sin audio: deshabilitado con motivo.
- Migración v2 → v3 solo de General, copia `deck.backup-v2`, aviso informativo («Configuración actualizada»).
- Contrato aditivo (`mic`, `media`, 3 acciones), simulador alineado (botones «Sin micrófono» y «Multimedia» en /__sim),
  agente con Core Audio (micrófono) y SMTC (multimedia).

### Verificación (2026-10-18)

| Comprobación | Resultado |
|---|---|
| General: 6 controles + 6 huecos de app, cuadrícula 6×3 | OK (unitarias + E2E) |
| Disposición visual de General: misma posición y tamaño de cada hueco antes/después | OK (comparación en el navegador con tu configuración real) |
| Otras pestañas antes/después con tu configuración real (Prueba OBS, OBS, Aplicaciones, Audio): ids, orden, textos, clases CSS, etiquetas accesibles, posición y tamaño | **Idénticas** |
| Migración: demás páginas y todos los botones anteriores con el mismo contenido; sin General no cambia nada; idempotente | OK (unitarias + E2E con migración real en IndexedDB) |
| Diferencia detectada | El orden de las claves JSON guardadas se normaliza al validar (igual que en cada carga desde la v2); el contenido no cambia |
| `npm run typecheck` | OK |
| PWA unitarias + contrato | **184/184** |
| E2E WebKit contra el simulador (18 anteriores adaptadas a los nuevos ids + 5 de General) | **23/23** |
| E2E PWA ↔ servicio real ↔ agente real | **3/3** (el agente de pruebas ya no puede usar el OBS ni la contraseña reales) |
| .NET (incluye micrófono, multimedia confirmada y app que no cumple la pausa) | **161/161** |
| Lectura real en tu PC (sin ejecutar nada): micrófono «ready», multimedia «paused», volumen 100, OBS conectado | OK |

### Pendiente físico (pruébalo tú)

| Prueba | Cómo |
|---|---|
| Silenciar/activar el micrófono real | Botón «Silenciar micrófono» de General; comprobar el icono del micrófono de Windows |
| Volumen deslizable con el dedo en el iPad | Arrastrar «Volumen»; comprobar el volumen de Windows |
| Pausar/reanudar con Spotify o YouTube | Reproducir algo en el PC y usar «Pausar» / «Reanudar» |
| Pulsación larga en el iPad (sin menú de copiar de iOS) | Mantener pulsado un hueco de app de la fila 2 |

---

## Fase 3 · Integración

### Implementado

- **P-1…P-4 (aceptadas):** contrato tolerante en lo que envía el PC; eventos del agente y `OperationTracker` en el
  servicio; motor `executeAction` en la PWA (resultado tipado, cancelación); `appsDetection` y `audio` en la instantánea.
- **Agente de sesión** `pc/src/PegasusDeck.Agent` (bandeja): canal con el servicio por named pipe con verificación mutua
  (ADR-016), lista blanca recibida y revalidada, abrir/enfocar apps (ADR-018), `LockWorkStation`, audio Core Audio
  (ADR-019), cliente obs-websocket 5 (ADR-017, ADR-021), menú "Emparejar iPad…" (UAC) y "Configurar OBS…" (DPAPI).
- **Servicio:** `PipeAgentLink`, `OperationTracker`, envío inmediato de cambios, `apps.json` por defecto con las apps
  instaladas (OBS, Chrome, DaVinci Resolve, Explorador).
- **PWA:** página "Audio", motivo "no autorizada en el PC" distinto de "no instalada", textos del simulador iguales a los del agente.

### Revisiones de cierre (agentes `pegasus-security` y `pegasus-integrations`)

| Hallazgo | Gravedad | Estado |
|---|---|---|
| El agente no podía verificar al servicio real (un usuario no puede leer la ruta de procesos de SYSTEM): nunca se habría conectado | Crítica | **Corregido**: propietario del pipe + sesión 0 (ADR-016). Pendiente físico con el servicio instalado |
| Otro usuario podía hacerse pasar por el servicio con el binario legítimo y opciones de desarrollo | Alta | **Corregido**: sesión 0, propietario, opciones solo en Debug, sin startup hooks (ADR-020) |
| En pausa, OBS da `outputActive=false`: "reanudar" y "detener" quedaban bloqueados | Alta | **Corregido** (ADR-021) + prueba |
| OBS no avisa cuando no puede grabar/pausar: acababa en tiempo agotado genérico | Alta | **Corregido**: comprobación de estado y motivo concreto + pruebas |
| Agente de otra sesión podía ocupar el canal; cambio rápido de usuario | Media | **Corregido**: sesión de consola activa y vigilancia cada 2 s |
| "Emparejar" elevaba una ruta configurable por el usuario | Media | **Corregido**: siempre el servicio instalado junto al agente |
| Mismo tiempo máximo en agente y servicio | Media | **Corregido** (3 s / 8 s) |
| Avalancha de peticiones al cambiar de colección de escenas | Media | **Corregido** + prueba |
| Relanzar apps que se estaban abriendo; mensajes falsos con apps en la bandeja; ventanas de splash/herramientas | Media | **Corregido** |
| Coste de detección (una instantánea por app) | Media | **Corregido**: una instantánea por ciclo, solo esta sesión, caché de rutas |
| Bucle de detección/audio podía morir; RCW sin liberar | Media | **Corregido** |
| Simulador distinto del agente real | Media | **Corregido** + fallos simulables (no graba, no pausa, sin audio, OBS incompatible) |
| OBS falso de otro usuario en el puerto 4455 | Baja | **Corregido**: sesión, ruta y dueño del puerto |
| Estado del agente con nulos tumbaba el canal | Baja | **Corregido** + prueba |
| Ejecutables modificables por usuarios / herramientas que ejecutan órdenes | Baja | **Corregido**: comprobación de permisos al lanzar, lista ampliada, Explorador sin argumentos |
| Rechazos del pipe en bucle sin espera | Baja | **Corregido** |
| Waiters de OBS sin caducidad; saludo sin tiempo máximo; códigos 207/504; eventos perdidos al cargar | Baja | **Corregidos** |
| Hueco entre instancias del pipe (alguien puede ocuparlo y forzar reintentos) | Baja | Riesgo aceptado: no puede suplantar (propietario + sesión 0); solo retrasa la reconexión |
| `SetWinEventHook` / notificaciones de audio en vez de sondeo | Mejora | Pospuesto (fase 5) |

### Pruebas ejecutadas (2026-10-18, todas en verde)

| Suite | Resultado |
|---|---|
| `npm run typecheck` | OK |
| PWA unitarias + contrato (incl. motor de acciones, lista blanca común, simulador) | **160/160** |
| E2E WebKit 1080×810 contra el simulador | **18/18** |
| E2E WebKit: PWA ↔ **servicio real** ↔ **agente real** (pipe verificado, app abierta y confirmada, agente caído, revocación) | **3/3** |
| .NET: núcleo, contrato, servicio, pipe servicio↔agente, OBS simulado (protocolo 5.2.3), endurecimiento, nativas | **155/155** |

Ninguna prueba automática toca el sistema real: OBS simulado, audio simulado (solo una lectura del real), app de pruebas
sin ventana, sin bloqueo de sesión; los procesos de prueba se cierran al terminar.

### Pruebas físicas pendientes (no se pueden hacer aquí sin tocar tu PC)

| Prueba | Cómo |
|---|---|
| Servicio instalado como servicio de Windows + agente: verificación por propietario del pipe y sesión 0 | Fase 5 (instalador) o a mano con `sc.exe create` como administrador; el agente debe conectarse |
| OBS real (29.1.3): ~~conectar con contraseña, leer escenas y fuentes~~ **comprobado el 2026-10-18**; grabar/pausar/reanudar/detener, cambiar escena, silenciar fuente: **pendiente de tu confirmación** | Página «Prueba OBS» |
| Bloquear Windows desde el iPad | Botón "Bloquear Windows" |
| Audio real: silenciar y volumen | Página "Audio" |
| Traer apps al frente (depende de si se está usando el PC) | "Abrir Chrome" con Chrome ya abierto |
| Cambio rápido de usuario | Dos usuarios con sesión; cambiar y comprobar que el iPad muestra "Sin sesión" y luego reconecta |
| Wake-on-LAN real | Procedimiento más abajo |

---

## Fase 2 · Núcleo

### Implementado y verificado

**Contrato (ADR-007, ADR-012)** — `contracts/`
- 6 esquemas JSON (protocolo 2) y 40 ejemplos válidos/inválidos. `/api/health` sin datos; estado solo por WebSocket autenticado.

**Estados por capas (ADR-005)** — `pwa/src/core/`
- `pcMachine.ts`: + `CANCEL_WAIT`. `linkMachine.ts` (nuevo): `DISCONNECTED/CONNECTING/READY/AUTH_REQUIRED/INCOMPATIBLE`, espera 1→2→4→8→15 s, handshake máx. 5 s.
- `globalStatus.ts` (nuevo): los 9 estados globales (`PC_OFFLINE` … `DEGRADED`, `ERROR`) con motivos.
- `wake.ts` (nuevo): intento de encendido `requested → pending → confirmed | failed | cancelled`.
- `system.ts`: hechos por capa; con enlace sin conectar, todo lo del PC es *desconocido*. Hechos de escenas y fuentes de OBS (`obs.scene:<nombre>`, `obs.input:<nombre>`).

**Acciones y resultados (ADR-006)**
- `actions.ts`: parámetros declarados (de ellos sale el esquema y, en la fase 4, el formulario), requisitos que dependen de parámetros, `timeoutMs`, `completion`, `idempotent`, `destructive`.
- `app/executor.ts` + `app/operations.ts`: "aceptada" ≠ "completada"; espera del evento `operation` con tiempo máximo; aviso si la confirmación llega tarde; reintentos solo en acciones idempotentes y solo ante fallos transitorios; doble toque ignorado.

**Autenticación (ADR-011)**
- `app/pairing.ts`, `ui/PairingDialog.tsx`: emparejamiento con código de 6 cifras → token de dispositivo (única credencial en el iPad). Token en el primer mensaje del WebSocket. Token rechazado/revocado → aviso y re-emparejamiento.

**Modelo del panel v2 (ADR-010)** — `core/deck.ts`, `core/migrations.ts`
- Páginas con rejilla 3–8 × 2–5 y huecos fijos; paleta cerrada; `timeoutMs`, `retry`, `feedback`, `confirm {text, verb}`.
- Migración v1 → v2 con copia de seguridad previa; nunca descarta botones (crea páginas adicionales).

**Sistema de diseño (ADR-008)** — `ui/tokens.css`, `ui/kit/`
- Tokens CSS como fuente única; Tailwind solo los referencia. Kit: `Button`, `IconButton`, `StatusPill`, `Banner`, `Dialog`, `Sheet`, `Field`, `Section`, `Segmented`.
- Botones planos sin halos; estados disponible, deshabilitado (candado + motivo), activo (●), en curso (spinner + "En curso…"), fallido (sacudida + anillo rojo).
- Un único aviso contextual: arranque (con **Dejar de esperar**), error, emparejar, incompatible, reconectando, funcionamiento parcial.

**Otros**
- `idb` eliminada (IndexedDB nativo). `ajv` añadida solo para pruebas (ADR-009).
- Registro de diagnóstico en memoria con enmascarado de secretos (`app/log.ts`), visible en "Estados del sistema".
- Simulador al protocolo 2: emparejamiento, tokens con hash, comprobación de `Origin`, operaciones con confirmación diferida y controles de fallos.

### Pruebas de la parte PWA (antes del servicio .NET; ver resultados finales abajo)

| Suite | Resultado |
|---|---|
| `npm run typecheck` | OK |
| Unitarias (Vitest) | **64/64** |
| Contrato: ejemplos vs esquema JSON y vs Zod | **41/41** |
| Contrato: simulador (auth, origen, lista blanca, WS) | **7/7** |
| E2E WebKit 1080×810 (iPad 9.ª gen) | **16/16** |

### Servicio .NET (`pc/`)

- `PegasusDeck.Core`: contrato, análisis estricto de peticiones, lista blanca (`ActionCatalog`), tokens (256 bits, hash
  SHA-256, comparación en tiempo constante), `DeviceStore`, `PairingStore` (código de un uso, 2 min, 5 intentos),
  `RateLimiter`, `SnapshotLimits`, escritura atómica.
- `PegasusDeck.Service`: servicio de Windows/ASP.NET Core; HTTPS con certificado PEM de la CA local; `RequestGuard`
  (Host/Origin exactos, cabeceras de seguridad); `/api/health`, `/api/pair`, `/api/actions`, `WS /api/events`; PWA
  estática con caché correcta; órdenes `pair`, `devices list|revoke`. Sin agente (fase 3): acciones → `409 NO_SESSION`.
- SDK .NET 8.0.426 instalado con winget (autorizado). Paquetes con `packages.lock.json`.

### Revisión de cierre (agentes `pegasus-security` y `pegasus-architect`)

| Hallazgo | Gravedad | Estado |
|---|---|---|
| DataDir heredaba la ACL de `%ProgramData%` (emparejar sin privilegios, `pairing.json` permanente) | Alta | **Corregido** (ADR-013) y verificado sin elevación; falta verificar con elevación |
| Carrera en `EventsHub` podía detener el servicio | Media | **Corregido** + prueba de estrés de desconexiones |
| Clave de la CA de mkcert legible en el PC | Media | **Documentado** (moverla fuera del PC); CA con *Name Constraints* en la fase 5 |
| Confirmación antes que la respuesta HTTP se perdía | Alta (arq.) | **Corregido** + prueba |
| Instantánea fuera de límites → datos viejos como actuales | Alta (arq.) | **Corregido** (ADR-015) + prueba con instantánea hostil |
| Simulador con códigos distintos a .NET / lista blanca triplicada | Media | **Corregido** (ADR-014, `contracts/actions.json`) |
| Apps "no autorizadas" sin sesión (era "desconocido") | Media | **Corregido** |
| Quemar intentos de emparejamiento desde la LAN | Baja | Mitigado: 3/min por IP. Riesgo residual aceptado (no hay fuerza bruta: 5/10⁶) |
| WebSockets sin autenticar ilimitados / cliente lento bloquea a todos | Baja | **Corregido** (topes, 3 s, envíos en paralelo con tiempo máximo) |
| Registro inundable por rechazos Host/Origin | Baja | **Corregido** (1 aviso/min + recuento) |
| Nombres de dispositivo con caracteres de control/BiDi | Baja | **Corregido** en esquema, PWA, simulador y .NET |
| Limitador sin limpieza / IPv6 | Baja | **Corregido** (limpieza, agrupación /64) |
| `trim()` en parámetros, `in` en hechos, token vacío, ADR-009 incompleto | Baja | **Corregidos** |
| Canal de eventos agente → servicio (A1), API del ejecutor para macros (M3), `appsDetection` (M4), tolerancia a campos nuevos (A3c) | — | **Propuestas P-1…P-4** en `DECISIONES.md`, para decidir antes de la fase 3 |

### Pruebas ejecutadas (2026-10-18, todas en verde)

| Suite | Resultado |
|---|---|
| `npm run typecheck` | OK |
| PWA unitarias + contrato (ejemplos, lista blanca, simulador) | **144/144** |
| E2E WebKit 1080×810 contra el simulador | **16/16** |
| E2E WebKit contra el **servicio .NET real** con HTTPS (`test:e2e:service`) | **2/2** |
| .NET (`dotnet test`): núcleo, contrato con JsonSchema.Net, integración, endurecimiento, estrés WS | **102/102** |
| Manual: `pair` sin elevación sobre carpeta nueva y sobre carpeta del usuario | Se niega con mensaje claro y no escribe nada |

### Pendiente (no se puede comprobar aquí)

| Pendiente | Cómo probarlo |
|---|---|
| Crear la carpeta de datos protegida y emparejar **como administrador** | Terminal de administrador → `dotnet run --project pc/src/PegasusDeck.Service -- pair`; comprobar con `icacls C:\ProgramData\PegasusDeck` |
| PWA instalada en el iPad con la CA local y el servicio real | `pc/README.md` → "Ejecutarlo en tu PC" |
| El servicio envía la instantánea "ante cada cambio" (hoy solo en el latido de 5 s; no hay cambios sin agente) | Fase 3 (P-2) |

---|---|
| `pc/PegasusDeck.Core` + `PegasusDeck.Service` (HTTPS, `/health`, `/pair`, token con hash, WS, `Origin`/`Host`, servir la PWA) + xUnit + pruebas de contrato .NET | Falta el SDK de .NET 8: `winget install Microsoft.DotNet.SDK.8` |
| PWA servida por el servicio real en el PC | Depende de lo anterior |

---

## Criterios de aceptación

| # | Criterio | Estado | Evidencia / cómo probar |
|---|---|---|---|
| 1 | PWA instalable en el iPad con el procedimiento documentado | Pendiente (físico) | `pwa/README.md` → "Probar en el iPad". |
| 2 | Adaptada al iPad 9.ª gen en horizontal | Parcial | E2E a 1080×810: rejilla 6×3 sin scroll, botones ≥ 120 px. Falta probar en el dispositivo. |
| 3 | Botones con resultados reales | Parcial | Simulador con confirmación por evento; servicio real responde (NO_SESSION hasta la fase 3). |
| 4 | Configuración persiste al cerrar y abrir | Parcial | E2E: ajustes, página y emparejamiento tras recarga. Botones editados: fase 4. |
| 5 | Perfiles y páginas creables y modificables | No iniciado | Fase 4. |
| 6 | Condiciones habilitan/deshabilitan correctamente | Parcial | 16 unitarias de disponibilidad + E2E 1, 2–5, 6–7, sesión, OBS. |
| 7 | Perder OBS no inutiliza lo demás | Cumplido (simulador) | Unitaria "perder OBS…" + E2E 6–7. |
| 8 | Pérdida del servicio detectada y gestionada | Cumplido | E2E simulador (PC apagado, canal cortado, revocado) + E2E servicio real (revocación desde el PC). |
| 9 | Macros informan errores sin éxito prematuro | Parcial | Base lista (operaciones confirmadas por evento, E2E respuesta tardía). Macros: fase 4. |
| 10 | Acciones de Windows limitadas a las autorizadas | Cumplido (fase 2) | Lista blanca común (`contracts/actions.json`) probada en PWA, simulador y .NET; nada llega todavía a procesos. |
| 11 | Importación inválida rechazada | Parcial | Validación y migración probadas (unitarias). Interfaz de importación: fase 4. |
| 12 | Procedimiento realista de prueba de WoL | Retirado | Sin encendido desde Pegasus Deck (ADR-032). |
| 13 | Scripts de instalación comprobados | No iniciado | Fase 5. |
| 14 | Documentación coincide con lo implementado | En curso | Este documento, `contracts/README.md`, `pwa/README.md`. |

---

## Prueba física de Wake-on-LAN

Retirada (ADR-032): Pegasus Deck ya no enciende ni apaga el PC.

---

## Historial

### Fase 1 · Auditoría (2026-10-18)
Revisión secuencial con las listas de los agentes (aún no cargados en esa sesión). Hallazgos principales, todos
resueltos en la fase 2 salvo los marcados: falta de capa de enlace (ADR-005 ✅), catálogo sin esquemas (✅), contrato
sin fuente compartida (ADR-007 ✅), panel sin huecos (ADR-010 ✅), estilo con halos (ADR-008 ✅), sin cancelar la
espera (✅), E2E con viewport incorrecto (✅), `idb` (✅), API sin autenticación (✅ en simulador; **pendiente en .NET**).

Riesgos abiertos: R1 prompt maestro no disponible · R5 versión de iPadOS desconocida · R6 modelo de router desconocido.
(R4 resuelto: SDK .NET 8 instalado.)

### Archivos de la fase 2 (PWA)
- Nuevos: `contracts/**`, `pwa/src/core/{contract,linkMachine,globalStatus,wake,migrations,activity}.ts`,
  `pwa/src/app/{api,log,operations,pairing}.ts`, `pwa/src/ui/{tokens.css,kit/index.tsx,status.ts,StatusBanner.tsx,PairingDialog.tsx}`,
  `pwa/tests/unit/{fixtures,layers.test,deckModel.test}.ts`, `pwa/tests/contract/*`, `pwa/tests/e2e/core.spec.ts`.
- Modificados: `pwa/src/core/{pcMachine,system,conditions,actions,deck}.ts`, `pwa/src/app/{store,connection,executor,persistence}.ts`,
  `pwa/src/ui/{Header,DeckGrid,Overlays,StatusSheet,SettingsView}.tsx`, `pwa/src/{App.tsx,index.css}`, `pwa/sim/server.mjs`,
  `pwa/{package.json,tailwind.config.js,tsconfig.json,vitest.config.ts,playwright.config.ts}`, `docs/DECISIONES.md`.
- Eliminados: `pwa/src/ui/{statusText.ts,BootBanner.tsx}`, `pwa/tests/e2e/phase1.spec.ts`.

### Archivos de la fase 2 (servicio y cierre)
- Nuevos: `pc/**` (solución, `PegasusDeck.Core`, `PegasusDeck.Service`, `PegasusDeck.Tests`, `README.md`),
  `contracts/actions.json`, `contracts/examples/{pair.request,events.client}/invalid/*` (nombres y token),
  `pwa/playwright.service.config.ts`, `pwa/scripts/run-service.mjs`, `pwa/tests/e2e-service/service.spec.ts`,
  `pwa/tests/contract/actions.test.ts`, `pwa/tests/unit/robustness.test.ts`.
- Modificados: `pwa/src/app/{operations,connection}.ts`, `pwa/src/core/{actions,system,contract}.ts`, `pwa/sim/server.mjs`,
  `contracts/schemas/{pair,events}.schema.json`, `contracts/README.md`, `docs/{DECISIONES,ARQUITECTURA_AMPLIADA}.md`, `CLAUDE.md`.
