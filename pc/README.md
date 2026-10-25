# Pegasus Deck · Servicio de Windows y agente de sesión

Dos procesos (ADR-002):

- **`PegasusDeck.Service`** — servicio de Windows (sesión 0, antes de iniciar sesión).
- **`PegasusDeck.Agent`** — app de bandeja en la sesión del usuario: abre/enfoca apps autorizadas, bloquea la sesión,
  controla el audio y habla con OBS. Se comunica con el servicio por un named pipe con verificación mutua (ADR-016).

El servicio:

- Sirve la PWA y la API en el **mismo origen** por HTTPS en la red local (ADR-003).
- `GET /api/health` sin datos, `POST /api/pair`, `POST /api/actions` con token, `WS /api/events` autenticado (contrato: `../contracts`).
- Comprueba `Host` y `Origin` en cada petición, limita tamaños e intentos, guarda solo hashes de token.
- Sin agente conectado (nadie ha iniciado sesión), las acciones responden `409 NO_SESSION` y la PWA muestra "Sin sesión
  de Windows". Las acciones confirmadas por evento las sigue `OperationTracker` (tiempo máximo y fallo si el agente se cae).

## Estructura

| Proyecto | Contenido |
|---|---|
| `src/PegasusDeck.Core` | Contrato, validación de peticiones, lista blanca de acciones, tokens, emparejamiento, dispositivos |
| `src/PegasusDeck.Service` | Host ASP.NET Core / servicio de Windows, API, WebSocket, PWA estática, órdenes locales, servidor del pipe |
| `src/PegasusDeck.Agent` | Agente de sesión (bandeja): apps, bloqueo, audio (Core Audio), cliente obs-websocket 5 |
| `tests/PegasusDeck.Tests` | xUnit: núcleo, contrato, integración del servicio, servicio ↔ agente por pipe real, OBS simulado (`FakeObs`) |

## Compilar y probar

```bash
dotnet test
```

Prueba de integración con la PWA real servida por este servicio (HTTPS autofirmado, WebKit):

```bash
npm --prefix ../pwa run test:e2e:service
```

## Instalación (ADR-025)

Una vez: certificado para la IP del PC con mkcert (`certs/pegasus-cert.pem` y `pegasus-key.pem` en la raíz del repo),
red marcada como **Privada**. Después, en PowerShell **como administrador**:

```powershell
cd D:\Samuel\Documentos\streamdeck\pc\install
powershell -ExecutionPolicy Bypass -File .\build.ps1               # paquete Release (no toca el sistema)
powershell -ExecutionPolicy Bypass -File .\install.ps1 -WhatIf     # ver qué haría
powershell -ExecutionPolicy Bypass -File .\install.ps1             # instalar
powershell -ExecutionPolicy Bypass -File .\status.ps1              # estado
```

Actualizar: `build.ps1` y luego `update.ps1`. Desinstalar: `uninstall.ps1` (`-RemoveData` también borra los datos).

## Ejecutarlo a mano (desarrollo)

Todo en una terminal **como administrador**: la carpeta de datos se crea protegida (solo SYSTEM y Administradores,
ADR-013) y el servicio se niega a usar una carpeta que otros usuarios puedan modificar.

1. Compila la PWA: `npm --prefix ../pwa run build` (genera `pwa/dist`).
2. Certificado de la CA local (una vez), sustituyendo la IP por la de tu PC:

   ```bash
   mkcert -cert-file C:/ProgramData/PegasusDeck/cert.pem -key-file C:/ProgramData/PegasusDeck/key.pem 192.168.1.100
   ```

3. Configuración en `src/PegasusDeck.Service/appsettings.json` (o variables `Pegasus__…`):

   ```json
   "Pegasus": {
     "Port": 8443,
     "ListenAddresses": ["192.168.1.100"],
     "AllowedHosts": ["192.168.1.100"],
     "CertificatePath": "C:/ProgramData/PegasusDeck/cert.pem",
     "CertificateKeyPath": "C:/ProgramData/PegasusDeck/key.pem",
     "WebRoot": "D:/Samuel/Documentos/streamdeck/pwa/dist"
   }
   ```

   El servicio **se niega a arrancar** si falta el certificado, si no se indica la IP de la LAN, si se intenta escuchar
   en `0.0.0.0` o si la carpeta de datos no está protegida.

   **Protege la CA local:** tras generar el certificado, mueve `rootCA-key.pem` (carpeta de `mkcert -CAROOT`) fuera del PC
   (por ejemplo a un USB guardado). Con esa clave cualquiera podría falsificar webs para el iPad. Renovar el certificado
   exigirá volver a traerla. (Fase 5: CA limitada por *Name Constraints* a la IP del PC.)

4. Arranca: `dotnet run --project src/PegasusDeck.Service`.
5. Permite el puerto solo en la red privada y la subred local (PowerShell como administrador):

   ```powershell
   New-NetFirewallRule -DisplayName "Pegasus Deck" -Direction Inbound -Protocol TCP -LocalPort 8443 -Profile Private -RemoteAddress LocalSubnet -Action Allow
   ```

6. En el iPad: instala el certificado raíz de mkcert (`mkcert -CAROOT` → `rootCA.pem`) y confía en él; abre
   `https://192.168.1.100:8443`, añádelo a la pantalla de inicio y empareja.

## Agente de sesión

1. Compila: `dotnet build src/PegasusDeck.Agent` (o la solución entera).
2. Ejecuta `PegasusDeck.Agent.exe` en tu sesión (fase 5: se iniciará solo al iniciar sesión). Aparece un icono en la
   bandeja con el estado de la conexión y tres opciones: **Emparejar iPad…** (pide permiso de administrador y muestra el
   código), **Configurar OBS…** (puerto y contraseña del servidor WebSocket de OBS) y **Salir**.
3. Configuración del agente: `%LOCALAPPDATA%\PegasusDeck\agent.json` (`pipeName`, `trustedServicePath`, `obsPort`,
   contraseña de OBS cifrada con DPAPI). Por defecto confía en `PegasusDeck.Service.exe` de su misma carpeta.
4. En OBS: Herramientas → Ajustes del servidor WebSocket → activar, puerto 4455, contraseña (cópiala en el agente).

Para probarlo sin instalar (desarrollo), servicio y agente deben saber dónde está el otro:

```bash
dotnet run --project src/PegasusDeck.Service -- --Pegasus:TrustedAgentPath=D:/Samuel/Documentos/streamdeck/pc/src/PegasusDeck.Agent/bin/Debug/net8.0-windows10.0.19041.0/PegasusDeck.Agent.exe
```

```bash
src/PegasusDeck.Agent/bin/Debug/net8.0-windows10.0.19041.0/PegasusDeck.Agent.exe --insecure-dev --trusted-service=D:/Samuel/Documentos/streamdeck/pc/src/PegasusDeck.Service/bin/Debug/net8.0-windows/PegasusDeck.Service.exe
```

`--insecure-dev` (solo en compilaciones Debug) es necesario porque en desarrollo el servicio no corre en la sesión 0: el
agente solo comprueba la ruta. Instalado (Release), el agente exige sesión 0 y que el pipe pertenezca a la cuenta del
servicio (ADR-016), y el servicio exige que el agente esté en la sesión activa de la consola.

### Lista blanca de aplicaciones (`apps.json`, ADR-018)

Se crea en la carpeta de datos al primer arranque con OBS, Chrome, DaVinci Resolve y el Explorador si están instalados.
Formato y reglas en `docs/DECISIONES.md` (ADR-018). Cambios: icono de la bandeja → **Aplicaciones autorizadas…**
(ADR-023, pide permiso de administrador) o `PegasusDeck.Service apps add --exe=<ruta> | apps remove <id>` en una
terminal de administrador. El servicio aplica los cambios en unos segundos, sin reiniciar. En desarrollo, el agente
necesita `--dev-data-dir=<carpeta de datos>` para que el menú use la del servicio de pruebas.

## Órdenes locales (delante del PC, terminal de administrador)

| Orden | Hace |
|---|---|
| `dotnet run --project src/PegasusDeck.Service -- pair` | Genera un código de 6 cifras (2 min, un uso, 5 intentos; 3 intentos/min por IP) |
| `… -- devices list` | Lista los iPad emparejados |
| `… -- devices revoke <id>` | Revoca uno; su conexión se cierra en el siguiente latido |
| `… -- apps list` / `apps add --exe=<ruta> [--name=…] [--allow-unprotected]` / `apps remove <id>` | Lista blanca de apps (lo mismo que el menú de la bandeja) |

Los datos están en `Pegasus:DataDir` (por defecto `%ProgramData%\PegasusDeck`, protegida): `devices.json` (hashes),
`pairing.json` (hash con sal del código pendiente).

Opciones solo de desarrollo/pruebas: `DevelopmentHttp`, `SelfSignedCertificate`, `UnprotectedDataDir` (rechazada fuera
de esos dos modos), `HeartbeatSeconds`, `PairAttemptsPerMinute`.

## Protecciones

Host y Origin exactos · token de 256 bits (solo hash en disco, comparación en tiempo constante) · cuerpos ≤ 16 KB y
JSON con profundidad ≤ 8 · WebSocket: autenticación en ≤ 3 s, máx. 8 pendientes y 16 clientes, envíos con tiempo máximo
y en paralelo, un fallo del latido no detiene el servicio · máx. 64 conexiones · registros sin secretos y sin inundar
(un aviso por minuto) · nombres de dispositivo sin caracteres de control.

## Pendiente

- Pruebas físicas: OBS real, bloqueo de sesión, audio real, foco de ventanas, varias sesiones de Windows (ver `docs/PROGRESO.md`).
- Fase 5: instalación como servicio (`sc.exe`/script), permisos de `DataDir`, regla de firewall automática, actualización y desinstalación.
