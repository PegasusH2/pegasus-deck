# Pegasus Deck

Panel tipo **Stream Deck** en un iPad que controla un PC con Windows, OBS y aplicaciones en la red local.
El iPad usa una PWA (sin App Store); el PC ejecuta un servicio de Windows y un agente en la bandeja.

- **General:** 18 casillas configurables (apps autorizadas, volumen deslizable, salida de audio, micrófono, silenciar, pausar/reanudar), arrastrar y soltar; triple toque cierra las ventanas de una app.
- **OBS:** grabación, streaming, micrófono y botones con las escenas reales (obs-websocket 5).
- **DaVinci Resolve:** secciones (Páginas, Edición, Color, Entrega, Photo) con los atajos de tu propio preset de teclado.
- **Seguridad:** solo red local (HTTPS con CA propia), emparejamiento con código de 6 dígitos, lista blanca de acciones y apps, confirmación en dos pasos para lo destructivo. Nada se expone a Internet.

## Estructura

| Ruta | Qué es |
|---|---|
| `pwa/` | PWA (React, TypeScript, Vite) y simulador del PC para desarrollo (`pwa/sim`). |
| `pc/` | .NET 8: servicio de Windows, agente de sesión, núcleo compartido, pruebas e instalador (`pc/install`). |
| `contracts/` | Contrato PWA ↔ PC (JSON Schema, ejemplos y lista blanca de acciones). |
| `docs/` | Arquitectura, decisiones (ADR) y estado del proyecto. |

## Probarlo sin PC real (simulador)

```bash
cd pwa
npm install
npm run dev
```

Abre http://localhost:5173 (panel del simulador en `/__sim`).

## Instalarlo en Windows

Guía paso a paso en [`docs/manuales`](docs/manuales/README.md). Resumen:

Requisitos: Windows 10 2004+ / 11, [.NET 8 SDK](https://dotnet.microsoft.com/download) y Node.js para compilar,
y [mkcert](https://github.com/FiloSottile/mkcert) para crear **tu propia** CA y certificado (cada PC el suyo).

1. `mkcert -install` y `mkcert -cert-file certs/pegasus-cert.pem -key-file certs/pegasus-key.pem <IP-del-PC>`.
   Ejecútalo desde una terminal normal de Windows (no desde dentro de otra app empaquetada).
2. `pc\install\build.ps1` y, como administrador, `pc\install\install.ps1`.
3. En el iPad: instala tu CA (`rootCA.pem` renombrado a `.crt`, nunca la clave), actívala en
   Ajustes → General → Información → Ajustes de confianza, abre `https://<IP>:8443` en Safari y añádela a la pantalla de inicio.
4. Empareja desde el icono de la bandeja → «Emparejar iPad…».

Si cambia la IP del PC: bandeja → «Dirección IP del PC…».

**Nunca compartas** `certs/` ni `rootCA-key.pem` de mkcert: con esa clave se podrían suplantar webs seguras en tus dispositivos.

## Pruebas

Desde `pwa/`: `npm run typecheck`, `npm test`, `npm run test:e2e`, `npm run test:e2e:service`. Desde `pc/`: `dotnet test`.

## Licencia

[MIT](LICENSE).

## Estado

Proyecto personal en desarrollo. Lo comprobado y lo pendiente de probar físicamente está en [`docs/PROGRESO.md`](docs/PROGRESO.md).
