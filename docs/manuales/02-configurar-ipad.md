# 2 · Configurar el iPad

Pegasus Deck es una PWA: no se instala desde la App Store, se abre en Safari y se añade a la pantalla de inicio.

## 1. Confiar en tu CA (una vez)

Para abrir la conexión segura, el iPad tiene que confiar en la CA de mkcert de tu PC.

1. En el PC, ejecuta `mkcert -CAROOT` y copia `rootCA.pem` con el nombre `PegasusDeck-CA.crt`. Es solo el certificado;
   **nunca** copies `rootCA-key.pem`.
2. Pásalo al iPad (AirDrop, correo o iCloud Drive) y ábrelo. Aparecerá «Perfil descargado».
3. Ajustes → **Perfil descargado** → Instalar.
4. Ajustes → General → Información → **Ajustes de confianza de certificados** → activa la CA de mkcert.

## 2. Añadir la app

1. En Safari abre `https://<IP-del-PC>:8443` (por ejemplo `https://192.168.1.23:8443`). Debe abrirse sin avisos de seguridad.
2. Compartir → **Añadir a pantalla de inicio**. Ábrela siempre desde ese icono.

Mientras no esté emparejado, el iPad muestra «Este iPad no está emparejado» y el botón **Emparejar**.

## 3. Emparejar

1. En el PC: icono de Pegasus Deck en la bandeja → **Emparejar iPad…**. Windows pide permiso y aparece un código de
   6 dígitos (válido 2 minutos, de un solo uso).
2. En el iPad: **Emparejar** → escribe el código.

El iPad guarda solo un token de dispositivo, nunca contraseñas. Para quitarle el acceso:

- en el iPad: Configuración → Olvidar;
- en el PC, como administrador: `PegasusDeck.Service devices list` y después `PegasusDeck.Service devices revoke <id>`.

## Si cambia la IP del PC

La app queda ligada a la dirección. Después de cambiar la IP (manual 3):

1. Abre la dirección nueva en Safari y vuelve a añadirla a la pantalla de inicio.
2. Borra el icono antiguo.
3. Vuelve a emparejar.

El perfil de la CA sigue sirviendo.
