# 4 · Solución de problemas

Empieza siempre por `powershell -ExecutionPolicy Bypass -File pc\install\status.ps1`: indica qué falla (servicio,
agente, IP, puerto, certificado o firewall).

| Síntoma | Causa probable | Qué hacer |
|---|---|---|
| El iPad no conecta y `status.ps1` dice «Configurado para X, pero ahora el PC tiene Y» | El router cambió la IP del PC | Bandeja → Dirección IP del PC… (manual 3) y reserva la IP en el router |
| El servicio está «Stopped» | No puede escuchar en la IP configurada o falta el certificado | Revisa la IP; en el Visor de eventos → Aplicación, busca `PegasusDeck.Service` |
| Safari avisa de que la conexión no es segura | El iPad no confía en la CA o el certificado no es para esa IP | Activa la CA en Ajustes de confianza (manual 2) o cambia la IP desde la bandeja |
| «Este iPad no está emparejado» o «El PC ya no reconoce este iPad» | No hay token o se revocó | Bandeja → Emparejar iPad… y escribe el código |
| «Sin sesión de Windows» | El agente de la bandeja no está abierto | Inicia sesión en Windows o abre «Pegasus Deck» desde el menú Inicio |
| Una app no aparece al configurar una casilla | No está autorizada | Bandeja → Aplicaciones autorizadas… |
| OBS dice «Contraseña incorrecta» | No coincide con la de OBS | Bandeja → Configurar OBS… |
| DaVinci dice «Sin atajo en Resolve» | Esa orden no tiene tecla en tu preset | Asígnala en Resolve con Ctrl+Alt+K |
| «No se puede usar la CA de mkcert» al cambiar la IP | La CA no está en este PC o se creó con otra cuenta | Ejecuta `mkcert -CAROOT`; usa la misma CA que tiene el iPad |
| `update.ps1` vuelve a la versión anterior | La versión nueva no arrancó | Mira el Visor de eventos; la copia de los datos está en `C:\ProgramData\PegasusDeck\backups` |

Los registros no guardan tokens, contraseñas ni códigos de emparejamiento.
