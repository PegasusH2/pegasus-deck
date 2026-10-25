# Comunes a los scripts de instalación de Pegasus Deck (se cargan con ". .\common.ps1").

$ServiceName = 'PegasusDeck'
$TaskName = 'Pegasus Deck · agente'
$FirewallRule = 'Pegasus Deck'
$InstallDir = Join-Path $env:ProgramFiles 'PegasusDeck'
$DataDir = Join-Path $env:ProgramData 'PegasusDeck'
$ShortcutPath = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\Pegasus Deck.lnk'
$SidSystem = 'S-1-5-18'
$SidAdmins = 'S-1-5-32-544'
$SidUsers = 'S-1-5-32-545'

# Carga los módulos de red antes de nada: con -WhatIf, al cargarlos PowerShell anuncia cada alias.
$__whatIf = $WhatIfPreference
$WhatIfPreference = $false
Import-Module NetTCPIP, NetConnection, NetSecurity, ScheduledTasks, CimCmdlets -ErrorAction SilentlyContinue -WarningAction SilentlyContinue
$WhatIfPreference = $__whatIf

function Write-Step([string]$text) { Write-Host "`n== $text" -ForegroundColor Cyan }
function Write-Ok([string]$text) { Write-Host "   OK  $text" -ForegroundColor Green }
function Write-Warn([string]$text) { Write-Host "   !!  $text" -ForegroundColor Yellow }
function Write-Info([string]$text) { Write-Host "       $text" }

function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    return ([Security.Principal.WindowsPrincipal]$id).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Sin -WhatIf hace falta una terminal de administrador; con -WhatIf solo se muestra lo que se haría.
function Assert-Admin([bool]$whatIf) {
    if (-not $whatIf -and -not (Test-Admin)) {
        throw 'Abre PowerShell como administrador (o añade -WhatIf para ver lo que haría sin cambiar nada).'
    }
}

# IPv4 de la red local: interfaz conectada con puerta de enlace, preferiblemente con perfil Privado.
function Get-LanAddress {
    $configs = Get-NetIPConfiguration | Where-Object { $_.IPv4DefaultGateway -and $_.NetAdapter.Status -eq 'Up' }
    $private = $configs | Where-Object { (Get-NetConnectionProfile -InterfaceIndex $_.InterfaceIndex -ErrorAction SilentlyContinue).NetworkCategory -eq 'Private' }
    $pick = @($private) + @($configs) | Select-Object -First 1
    if ($pick) { return @($pick.IPv4Address)[0].IPAddress }
    return $null
}

# Cierra el agente de cualquier sesión y los de desarrollo (mismo nombre de pipe: chocarían con el instalado).
function Stop-PegasusAgents {
    Get-Process PegasusDeck.Agent -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

# Carpeta de datos (ADR-013): solo SYSTEM y Administradores, sin herencia; los hijos la heredan.
function Set-ProtectedAcl([string]$path) {
    New-Item -ItemType Directory -Force $path | Out-Null
    & icacls.exe $path /inheritance:r /grant:r "*${SidSystem}:(OI)(CI)F" "*${SidAdmins}:(OI)(CI)F" /Q | Out-Null
    & icacls.exe $path /setowner "*$SidAdmins" /T /C /Q | Out-Null
    & icacls.exe "$path\*" /reset /T /C /Q 2>$null | Out-Null
}

# Lee un certificado PEM (Windows PowerShell 5.1 no tiene X509Certificate2.CreateFromPem).
function Read-PemCertificate([string]$path) {
    $text = Get-Content $path -Raw
    $b64 = ($text -replace '-----BEGIN CERTIFICATE-----', '' -replace '-----END CERTIFICATE-----.*', '' -replace '\s', '')
    return [Security.Cryptography.X509Certificates.X509Certificate2]::new([Convert]::FromBase64String($b64))
}

function Test-CertificateFor([Security.Cryptography.X509Certificates.X509Certificate2]$cert, [string]$ip) {
    $san = $cert.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.17' }
    return $san -and $san.Format($false) -match [regex]::Escape($ip)
}

# /api/health sin datos (ADR-012). -k: la CA local no está en el almacén de Windows (no hace falta).
function Test-Health([string]$ip, [int]$port) {
    $out = & curl.exe -sk --max-time 5 "https://${ip}:$port/api/health" 2>$null
    return ($LASTEXITCODE -eq 0 -and $out -match '"protocol"')
}

function Wait-Port([int]$port, [int]$seconds = 20) {
    for ($i = 0; $i -lt $seconds * 2; $i++) {
        if (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue) { return $true }
        Start-Sleep -Milliseconds 500
    }
    return $false
}
