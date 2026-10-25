# Pegasus Deck · PWA

Panel tipo Stream Deck para iPad. Implementado hasta ahora (contra el simulador del PC; el servicio .NET está pendiente):

- Estados por capas: PC (`src/core/pcMachine.ts`), enlace con el servicio (`linkMachine.ts`), estado global (`globalStatus.ts`).
- Condiciones AND/OR/NOT con motivos legibles y botones que se habilitan solos (`conditions.ts`, `deck.ts`, `actions.ts`).
- Contrato v2 con el PC (`../contracts`, `src/core/contract.ts`): `/api/health`, emparejamiento, acciones y WebSocket autenticado.
- Acciones "aceptadas" que solo se dan por hechas al confirmarse, con tiempo máximo y reintentos seguros (`src/app/executor.ts`, `operations.ts`).
- Sin encender ni apagar el PC desde el iPad (ADR-032): la PWA solo detecta si responde.
- Sistema de diseño con tokens CSS (`src/ui/tokens.css`) y kit de componentes (`src/ui/kit`).
- Persistencia en IndexedDB con migraciones; PWA instalable que abre sin el PC (service worker).
- **Simulador del PC** (`sim/server.mjs`) con el mismo contrato que el servicio .NET.

Arquitectura completa: [`../docs/ARQUITECTURA_AMPLIADA.md`](../docs/ARQUITECTURA_AMPLIADA.md).

## Desarrollo

```bash
npm install
```

```bash
npm run dev
```

Arranca el simulador (`http://127.0.0.1:8787`) y la PWA (`http://localhost:5173`).
El panel de control del simulador está en `http://localhost:5173/__sim`: permite encender/apagar el
"PC", **generar el código de emparejamiento**, iniciar sesión, abrir/cerrar OBS y otras apps, simular un paquete
mágico y provocar fallos (cortar el canal de eventos, OBS que no confirma).

Primer uso: con el "PC" encendido, la PWA muestra **Empareja este iPad** → pulsa *Emparejar* e introduce el código
que genera el panel del simulador.

## Pruebas

```bash
npm test
```

Unitarias y de contrato (los ejemplos de `../contracts` contra los esquemas JSON, los analizadores de la PWA y el simulador).

```bash
npm run test:e2e
```

Las E2E usan WebKit (el motor de Safari) a 1080×810 (iPad 9.ª gen en horizontal) y un simulador en puertos
propios (8790 HTTP, 40090 UDP). La primera vez: `npx playwright install webkit`.

## Probar en el iPad (red local)

Para instalar la PWA hace falta HTTPS de confianza:

1. Instala [mkcert](https://github.com/FiloSottile/mkcert) en el PC y ejecuta `mkcert -install`.
2. Genera el certificado para la IP del PC (ejemplo `192.168.1.100`):

   ```bash
   mkcert -cert-file certs/pegasus-cert.pem -key-file certs/pegasus-key.pem 192.168.1.100 localhost
   ```

3. Copia `rootCA.pem` (carpeta indicada por `mkcert -CAROOT`) al iPad, instálalo como perfil y actívalo en
   Ajustes → General → Información → Ajustes de confianza de certificados. **Nunca compartas `rootCA-key.pem`.**
4. `npm run dev` servirá por `https://192.168.1.100:5173`. Ábrelo en Safari → Compartir → «Añadir a pantalla de inicio».

Vite solo escucha en la red local; no abras puertos en el router.

## Pendiente

Estado detallado y criterios de aceptación en [`../docs/PROGRESO.md`](../docs/PROGRESO.md); fases en
[`../docs/ARQUITECTURA_AMPLIADA.md`](../docs/ARQUITECTURA_AMPLIADA.md) §14.6. Lo principal: servicio y agente de
Windows en .NET 8 (requiere instalar el SDK), autenticación por emparejamiento, OBS real, editor visual, perfiles,
macros, importar/exportar e instalador.
