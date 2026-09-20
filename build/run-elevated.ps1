param(
    [string]$DistributionPath = (Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'BTHaven-Instalador-Publico')
)
$ErrorActionPreference = 'Stop'
# Elevate only certificate trust inside the installer, not installation for the original user.
& (Join-Path $PSScriptRoot 'run-install.ps1') -DistributionPath $DistributionPath
