# Pegasus Deck

Panel tipo Stream Deck en un iPad (PWA) que controla un PC con Windows y OBS en la red local.
Idioma del proyecto: español (código en inglés; textos de interfaz, docs y commits en español).

## Mapa del repositorio

| Ruta | Qué es |
|---|---|
| `pwa/` | PWA (TypeScript estricto, React, Vite). `src/core` = lógica pura sin DOM; `src/app` = estado, conexión, ejecución; `src/ui` = componentes. |
| `pwa/sim/` | Simulador del PC: mismo contrato HTTP/WS que el servicio real. Solo desarrollo y pruebas. |
| `contracts/` | Contrato PWA ↔ servicio: JSON Schema, ejemplos y `actions.json` (lista blanca). Fuente única para TS y .NET. |
| `pc/` | .NET 8: `PegasusDeck.Core`, `PegasusDeck.Service` (servicio de Windows) y pruebas. El agente de sesión llega en la fase 3. |
| `docs/` | `ARQUITECTURA_AMPLIADA.md`, `DECISIONES.md` (ADR), `PROGRESO.md` (estado por fase). |

## Reglas que no se negocian

1. **Nada de ejecución arbitraria.** Solo acciones registradas en el catálogo, con parámetros validados en la PWA *y* en el PC. Las apps se identifican por id de una lista blanca guardada en el PC, nunca por ruta.
2. **Nada expuesto a Internet.** Sin redirección de puertos, UPnP ni túneles. El servicio escucha solo en la LAN.
3. **No afirmar lo que no se ha comprobado.** Enviar Wake-on-LAN no significa "PC encendido"; enviar una petición a OBS no significa "acción completada". Lo no probado físicamente se marca como *pendiente* en `docs/PROGRESO.md`.
4. **Sin secretos en la PWA ni en logs.** Solo un token de dispositivo revocable; la contraseña de OBS vive en el PC.
5. **Desconocido ≠ cerrado.** Si no se sabe un estado, el botón queda deshabilitado con motivo "estado desconocido".
6. **Distribución estable.** Un botón no disponible se muestra deshabilitado con motivo; no desaparece ni cambia de significado.

## Cuándo cargar cada skill

| Tarea | Skill |
|---|---|
| Nuevos módulos, contratos, dependencias, estructura | `pegasus-architecture` |
| Pantallas, flujos, estados visuales, editor | `pegasus-ux-ui` (+ `pegasus-design-system` al escribir estilos) |
| Colores, tipografía, espaciado, componentes base | `pegasus-design-system` |
| Cualquier cosa de OBS | `pegasus-obs` |
| Acciones de Windows, detección de apps, servicio/agente | `pegasus-windows` |
| Wake-on-LAN, detección del PC, red, HTTPS local | `pegasus-wol-network` |
| API, autenticación, validación, acciones destructivas | `pegasus-security` |
| Escribir o ejecutar pruebas, cerrar una fase | `pegasus-testing` |
| Instalador, actualizaciones, copias de seguridad | `pegasus-release` |

Agentes de revisión en `.claude/agents/` (solo cuando aporten: no en cada cambio): `pegasus-architect`,
`pegasus-ux-reviewer`, `pegasus-integrations`, `pegasus-security`, `pegasus-qa`. Revisan y proponen; el agente
principal integra.

## Comandos (desde `pwa/`)

```bash
npm run dev        # simulador + PWA (http://localhost:5173, panel del simulador en /__sim)
npm run typecheck
npm test           # unitarias (Vitest)
npm run test:e2e   # E2E con WebKit, viewport iPad 9.ª gen en horizontal
npm run test:e2e:service   # PWA real contra el servicio .NET real (HTTPS)
```

Desde `pc/`: `dotnet test`.

## Antes de dar algo por terminado

`typecheck` + `test` + `test:e2e` + `test:e2e:service` + `dotnet test` en verde, y `docs/PROGRESO.md` actualizado con lo probado, lo pendiente y cómo probarlo.
