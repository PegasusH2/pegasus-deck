# 3 · Configuración y uso

Casi toda la configuración se hace en el PC, desde el icono de Pegasus Deck en la bandeja (junto al reloj). El punto de
color del icono indica si el agente está conectado al servicio.

## Aplicaciones autorizadas

Bandeja → **Aplicaciones autorizadas…** (o doble clic en el icono). Desde el iPad solo se pueden abrir o cerrar las apps de
esta lista; el iPad nunca envía rutas ni comandos.

- **Añadir:** elige un programa del menú Inicio o una app de Microsoft Store, o busca el `.exe`. Windows pide permiso de administrador.
- **Quitar:** selecciona la app y quítala.
- Los juegos de Steam, Epic, Battle.net, etc. se detectan solos y se muestran con el icono de un mando.

## Pestaña General (iPad)

- Tiene 18 casillas configurables. **Mantén pulsada** una casilla para elegir qué muestra: una app autorizada, un control
  del PC (micrófono, volumen, silenciar, pausar/reanudar) o nada.
- **Arrastra** un botón a otra casilla para moverlo. El orden se guarda en el iPad.
- **Triple toque** en una app: cierra todas sus ventanas, como con la X, después de pedir confirmación.
- El volumen se ajusta con un deslizador.

Un botón que no se puede usar se queda en su sitio, deshabilitado y con el motivo (por ejemplo «PC no disponible» o
«estado desconocido»). Pegasus Deck no enciende ni apaga el PC.

## OBS

1. En OBS: Herramientas → **Ajustes del servidor WebSocket** → activa el servidor (puerto 4455) y pon una contraseña.
2. En el PC: bandeja → **Configurar OBS…** → escribe el puerto y la contraseña. Se guarda cifrada para tu usuario de
   Windows (DPAPI) y nunca llega al iPad.

La pestaña OBS permite grabar, pausar, emitir y silenciar el micrófono. Desde la casilla 7 hay un botón por cada escena
real de OBS, con la activa marcada. Una acción solo se da por hecha cuando OBS la confirma.

## DaVinci Resolve

Funciona también con Resolve gratuito: los botones envían los atajos de teclado de **tu** preset activo de Resolve.

- Para cambiar de sección, pulsa la primera casilla de la fila inferior: Páginas, Edición, Color, Entrega o Photo.
- La fila inferior es fija: marcador, clip anterior, clip siguiente, reproducir/pausar y guardar.
- Si un botón dice «Sin atajo en Resolve», asígnale uno en Resolve (personalización del teclado, Ctrl+Alt+K) y vuelve a pulsarlo.
- Resolve debe estar abierto. El botón lo pone en primer plano antes de enviar la tecla.

## Dirección IP del PC

Bandeja → **Dirección IP del PC…** muestra a qué dirección se conecta el iPad y si el PC todavía la tiene.

1. Pulsa una de las IP actuales del PC o escríbela.
2. Pulsa **Aplicar** y acepta el permiso de administrador.

Pegasus Deck cambia la configuración, crea un certificado para la IP nueva con tu CA de mkcert y reinicia el servicio.
Si el servicio no arranca, lo deja todo como estaba. Solo admite IP de red local que tenga el PC en ese momento.

Si el router cambia la IP del PC, aparece un aviso junto al reloj; al pulsarlo se abre esta ventana. Después sigue los
pasos de «Si cambia la IP del PC» del manual 2.
