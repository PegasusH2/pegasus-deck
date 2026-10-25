<#
.SYNOPSIS
  Actualiza Pegasus Deck con un paquete nuevo (build.ps1), con copia de seguridad y vuelta atrás si falla.

.DESCRIPTION
  1. Comprueba ANTES de tocar nada que hay una configuración válida (IP y certificado): la de
     C:\ProgramData\PegasusDeck\appsettings.json (copia protegida que guarda install.ps1) o la instalada.
     Si no la hay, no cambia nada y pide ejecutar install.ps1.
  2. Copia de seguridad de los datos en C:\ProgramData\PegasusDeck\backups\<fecha> (carpeta protegida).
  3. Para servicio y agente; guarda el programa actual en "PegasusDeck.old".
  4. Copia el programa nuevo con esa configuración; arranca y comprueba /api/health.
  5. Si no arranca o no responde, restaura el programa anterior y lo vuelve a arrancar.
  Nunca borra configuración, dispositivos emparejados ni apps autorizadas. Con -WhatIf no cambia nada.
#>
[CmdletBinding(SupportsShouldProcess)]
param([string]$Package)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
if (-not $Package) { $Package = Join-Path $PSScriptRoot 'out\PegasusDeck' }
Assert-Admin ([bool]$WhatIfPreference)

if (-not (Get-Service $ServiceName -ErrorAction SilentlyContinue) -or -not (Test-Path $InstallDir)) { throw 'Pegasus Deck no está instalado: usa install.ps1.' }
if (-not (Test-Path (Join-Path $Package 'PegasusDeck.Service.exe'))) { throw "No hay paquete en $Package. Ejecuta antes .\build.ps1." }

# Configuración válida = con IP y certificado (la que escribe install.ps1). Nunca la de fábrica del paquete.
function Read-ValidConfig([string]$file) {
    if (-not (Test-Path $file)) { return $null }
    try { $c = Get-Content $file -Raw | ConvertFrom-Json } catch { return $null }
    if (@($c.Pegasus.ListenAddresses).Count -gt 0 -and $c.Pegasus.CertificatePath) { return $c }
    return $null
}
$savedConfig = Join-Path $DataDir 'appsettings.json'
$configFile = @($savedConfig, (Join-Path $InstallDir 'appsettings.json')) | Where-Object { Read-ValidConfig $_ } | Select-Object -First 1
if (-not $configFile) { throw 'No hay una configuración válida (IP y certificado). No se ha cambiado nada: ejecuta install.ps1, que la vuelve a crear sin perder los datos.' }
$config = Read-ValidConfig $configFile
$configJson = Get-Content $configFile -Raw
$ip = @($config.Pegasus.ListenAddresses)[0]
$port = [int]$config.Pegasus.Port
$old = "$InstallDir.old"
Write-Ok "Configuración: $configFile (https://${ip}:$port)"

Write-Step 'Copia de seguridad de la configuración'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = Join-Path $DataDir "backups\$stamp"
if ($PSCmdlet.ShouldProcess($backup, 'Copiar apps.json, devices.json, certificados y configuración')) {
    New-Item -ItemType Directory -Force $backup | Out-Null
    Get-ChildItem $DataDir -Exclude 'backups', 'logs' | Copy-Item -Destination $backup -Recurse -Force
    Write-Ok $backup
}

function Restore-Previous {
    Write-Warn 'La versión nueva no arranca o no responde: se vuelve a la anterior.'
    Stop-Service $ServiceName -Force -ErrorAction SilentlyContinue
    Stop-PegasusAgents
    Start-Sleep -Seconds 1
    if (Test-Path $InstallDir) { Remove-Item $InstallDir -Recurse -Force }
    Rename-Item $old (Split-Path $InstallDir -Leaf)
    Start-Service $ServiceName -ErrorAction SilentlyContinue
    Start-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
}

Write-Step 'Sustituir el programa'
if ($PSCmdlet.ShouldProcess($InstallDir, 'Parar, guardar el actual como .old y copiar el nuevo')) {
    Stop-Service $ServiceName -Force
    Stop-PegasusAgents
    Start-Sleep -Seconds 1
    # Un .old de un intento anterior no se borra: se aparta con su fecha.
    if (Test-Path $old) { Rename-Item $old "$(Split-Path $old -Leaf)-$stamp" }
    Rename-Item $InstallDir (Split-Path $old -Leaf)
    New-Item -ItemType Directory -Force $InstallDir | Out-Null
    & robocopy.exe $Package $InstallDir /MIR /NFL /NDL /NJH /NJS /NP /R:2 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { Restore-Previous; throw "La copia ha fallado (robocopy $LASTEXITCODE); sigue la versión anterior." }
    Set-Content (Join-Path $InstallDir 'appsettings.json') $configJson -Encoding UTF8
    Set-Content $savedConfig $configJson -Encoding UTF8
    Write-Ok 'Programa nuevo copiado (configuración conservada)'
}

# El PC ya no tiene la IP configurada (el router le dio otra): el servicio no podría escuchar en ella y se volvería
# a la versión anterior sin motivo. Se deja la versión nueva y se cambia la IP desde la bandeja (ADR-034).
$ipHere = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue).IPAddress -contains $ip
if (-not $ipHere -and -not $WhatIfPreference) {
    Start-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    Write-Warn "Este PC ya no tiene la IP ${ip}: el servicio no puede arrancar con ella."
    Write-Warn 'Versión nueva instalada. Ahora: icono de Pegasus Deck en la bandeja → «Dirección IP del PC…» → elige la IP actual → Aplicar.'
    Write-Info "La versión anterior queda en $old por si hiciera falta."
    return
}

Write-Step 'Arrancar y comprobar'
if ($PSCmdlet.ShouldProcess($ServiceName, 'Arrancar y comprobar /api/health')) {
    $ok = $false
    try {
        Start-Service $ServiceName
        $ok = (Wait-Port $port) -and (Test-Health $ip $port)
    } catch {
        Write-Warn "El servicio no ha arrancado: $($_.Exception.Message)"
    }
    if (-not $ok) {
        Restore-Previous
        throw "Actualización cancelada; sigue la versión anterior. Mira el Visor de eventos (Aplicación). Copia de los datos en $backup."
    }
    Remove-Item $old -Recurse -Force
    Start-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    Write-Ok "Actualizado y respondiendo en https://${ip}:$port"
}
