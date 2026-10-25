---
name: pegasus-wol-network
description: Wake-on-LAN, detección de disponibilidad del PC, HTTPS local y red en Pegasus Deck. Usar al tocar el encendido del PC, el sondeo/WebSocket, certificados, la máquina de estados del PC o la documentación de requisitos de red/BIOS.
---

# Red y Wake-on-LAN

## Hechos técnicos (no reabrir sin pruebas nuevas)

- Safari/WebKit **no tiene sockets UDP**: la PWA no puede enviar el paquete mágico. WebRTC tampoco sirve.
- Con el PC apagado no hay servicio: el envío debe hacerlo **otro dispositivo encendido**.
- iOS restringe broadcast/multicast a apps con el permiso de multicast de Apple; las apps WoL de la App Store lo tienen.
- Una PWA no puede hacer ping (ICMP). La única señal fiable desde el navegador es que el servicio responda por HTTPS.
- El service worker exige HTTPS de confianza → CA local (mkcert) instalada en el iPad.

## Mecanismo aprobado

1. **Principal:** la PWA abre `shortcuts://run-shortcut?name=<atajo>`; el Atajo usa la acción de una app WoL gratuita que envía el paquete desde el iPad.
2. **Alternativa:** página WoL del router (la PWA solo la abre; no guarda credenciales).
3. **Opcional:** dispositivo siempre encendido que el usuario ya tenga (NAS, Raspberry Pi, Home Assistant).
Nunca: abrir puertos a Internet, servicios de pago, un PC extra encendido.

## Separar envío y confirmación

Intento de encendido (`WakeAttempt`): `requested → pending → confirmed | failed | cancelled`.
- `requested`: la PWA lanzó el Atajo/router (no sabe si salió el paquete).
- `confirmed`: **solo** cuando el servicio responde.
- `failed`: se agotó `bootTimeoutSec`. Ofrecer "Reintentar" y la guía de requisitos.
- `cancelled`: el usuario deja de esperar; el PC vuelve a "Offline", sin afirmar nada sobre su estado real.

## Sondeo

| Estado | Intervalo |
|---|---|
| Desconocido | 0,5 s (máx. 2 fallos) |
| Offline | 10 s |
| Arrancando | 3 s |
| Online con WebSocket | 15 s + latido WS (muerto a los 12 s sin mensajes) |
| Online sin WebSocket | 2 s; 3 fallos seguidos ⇒ Offline |
Pausar en segundo plano; comprobar al volver (`visibilitychange`).

## Probar WoL de verdad (no se puede automatizar)

Seguir la lista de `docs/PROGRESO.md` → "Prueba física de Wake-on-LAN": desde suspensión, hibernación y apagado,
anotando tiempo hasta "confirmado". Con el simulador: app WoL en el iPad → IP del PC, puerto 40009, MAC `00:11:22:33:44:55`.

## Requisitos del PC (resumen; detalle en la pantalla de Configuración)

Cable Ethernet · BIOS: WoL/PCI-E wake on, ErP off · NIC: "Permitir reactivar", "Solo paquete mágico", "Shutdown Wake-On-Lan" · Windows: Inicio rápido desactivado · Reserva DHCP · PC enchufado.
