<#
.SYNOPSIS
  Instala Pegasus Deck en este PC (ejecutar en PowerShell como administrador).

.DESCRIPTION
  - Copia el paquete (build.ps1) a C:\Program Files\PegasusDeck.
  - Servicio de Windows "PegasusDeck" (LocalSystem, ADR-025): inicio automático y reinicio si falla.
  - Agente de sesión: tarea programada al iniciar sesión (sin elevación) y acceso en el menú Inicio.
  - Carpeta de datos protegida C:\ProgramData\PegasusDeck (ADR-013) con el certificado HTTPS.
  - Regla de firewall solo para la red Privada y la subred local (si no existe).
  Nunca abre puertos en el router, ni desactiva el firewall, ni toca otros servicios.
  Se puede ejecutar varias veces. Con -WhatIf muestra lo que haría sin cambiar nada.

.EXAMPLE
  .\install.ps1 -WhatIf
  .\install.ps1
  .\install.ps1 -IpAddress 192.168.1.23
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$IpAddress,
    [int]$Port = 8443,
    [string]$Package,
    [string]$CertFile,
    [string]$KeyFile
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
if (-not $Package) { $Package = Join-Path $PSScriptRoot 'out\PegasusDeck' }
if (-not $CertFile) { $CertFile = Join-Path $repo 'certs\pegasus-cert.pem' }
if (-not $KeyFile) { $KeyFile = Join-Path $repo 'certs\pegasus-key.pem' }
$whatIf = [bool]$WhatIfPreference
Assert-Admin $whatIf

Write-Step 'Comprobaciones'
if ([Environment]::OSVersion.Version.Build -lt 19041) { throw 'Hace falta Windows 10 versión 2004 o posterior.' }
foreach ($exe in 'PegasusDeck.Service.exe', 'PegasusDeck.Agent.exe') {
    if (-not (Test-Path (Join-Path $Package $exe))) { throw "Falta $exe en $Package. Ejecuta antes .\build.ps1." }
}
Write-Ok "Paquete: $Package"
if (-not $IpAddress) { $IpAddress = Get-LanAddress }
if (-not $IpAddress) { throw 'No se ha encontrado la IP de la red local. Indícala con -IpAddress.' }
Write-Ok "IP del PC en la red local: $IpAddress (pide a tu router que la reserve para este PC: si cambia, el iPad no lo encontrará)"
$netCategory = (Get-NetConnectionProfile | Where-Object { (Get-NetIPAddress -InterfaceIndex $_.InterfaceIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue).IPAddress -contains $IpAddress }).NetworkCategory
if ($netCategory -ne 'Private') { Write-Warn "La red de $IpAddress es '$netCategory': el iPad solo podrá conectar si la marcas como Privada." }
foreach ($f in $CertFile, $KeyFile) { if (-not (Test-Path $f)) { throw "No existe $f (certificado HTTPS; ver docs: mkcert)." } }
$cert = Read-PemCertificate $CertFile
if (-not (Test-CertificateFor $cert $IpAddress)) { throw "El certificado $CertFile no es para $IpAddress. Genera uno nuevo con mkcert para esa IP." }
if ($cert.NotAfter -lt (Get-Date).AddDays(30)) { Write-Warn "El certificado caduca el $($cert.NotAfter.ToString('d')): renuévalo pronto." }
Write-Ok "Certificado para $IpAddress, válido hasta el $($cert.NotAfter.ToString('d'))"

Write-Step 'Parar lo que esté en marcha'
if ($PSCmdlet.ShouldProcess('servicio y agentes de Pegasus Deck (también los de desarrollo)', 'Parar')) {
    if (Get-Service $ServiceName -ErrorAction SilentlyContinue) { Stop-Service $ServiceName -Force -ErrorAction SilentlyContinue }
    Stop-PegasusAgents
    Get-Process PegasusDeck.Service -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    Write-Ok 'Parados'
}

Write-Step "Programa en $InstallDir"
if ($PSCmdlet.ShouldProcess($InstallDir, 'Copiar el programa')) {
    New-Item -ItemType Directory -Force $InstallDir | Out-Null
    & robocopy.exe $Package $InstallDir /MIR /NFL /NDL /NJH /NJS /NP /R:2 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "La copia ha fallado (robocopy $LASTEXITCODE)." }
    Write-Ok 'Copiado'
}

Write-Step "Datos en $DataDir (solo SYSTEM y Administradores)"
$certDir = Join-Path $DataDir 'certs'
if ($PSCmdlet.ShouldProcess($DataDir, 'Crear o reparar la carpeta protegida y copiar el certificado')) {
    Set-ProtectedAcl $DataDir
    New-Item -ItemType Directory -Force $certDir | Out-Null
    Copy-Item $CertFile (Join-Path $certDir 'pegasus-cert.pem') -Force
    Copy-Item $KeyFile (Join-Path $certDir 'pegasus-key.pem') -Force
    & icacls.exe "$DataDir\*" /reset /T /C /Q 2>$null | Out-Null
    Write-Ok 'Carpeta protegida y certificado copiado'
}

Write-Step 'Configuración'
$settings = [ordered]@{
    Logging = [ordered]@{ LogLevel = [ordered]@{ Default = 'Information'; 'Microsoft.AspNetCore' = 'Warning' } }
    Pegasus = [ordered]@{
        Port = $Port
        ListenAddresses = @($IpAddress)
        AllowedHosts = @($IpAddress)
        CertificatePath = (Join-Path $certDir 'pegasus-cert.pem')
        CertificateKeyPath = (Join-Path $certDir 'pegasus-key.pem')
        WebRoot = (Join-Path $InstallDir 'wwwroot')
    }
}
if ($PSCmdlet.ShouldProcess((Join-Path $InstallDir 'appsettings.json'), 'Escribir')) {
    $settings | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $InstallDir 'appsettings.json') -Encoding UTF8
    # Copia en la carpeta protegida de datos: update.ps1 la usa como referencia (nunca la de fábrica del paquete).
    $settings | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $DataDir 'appsettings.json') -Encoding UTF8
    Write-Ok "https://${IpAddress}:$Port"
}

Write-Step 'Servicio de Windows'
$bin = '"' + (Join-Path $InstallDir 'PegasusDeck.Service.exe') + '"'
if ($PSCmdlet.ShouldProcess($ServiceName, 'Crear o actualizar el servicio (LocalSystem, automático, reinicio si falla)')) {
    if (Get-Service $ServiceName -ErrorAction SilentlyContinue) {
        & sc.exe config $ServiceName binPath= $bin start= auto obj= LocalSystem | Out-Null
    } else {
        New-Service -Name $ServiceName -BinaryPathName $bin -DisplayName 'Pegasus Deck' -StartupType Automatic | Out-Null
    }
    & sc.exe description $ServiceName 'Panel Pegasus Deck: API local para el iPad (solo red local).' | Out-Null
    & sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null
    Write-Ok 'Servicio PegasusDeck (inicio automático)'
}

Write-Step 'Agente de sesión'
$agentExe = Join-Path $InstallDir 'PegasusDeck.Agent.exe'
if ($PSCmdlet.ShouldProcess($TaskName, 'Crear la tarea "al iniciar sesión" (sin elevación) y el acceso del menú Inicio')) {
    $action = New-ScheduledTaskAction -Execute $agentExe -WorkingDirectory $InstallDir
    $trigger = New-ScheduledTaskTrigger -AtLogOn
    $principal = New-ScheduledTaskPrincipal -GroupId $SidUsers -RunLevel Limited
    $taskSettings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $taskSettings `
        -Description 'Agente de Pegasus Deck en la sesión del usuario (apps, audio, OBS).' -Force | Out-Null
    $shell = New-Object -ComObject WScript.Shell
    $lnk = $shell.CreateShortcut($ShortcutPath)
    $lnk.TargetPath = $agentExe
    $lnk.WorkingDirectory = $InstallDir
    $lnk.Description = 'Pegasus Deck (agente de la bandeja)'
    $lnk.Save()
    Write-Ok 'Arrancará solo al iniciar sesión (y en el menú Inicio: "Pegasus Deck")'
}

Write-Step 'Firewall (red Privada, subred local)'
$rule = Get-NetFirewallRule -DisplayName $FirewallRule -ErrorAction SilentlyContinue
if ($rule) {
    $ports = ($rule | Get-NetFirewallPortFilter).LocalPort
    if ($ports -contains "$Port") { Write-Ok "La regla '$FirewallRule' ya existe (puerto $Port)" }
    elseif ($PSCmdlet.ShouldProcess($FirewallRule, "Cambiar al puerto $Port")) { $rule | Get-NetFirewallPortFilter | Set-NetFirewallPortFilter -LocalPort $Port; Write-Ok "Regla actualizada al puerto $Port" }
} elseif ($PSCmdlet.ShouldProcess($FirewallRule, "Crear regla de entrada TCP $Port (Privada, subred local)")) {
    New-NetFirewallRule -DisplayName $FirewallRule -Direction Inbound -Protocol TCP -LocalPort $Port -Profile Private -RemoteAddress LocalSubnet -Action Allow | Out-Null
    Write-Ok 'Regla creada'
}

Write-Step 'Arrancar y comprobar'
if ($PSCmdlet.ShouldProcess($ServiceName, 'Arrancar el servicio y el agente')) {
    Start-Service $ServiceName
    if (-not (Wait-Port $Port)) { throw "El servicio no escucha en el puerto $Port. Revisa el Visor de eventos (Aplicación, origen PegasusDeck)." }
    if (Test-Health $IpAddress $Port) { Write-Ok "Responde en https://${IpAddress}:$Port" } else { Write-Warn 'Escucha, pero /api/health no responde todavía.' }
    Start-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3
    if (Get-Process PegasusDeck.Agent -ErrorAction SilentlyContinue) { Write-Ok 'Agente en marcha (icono en la bandeja)' }
    else { Write-Warn 'El agente no se ha iniciado ahora: ábrelo desde el menú Inicio ("Pegasus Deck") o cierra sesión y vuelve a entrar.' }
}

if ($whatIf) {
    Write-Host "`nSimulación terminada: no se ha cambiado nada. Ejecuta .\install.ps1 (sin -WhatIf) en PowerShell como administrador." -ForegroundColor Green
    return
}
Write-Host "`nPegasus Deck instalado. En el iPad: https://${IpAddress}:$Port (si cambias de carpeta de datos, vuelve a emparejar)." -ForegroundColor Green
Write-Host 'Recuerda guardar fuera del PC la clave de la CA de mkcert (rootCA-key.pem).' -ForegroundColor Yellow
