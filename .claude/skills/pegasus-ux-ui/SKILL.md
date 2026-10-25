---
name: pegasus-ux-ui
description: Criterios UX/UI de Pegasus Deck para iPad 9.ª generación en horizontal (pantalla principal, cuadrícula, editor, configuración, estados vacíos/error/carga/reconexión/primer uso). Usar al diseñar o modificar cualquier pantalla o flujo.
---

# UX/UI de Pegasus Deck

Dispositivo objetivo: **iPad 9.ª gen, horizontal → viewport CSS 1080×810**, Safari / PWA a pantalla completa.
Comprobar también vertical (810×1080) sin romperse. Para inspiración puede cargarse la skill `ui-ux-pro-max`;
los tokens concretos están en `pegasus-design-system`.

## Pantalla principal (de arriba abajo)

1. Barra de estado compacta (≤ 64 px): marca, PC, Servicio, OBS, perfil activo, accesos a editor y ajustes.
2. Aviso contextual solo cuando hay algo que hacer (arrancando, error, sin sesión, reconectando). Nunca más de uno.
3. Pestañas de páginas.
4. Cuadrícula: ocupa el resto. Debe caber sin scroll con la configuración por defecto (6×3 en 1080×810).

## Estados de un botón (todos distinguibles sin depender solo del color)

| Estado | Señal visual |
|---|---|
| Disponible | Relleno del color semántico, icono y texto blancos |
| Deshabilitado | Superficie neutra, icono atenuado, candado, motivo corto en rojo suave |
| Activo (p. ej. grabando) | Indicador "●" y borde/anillo verde o rojo según semántica |
| Ejecutando | Spinner en lugar del icono; resto de pulsaciones ignoradas |
| Fallido | Sacudida breve + aviso con el motivo; vuelve a su estado |
| Desconocido / reconectando | Igual que deshabilitado con texto "Comprobando…" |

## Reglas de interacción

- Una acción sin confirmación se ejecuta con **un toque** (`click`), feedback visual inmediato (< 100 ms, `:active` + estado busy).
- Tocar un botón deshabilitado **explica** por qué (aviso), no hace nada más.
- Sin scroll accidental: la cuadrícula no desplaza; `touch-action: manipulation`; nada de gestos de arrastre en modo uso.
- Confirmación solo en acciones destructivas o configuradas: diálogo con verbo concreto ("Apagar", no "Aceptar").
- Avisos: abajo, máx. 3 visibles, errores 7 s, resto 4,5 s, siempre cerrables.
- Mover/editar botones solo en el **modo edición** (nunca en modo uso).

## Estados de pantalla obligatorios

Primer uso (asistente: PC, emparejar, WoL), cargando, sin conexión (PWA desde caché), reconectando,
servicio incompatible, página vacía (con botón "Añadir"), error de configuración importada.

## Editor

- Formularios generados desde el esquema de la acción: mostrar **solo** los campos de esa acción.
- Vista previa del botón en vivo, con su estado real actual.
- Condiciones: editor por bloques (Y / O / NO) con chips legibles, no JSON crudo.
- Guardar explícito; descartar cambios pide confirmación.

## Lista de comprobación antes de cerrar una pantalla

- [ ] Captura a 1080×810 y 810×1080 revisada.
- [ ] Objetivos táctiles ≥ 44×44 px.
- [ ] Todos los estados anteriores representados.
- [ ] Textos en español claros, sin jerga ("Servicio desconectado", no "WS closed").
