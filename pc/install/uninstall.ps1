<#
.SYNOPSIS
  Desinstala Pegasus Deck.

.DESCRIPTION
  Quita el servicio, la tarea del agente, el acceso del menú Inicio, la regla de firewall y el programa.
  Los datos (C:\ProgramData\PegasusDeck: iPads emparejados, apps autorizadas, certificados) se conservan
  salvo que se pida -RemoveData, y entonces se pide confirmación. Con -WhatIf no cambia nada.
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Medium')]
param([switch]$RemoveData)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
Assert-Admin ([bool]$WhatIfPreference)

Write-Step 'Servicio y agente'
if ((Get-Service $ServiceName -ErrorAction SilentlyContinue) -and $PSCmdlet.ShouldProcess($ServiceName, 'Parar y eliminar el servicio')) {
    Stop-Service $ServiceName -Force -ErrorAction SilentlyContinue
    & sc.exe delete $ServiceName | Out-Null
    Write-Ok 'Servicio eliminado'
}
if ((Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) -and $PSCmdlet.ShouldProcess($TaskName, 'Eliminar la tarea del agente')) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
    Write-Ok 'Tarea eliminada'
}
if ($PSCmdlet.ShouldProcess('PegasusDeck.Agent', 'Cerrar el agente')) { Stop-PegasusAgents }
if ((Test-Path $ShortcutPath) -and $PSCmdlet.ShouldProcess($ShortcutPath, 'Eliminar el acceso del menú Inicio')) { Remove-Item $ShortcutPath -Force }

Write-Step 'Firewall'
if ((Get-NetFirewallRule -DisplayName $FirewallRule -ErrorAction SilentlyContinue) -and $PSCmdlet.ShouldProcess($FirewallRule, 'Eliminar la regla')) {
    Remove-NetFirewallRule -DisplayName $FirewallRule
    Write-Ok 'Regla eliminada'
}

Write-Step 'Programa'
if ((Test-Path $InstallDir) -and $PSCmdlet.ShouldProcess($InstallDir, 'Eliminar')) {
    Start-Sleep -Seconds 1
    Remove-Item $InstallDir -Recurse -Force
    Write-Ok 'Eliminado'
}

Write-Step 'Datos'
if ($RemoveData) {
    if ((Test-Path $DataDir) -and $PSCmdlet.ShouldContinue("Se borrarán los iPads emparejados, las apps autorizadas, los certificados y las copias de $DataDir. ¿Continuar?", 'Borrar datos de Pegasus Deck')) {
        Remove-Item $DataDir -Recurse -Force
        Write-Ok 'Datos borrados'
    }
} else {
    Write-Info "Se conservan en $DataDir (para borrarlos: .\uninstall.ps1 -RemoveData)."
}
Write-Info 'La configuración del agente (contraseña de OBS cifrada) está en %LOCALAPPDATA%\PegasusDeck de cada usuario.'
