$ErrorActionPreference = 'Continue'
$staging = Join-Path $PSScriptRoot '../src/BTHaven.App/bin/Release/net10.0-windows10.0.26100.0/win-x64/AppX'
Write-Output "manifest mtime: $((Get-Item "$staging/AppxManifest.xml").LastWriteTime)"
$packageCache = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget/packages' }
$bt = Get-ChildItem (Join-Path $packageCache 'microsoft.windows.sdk.buildtools') -Recurse -Include makeappx.exe,signtool.exe,makepri.exe -ErrorAction SilentlyContinue
$bt | Where-Object { $_.FullName -match '\\x64\\' } | Select-Object -First 8 | ForEach-Object { Write-Output $_.FullName }
