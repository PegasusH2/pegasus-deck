<#
.SYNOPSIS
  Prepara el paquete de instalación de Pegasus Deck (no necesita administrador ni toca el sistema).

.DESCRIPTION
  - Servicio y agente en Release, autocontenidos (incluyen .NET 8: no dependen del runtime del PC).
  - La PWA compilada en wwwroot.
  Resultado: pc\install\out\PegasusDeck\ (lo que install.ps1 copia a Program Files).
#>
[CmdletBinding()]
param(
    [string]$Output
)
$ErrorActionPreference = 'Stop'
if (-not $Output) { $Output = Join-Path $PSScriptRoot 'out\PegasusDeck' }
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$pc = Join-Path $repo 'pc'

function Step([string]$text) { Write-Host "`n== $text" -ForegroundColor Cyan }

if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }
New-Item -ItemType Directory -Force $Output | Out-Null

Step 'Servicio (Release, autocontenido)'
dotnet publish (Join-Path $pc 'src\PegasusDeck.Service') -c Release -r win-x64 --self-contained true -o $Output -p:PublishReadyToRun=false --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Falló la publicación del servicio.' }

Step 'Agente (Release, autocontenido)'
dotnet publish (Join-Path $pc 'src\PegasusDeck.Agent') -c Release -r win-x64 --self-contained true -o $Output --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Falló la publicación del agente.' }

Step 'PWA'
Push-Location (Join-Path $repo 'pwa')
try {
    npm run build --silent
    if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación de la PWA.' }
} finally { Pop-Location }
Copy-Item (Join-Path $repo 'pwa\dist') (Join-Path $Output 'wwwroot') -Recurse

# Nada de desarrollo en el paquete: ni símbolos ni configuración local.
Get-ChildItem $Output -Filter *.pdb | Remove-Item -Force
foreach ($exe in 'PegasusDeck.Service.exe', 'PegasusDeck.Agent.exe') {
    if (-not (Test-Path (Join-Path $Output $exe))) { throw "Falta $exe en el paquete." }
}
$size = (Get-ChildItem $Output -Recurse | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("`nPaquete listo: {0} ({1:N0} MB)" -f $Output, $size) -ForegroundColor Green
