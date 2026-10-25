---
name: pegasus-architecture
description: Reglas de arquitectura de Pegasus Deck (PWA, API local, servicio y agente de Windows, adaptadores). Usar al crear módulos, cambiar contratos PWA↔PC, añadir dependencias o tipos de acción, o mover código entre capas.
---

# Arquitectura de Pegasus Deck

Referencia completa: `docs/ARQUITECTURA_AMPLIADA.md`. Decisiones: `docs/DECISIONES.md`.

## Capas de la PWA (`pwa/src`) y dependencias permitidas

```
ui/  ──► app/  ──► core/
 └──────────────► core/
```

- `core/`: lógica pura y determinista (máquinas de estado, condiciones, catálogo, esquemas, WoL). **Prohibido**: DOM, `fetch`, `Date.now()` implícito (el tiempo entra como parámetro `at`), React, IndexedDB.
- `app/`: efectos (conexión, persistencia, ejecutor, macros). Única capa que habla con la red.
- `ui/`: componentes. **No** conocen tipos concretos de acción: consultan el catálogo (`getAction`, esquema de parámetros) y llaman al ejecutor.
- `core` nunca importa de `app` ni de `ui`. Comprueba con `grep -rn "from '\.\./app\|from '\.\./ui" pwa/src/core` → vacío.

## Lado PC (`pc/`, .NET 8)

- `PegasusDeck.Service`: servicio de Windows (sesión 0). API HTTPS, auth, `/health`, apagado/reinicio, proxy al agente.
- `PegasusDeck.Agent`: app de bandeja en la sesión del usuario. Todo lo que toca el escritorio: abrir/enfocar apps, `LockWorkStation`, audio, cliente OBS.
- `PegasusDeck.Core`: contratos, validación, registro de acciones compartidos por ambos.
- Servicio ↔ agente: named pipe con ACL al SID del usuario. La PWA solo habla con el servicio.

## Contratos

- El contrato PWA↔PC vive en `contracts/` (JSON Schema + ejemplos en `contracts/examples/`). Al cambiarlo:
  1. Actualiza el esquema y añade/ajusta un ejemplo.
  2. Actualiza el tipo TS (`pwa/src/core/system.ts` o el módulo de contrato) y el simulador (`pwa/sim/server.mjs`).
  3. Las pruebas de contrato de ambos lados validan los ejemplos.
- Campo `protocol` en cada respuesta. Cambio incompatible ⇒ subir versión; la PWA muestra "servicio incompatible" en vez de fallar en silencio.

## Añadir un tipo de acción

1. Definición en `pwa/src/core/actions.ts`: `type`, categoría, ejecutor, `requires`, esquema Zod de parámetros, `destructive`, `timeoutMs`, cómo se confirma el resultado (respuesta síncrona o evento).
2. Handler en el PC con la misma validación (o en el simulador mientras no exista el PC).
3. Prueba unitaria de disponibilidad + prueba E2E o de contrato.
4. El editor genera el formulario desde el esquema: no añadir JSX específico salvo un widget de parámetro reutilizable.

## Dependencias

Antes de añadir una: ¿lo resuelve la plataforma o 30 líneas propias? Si se añade, justificarla en `docs/DECISIONES.md`. Versiones exactas (sin `^`).

## Al terminar un cambio de arquitectura

Registrar la decisión (contexto, opciones, elección, consecuencias) en `docs/DECISIONES.md`.
