---
name: pegasus-security
description: Requisitos y lista de revisión de seguridad de Pegasus Deck (emparejamiento y tokens, CORS/origen, validación, lista blanca de acciones, confirmación de acciones destructivas, red local, logs sin secretos). Usar al tocar la API, la autenticación, nuevas acciones, importación de configuración o el instalador.
---

# Seguridad

## Modelo

- Atacante considerado: otro dispositivo de la LAN, una web abierta en el iPad u otro equipo (CSRF / DNS rebinding), un JSON importado malicioso.
- Fuera de alcance: alguien con sesión de administrador en el PC.

## Autenticación

- Emparejamiento: el PC muestra un código de 6 dígitos (válido 2 min, 5 intentos). La PWA lo envía y recibe un **token de dispositivo** aleatorio (≥ 256 bits). El PC guarda solo su hash; se puede revocar.
- Todas las rutas salvo `/health` (mínima, sin datos sensibles) y `/pair` exigen `Authorization: Bearer`. WebSocket: token en el primer mensaje, no en la URL.
- Comparación de tokens en tiempo constante. Límite de intentos por IP.

## Origen y red

- Servicio escuchando solo en la IP de LAN; regla de firewall Windows: perfil *Privado*, *subred local*.
- Comprobar `Host` (IP o nombre esperado) contra DNS rebinding y `Origin` exacto de la PWA. CORS: solo ese origen.
- Sin UPnP, sin port-forwarding, sin túneles. Si alguien lo pide, explicar el riesgo y proponer VPN del router como opción del usuario.

## Acciones

- Solo `type` registrados; desconocido ⇒ 400. Parámetros validados con esquema en la PWA **y** en el PC (longitudes, enumerados, ids de lista blanca).
- Prohibido construir comandos con texto del cliente (`cmd`, `powershell`, `Process.Start` con cadena concatenada).
- Destructivas (`system.shutdown`, `system.restart`, cerrar apps): confirmación en la PWA **y** dos pasos en el servidor: `POST` devuelve `confirmToken` de un solo uso (15 s), segundo `POST` lo presenta.
- Principio de mínimo privilegio: el servicio no corre como LocalSystem salvo justificación escrita; el agente sin elevación.

## Datos

- PWA: solo token de dispositivo y configuración. Nada de contraseñas (OBS, router).
- Importación JSON: tamaño máximo (1 MB), validación completa con esquema, ids únicos, referencias existentes; si falla, no se aplica nada.
- Logs: enmascarar `Authorization`, tokens, contraseñas, códigos de emparejamiento. Rotación por tamaño.

## Lista de revisión rápida (antes de cerrar un cambio de API)

- [ ] ¿Nueva ruta autenticada?  ¿Valida `Origin`/`Host`?
- [ ] ¿Parámetros con esquema en ambos lados?
- [ ] ¿Puede algún parámetro llegar a un proceso, ruta de archivo o shell?
- [ ] ¿Acción destructiva con dos pasos?
- [ ] ¿Algún secreto en logs, URL o almacenamiento de la PWA?
Opcional: ejecutar la skill integrada `security-review` sobre el diff.
