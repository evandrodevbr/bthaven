param(
    [string]$DistributionPath = (Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'BTHaven-Instalador-Publico')
)
$ErrorActionPreference = 'Stop'
$installer = Join-Path $DistributionPath 'Install.ps1'
if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) { throw 'Prepare a new distribution with copy-to-desktop.ps1 first.' }
Start-Transcript -Path (Join-Path $PSScriptRoot 'install.log') | Out-Null
try { & $installer } finally { Stop-Transcript | Out-Null }
