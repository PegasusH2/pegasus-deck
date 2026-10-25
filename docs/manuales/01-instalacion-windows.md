# 1 · Instalación en Windows

## Requisitos

- Windows 10 versión 2004 o posterior, o Windows 11, con una cuenta de administrador.
- Para compilar: [.NET 8 SDK](https://dotnet.microsoft.com/download) y [Node.js](https://nodejs.org) 20 o posterior.
- [mkcert](https://github.com/FiloSottile/mkcert) (`winget install FiloSottile.mkcert`).
- El PC y el iPad en la misma red, con la red de Windows marcada como **Privada**.

## 1. IP fija para el PC

El iPad se conecta a una IP concreta. Reserva una IP para el PC en el router (DHCP → reserva por MAC) o ponla fija en
Windows: Configuración → Red e Internet → Ethernet → Asignación de IP → Manual.

## 2. Tu propio certificado (una vez por PC)

Ejecuta estos comandos desde una **terminal normal de Windows** (PowerShell o Terminal), no desde la terminal integrada
de otra app: algunas apps empaquetadas guardan los archivos de mkcert en una carpeta privada que el resto de programas no ve.

```powershell
cd <carpeta del proyecto>
mkcert -install
mkdir certs
mkcert -cert-file certs\pegasus-cert.pem -key-file certs\pegasus-key.pem 192.168.1.23
```

Cambia `192.168.1.23` por la IP de tu PC. `mkcert -CAROOT` indica dónde está tu CA (`rootCA.pem` y `rootCA-key.pem`).

> **Nunca compartas** la carpeta `certs/` ni `rootCA-key.pem`: con esa clave se podrían suplantar webs seguras en los
> dispositivos que confían en tu CA. El `.gitignore` del proyecto ya las excluye.

## 3. Compilar e instalar

```powershell
powershell -ExecutionPolicy Bypass -File pc\install\build.ps1
```

Después, en PowerShell **como administrador**:

```powershell
powershell -ExecutionPolicy Bypass -File pc\install\install.ps1 -IpAddress 192.168.1.23
```

Con `-WhatIf` puedes ver qué haría sin cambiar nada. `install.ps1`:

- copia el programa a `C:\Program Files\PegasusDeck` (con .NET incluido);
- crea la carpeta de datos protegida `C:\ProgramData\PegasusDeck` (solo SYSTEM y Administradores) y copia en ella el certificado;
- instala el servicio **PegasusDeck** (inicio automático) y la tarea que abre el agente de la bandeja al iniciar sesión;
- crea la regla de firewall «Pegasus Deck» (perfil Privado, solo la subred local) y comprueba que el servicio responde.

## Actualizar

Tras cambiar el código, ejecuta `build.ps1` y después `update.ps1` como administrador. Guarda una copia de los datos en
`C:\ProgramData\PegasusDeck\backups\<fecha>` y vuelve a la versión anterior si la nueva no arranca. Si el PC ya no tiene
la IP configurada, instala igualmente y te pide cambiarla desde la bandeja (manual 3).

## Comprobar el estado

`powershell -ExecutionPolicy Bypass -File pc\install\status.ps1` no necesita administrador. Muestra el estado del
servicio, el agente, la IP, el puerto, el certificado y el firewall.

## Desinstalar

Ejecuta `uninstall.ps1` como administrador. Los datos (dispositivos emparejados, apps autorizadas, certificado) solo se
borran con `-RemoveData`, y antes pide confirmación.
