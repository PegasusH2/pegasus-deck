---
name: pegasus-release
description: Instalación, arranque, actualización, desinstalación y copias de seguridad de Pegasus Deck en Windows y de la PWA en el iPad. Usar al crear o modificar scripts de instalación, migraciones de configuración o documentación de instalación.
---

# Instalación y mantenimiento

## Scripts (PowerShell, en `pc/install/`)

| Script | Hace | Nunca |
|---|---|---|
| `install.ps1` | Pide consentimiento; copia binarios a `%ProgramFiles%\PegasusDeck`; crea servicio (inicio automático + recuperación); tarea programada "al iniciar sesión" para el agente; regla de firewall (Privado, subred local); genera certificados con la CA local | Abrir puertos en el router, desactivar el firewall, tocar otros servicios |
| `update.ps1` | Para servicio y agente, copia de seguridad de `%ProgramData%\PegasusDeck`, sustituye binarios, arranca, comprueba `/health` y si falla restaura | Borrar configuración o perfiles |
| `uninstall.ps1` | Quita servicio, tarea, regla de firewall y binarios; **pregunta** antes de borrar datos y certificados | Borrar datos sin confirmar |
| `status.ps1` | Estado de servicio, agente, puerto, firewall, certificado, WoL (`powercfg /devicequery wake_armed`) | Cambiar nada |

Todos idempotentes (ejecutarlos dos veces no rompe nada), con `-WhatIf` y salida legible. Probarlos en una VM o en el PC real y anotar resultados en `docs/PROGRESO.md`.

## Datos y copias

- Configuración del PC: `%ProgramData%\PegasusDeck\` (`apps.json`, `devices.json` con hashes de tokens, `settings.json`, `logs\`).
- Configuración de la PWA: IndexedDB del iPad + exportación JSON manual (botón "Exportar copia").
- Toda configuración lleva `schemaVersion`. Las migraciones son funciones puras `vN → vN+1` con prueba unitaria; nunca se descarta una página/perfil por no entenderla: se conserva y se marca como inválida.
- Antes de migrar, guardar copia `*.bak-<fecha>`.

## PWA en el iPad

1. Instalar el perfil de la CA local y confiar en él (ver `pwa/README.md`).
2. Abrir `https://<ip-del-pc>:<puerto>` en Safari → Compartir → "Añadir a pantalla de inicio".
3. Emparejar con el código que muestra el PC.
Actualizaciones de la PWA: el service worker se actualiza al abrirla con el PC encendido; los datos de IndexedDB se conservan.
