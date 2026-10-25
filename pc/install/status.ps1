<#
.SYNOPSIS
  Estado de Pegasus Deck en este PC (no cambia nada; no necesita administrador).
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Continue'
. (Join-Path $PSScriptRoot 'common.ps1')

Write-Step 'Programa y servicio'
if (Test-Path (Join-Path $InstallDir 'PegasusDeck.Service.exe')) { Write-Ok "Instalado en $InstallDir" } else { Write-Warn 'No instalado (install.ps1).' }
$svc = Get-Service $ServiceName -ErrorAction SilentlyContinue
if ($svc) {
    $cim = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'"
    $msg = "Servicio: $($svc.Status) · inicio $($cim.StartMode) · cuenta $($cim.StartName)"
    if ($svc.Status -eq 'Running') { Write-Ok $msg } else { Write-Warn $msg }
} else { Write-Warn 'El servicio PegasusDeck no existe.' }

Write-Step 'Agente'
$task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($task) { Write-Ok "Tarea al iniciar sesión: $($task.State)" } else { Write-Warn 'No hay tarea para el agente.' }
$agents = Get-Process PegasusDeck.Agent -ErrorAction SilentlyContinue
if ($agents) { foreach ($a in $agents) { Write-Ok "Agente en marcha (sesión $($a.SessionId)): $($a.Path)" } } else { Write-Warn 'El agente no está en marcha en ninguna sesión.' }

Write-Step 'Red'
$config = $null
$settingsFile = Join-Path $InstallDir 'appsettings.json'
if (Test-Path $settingsFile) { $config = Get-Content $settingsFile -Raw | ConvertFrom-Json }
if ($config) {
    $ip = @($config.Pegasus.ListenAddresses)[0]
    $port = [int]$config.Pegasus.Port
    $current = Get-LanAddress
    if ($current -and $current -ne $ip) { Write-Warn "Configurado para $ip, pero ahora el PC tiene ${current}: reserva la IP en el router o reinstala con -IpAddress." } else { Write-Ok "IP $ip" }
    if (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue) { Write-Ok "Escuchando en el puerto $port" } else { Write-Warn "Nadie escucha en el puerto $port" }
    if (Test-Health $ip $port) { Write-Ok "https://${ip}:$port responde" } else { Write-Warn "https://${ip}:$port no responde" }
    $cf = $config.Pegasus.CertificatePath
    if ($cf -and (Test-Path $cf -ErrorAction SilentlyContinue)) {
        $cert = Read-PemCertificate $cf
        $days = [int]($cert.NotAfter - (Get-Date)).TotalDays
        if ($days -gt 30) { Write-Ok "Certificado válido $days días más" } else { Write-Warn "El certificado caduca en $days días" }
    } elseif ($cf) { Write-Info "Certificado: $cf (sin permiso para leerlo sin administrador o no existe)" }
}
$rule = Get-NetFirewallRule -DisplayName $FirewallRule -ErrorAction SilentlyContinue
if ($rule) { Write-Ok "Firewall: '$FirewallRule' (perfil $($rule.Profile), activa: $($rule.Enabled))" } else { Write-Warn 'Sin regla de firewall: el iPad no podrá conectar.' }
foreach ($p in Get-NetConnectionProfile) { if ($p.NetworkCategory -ne 'Private') { Write-Warn "La red '$($p.Name)' es $($p.NetworkCategory): márcala como Privada." } }

