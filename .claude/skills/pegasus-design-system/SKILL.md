---
name: pegasus-design-system
description: Tokens y componentes base del sistema de diseño de Pegasus Deck (colores semánticos, tipografía, espaciado, radios, sombras, iconos, animación, cuadrícula). Usar al escribir o revisar cualquier estilo de la PWA.
---

# Sistema de diseño de Pegasus Deck

## Fuente única

- Tokens como **variables CSS** en `pwa/src/ui/tokens.css` (`:root`). Tailwind solo los referencia (`tailwind.config.js` → `colors: { surface: 'var(--pd-surface-1)' … }`).
- Prohibido: colores hex sueltos en componentes, `style={{}}` salvo valores dinámicos (color elegido por el usuario).
- Componentes base en `pwa/src/ui/kit/`: `Button`, `IconButton`, `Tile`, `StatusPill`, `Banner`, `Dialog`, `Field`, `Toggle`, `Select`, `Sheet`. Las pantallas los componen; no reimplementan.

## Tokens (valores iniciales)

| Grupo | Tokens |
|---|---|
| Fondo / superficies | `--pd-bg #050913`, `--pd-surface-1 #0d1528`, `--pd-surface-2 #131e38`, `--pd-surface-3 #1b2847`, `--pd-border rgba(255,255,255,.10)` |
| Texto | `--pd-text #f1f5f9`, `--pd-text-2 #94a3b8`, `--pd-text-3 #64748b` |
| Semánticos | `--pd-ok #22c55e` (activo/confirmado), `--pd-danger #ef4444` (parar/error), `--pd-warn #f59e0b` (esperando), `--pd-info #3b82f6` (navegación/acción), `--pd-accent #8b5cf6` (categoría OBS/audio) |
| Colores de botón | Paleta cerrada de 10 (`--pd-tile-green` … `--pd-tile-slate`) con contraste ≥ 4.5:1 contra texto blanco |
| Tipografía | Sistema (`-apple-system`). Escala: 12 / 14 / 16 / 18 / 22 / 28 px; nombre de botón 15–16 px semibold |
| Espaciado | Base 4 px: 4, 8, 12, 16, 20, 24, 32 |
| Radios | `--pd-radius-sm 8px`, `-md 12px`, `-lg 16px` (botones), `-xl 20px` (paneles) |
| Sombras | Una sola elevación sutil; **sin halos luminosos** ni brillos de color |
| Iconos | Lucide, trazo 1.75; 20 px en barras, 40–48 px en botones |
| Animación | 120 ms (pulsación), 200 ms (paneles), `ease-out`; respetar `prefers-reduced-motion` |

## Cuadrícula

- Columnas 3–8, filas 2–5, configurable por página. Por defecto 6×3 en horizontal.
- Huecos de 12–16 px; los botones son cuadrados o casi (relación 1:1 a 4:3) y se calculan para **caber sin scroll**.
- Posiciones fijas por hueco: un hueco vacío se ve vacío (en edición, con "+").

## Reglas

- Contraste mínimo 4.5:1 en texto; 3:1 en iconos y bordes de estado.
- Objetivo táctil mínimo 44×44 px.
- Fondos planos o con un único degradado muy sutil; nada de degradados decorativos en cada botón.
- El color comunica categoría o semántica, pero el estado se refuerza con icono/texto.
