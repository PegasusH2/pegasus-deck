---
name: pegasus-testing
description: Estrategia y comandos de pruebas de Pegasus Deck (Vitest para lógica, Playwright WebKit con viewport de iPad contra el simulador, pruebas de contrato y pruebas físicas pendientes). Usar al escribir pruebas, al cambiar estados/condiciones/acciones y antes de declarar una fase terminada.
---

# Pruebas y control de calidad

## Niveles

| Nivel | Herramienta | Dónde | Qué cubre |
|---|---|---|---|
| Unitarias | Vitest | `pwa/tests/unit` | `core/`: máquinas de estado, condiciones, catálogo, esquemas, import/export, macros |
| Contrato | Vitest + JSON Schema | `pwa/tests/contract`, `pc/tests` | Ejemplos de `contracts/` válidos en ambos lados y en el simulador |
| E2E | Playwright WebKit, `iPad (gen 7) landscape` (1080×810 = iPad 9.ª gen) | `pwa/tests/e2e` | Flujos completos contra `pwa/sim` |
| E2E real | Playwright WebKit + servicio .NET + agente reales (`npm run test:e2e:service`) | `pwa/tests/e2e-service` | HTTPS, emparejamiento, pipe con verificación mutua, apps |
| .NET | xUnit | `pc/tests` | Handlers, validación, auth, detección |
| Físicas | Manual, checklist | `docs/PROGRESO.md` | WoL real, instalación en iPad, OBS real, instalador |

## Reglas

- Lógica de tiempo: pasar `at` explícito; nada de `sleep` en unitarias.
- E2E en serie (un simulador compartido); cada prueba empieza con `sim('reset', …)`.
- Controlar el simulador solo por `/__sim/control`; añadir operaciones allí si hacen falta (no tocar el estado interno).
- Para botones deshabilitados (`aria-disabled`) usar `click({ force: true })` cuando se pruebe la explicación del motivo.
- Casos que siempre deben existir al tocar un área: error, desconexión, acción repetida (doble toque), respuesta tardía (llega tras el timeout), estado desconocido.
- Una prueba física no realizada **no** se marca como superada: se deja "Pendiente" con el procedimiento.
- Nunca tocar el sistema real del usuario en pruebas automáticas: OBS → `FakeObs` (xUnit); audio → `FakeAudio`; apps →
  copia sin ventana del agente (`PegasusDeck.TestApp.exe`); nada de `LockWorkStation`. Limpiar los procesos de prueba.

## Antes de cerrar una fase

```bash
cd pwa && npm run typecheck && npm test && npm run test:e2e && npm run test:e2e:service
cd ../pc && dotnet test
```

Luego actualizar `docs/PROGRESO.md`: implementado, pruebas ejecutadas (número y resultado), pendientes y archivos tocados.
Si hay dudas de calidad, invocar el agente `pegasus-qa`.
