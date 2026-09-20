param(
    [string]$DistributionPath = (Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'BTHaven-Instalador-Publico')
)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'run-install.ps1') -DistributionPath $DistributionPath
