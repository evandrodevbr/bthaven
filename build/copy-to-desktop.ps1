#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackagePath,
    [string]$Destination = (Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'BTHaven-Instalador-Publico')
)

$ErrorActionPreference = 'Stop'

$package = Get-Item -LiteralPath $PackagePath
if ($package.PSIsContainer -or $package.Extension -notin '.msix', '.appx') { throw 'Select one signed MSIX/AppX package.' }
$signature = Get-AuthenticodeSignature -LiteralPath $package.FullName
if (-not $signature.SignerCertificate -or $signature.Status -notin 'Valid', 'UnknownError', 'NotTrusted') {
    throw "Package signature is missing or rejected: $($signature.Status)."
}
if ($signature.Status -ne 'Valid') {
    Write-Warning "Distribution package signature is UNVERIFIED ($($signature.Status)); its integrity cannot yet be certified. The installer requires a Valid signature after explicitly approved certificate trust."
}
# A fresh directory avoids silently redistributing a private key left by an older installer.
if (Test-Path -LiteralPath $Destination) { throw 'Destination already exists. Choose a NEW directory; no existing files will be deleted or overwritten.' }
$certificate = $signature.SignerCertificate
$constraints = @($certificate.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.19' })
$eku = @($certificate.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.37' })
if ($constraints.Count -ne 1 -or $constraints[0].CertificateAuthority -or $eku.Count -ne 1 -or
    $eku[0].EnhancedKeyUsages.Value -notcontains '1.3.6.1.5.5.7.3.3' -or
    $certificate.NotBefore -gt (Get-Date) -or $certificate.NotAfter -le (Get-Date)) { throw 'Package signer must be a valid, non-CA code-signing certificate.' }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($package.FullName)
try {
    $entry = $archive.GetEntry('AppxManifest.xml')
    if (-not $entry) { throw 'Package has no AppxManifest.xml.' }
    $reader = [IO.StreamReader]::new($entry.Open())
    try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $architecture = [string]$manifest.Package.Identity.ProcessorArchitecture
    if ($architecture -notin 'x86', 'x64', 'arm', 'arm64', 'neutral') { throw "Unsupported package architecture: $architecture" }
} finally { $archive.Dispose() }

$dependencyRoot = Join-Path $package.DirectoryName 'Dependencies'
$dependencies = @(foreach ($folder in (@($dependencyRoot, (Join-Path $dependencyRoot $architecture), (Join-Path $dependencyRoot 'neutral')) | Select-Object -Unique)) {
    if (Test-Path -LiteralPath $folder) {
        Get-ChildItem -LiteralPath $folder -File | Where-Object { $_.Extension -in '.appx', '.msix' }
    }
})
if (@($dependencies.Name | Select-Object -Unique).Count -ne $dependencies.Count) { throw 'Duplicate dependency filenames; resolve the package output before distributing.' }
foreach ($dependency in $dependencies) {
    $dependencySignature = Get-AuthenticodeSignature -LiteralPath $dependency.FullName
    if ($dependencySignature.Status -ne 'Valid') {
        throw "Dependency signature must be trusted and Valid: $($dependency.FullName) ($($dependencySignature.Status))."
    }
}

$destinationPath = (New-Item -ItemType Directory -Path $Destination).FullName
Copy-Item -LiteralPath $package.FullName -Destination (Join-Path $destinationPath $package.Name)
[IO.File]::WriteAllBytes((Join-Path $destinationPath 'AppPublisher.cer'), $certificate.RawData)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install-package.ps1') -Destination (Join-Path $destinationPath 'Install.ps1')
if ($dependencies.Count) {
    $dependencyDestination = New-Item -ItemType Directory -Path (Join-Path $destinationPath 'Dependencies')
    foreach ($dependency in $dependencies) { Copy-Item -LiteralPath $dependency.FullName -Destination $dependencyDestination.FullName }
}
$launcher = @'
@echo off
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -File "%~dp0Install.ps1"
set "result=%errorlevel%"
if not "%result%"=="0" echo Installation failed or was cancelled. Exit code: %result%
pause
exit /b %result%
'@
[IO.File]::WriteAllText((Join-Path $destinationPath 'INSTALL.cmd'), (($launcher -replace "`r?`n", "`r`n") + "`r`n"), [Text.Encoding]::ASCII)
$instructions = @"
BTHaven - public-certificate installer

1. Obtain this complete folder from the publisher. Do not use a folder containing a PFX/private key.
2. Verify this signer thumbprint through a SEPARATE trusted channel: $($certificate.Thumbprint)
   Subject: $($certificate.Subject)
   Package: $($package.Name)
   SHA256: $((Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash)
   An untrusted signature is UNVERIFIED: distribution does not certify its integrity.
   The installer requires a Valid signature with the matching signer after certificate trust.
3. Open INSTALL.cmd as the user who will use BTHaven (do not run the whole installer as a different administrator).
4. If trust is missing, Windows requests administrator elevation for the CER import only.
   Review the certificate and type its complete thumbprint to approve LocalMachine\TrustedPeople.
   This affects all users. No private key is imported and Root is never modified.
5. Back in the original user window, review the package and dependencies, then type INSTALL.
   Declining either prompt stops installation. Organization policies may block installation; ask your administrator.
6. If PowerShell policy blocks the script, have your administrator review/sign or approve the scripts.
   This installer does not change execution policy or bypass organization policy.

Only the package, public CER, installer scripts, these instructions and dependencies with Valid signatures belong here.
Existing Desktop installers, PFX files and installed certificates were NOT deleted or changed.
If an older PFX was shared, its owner must approve cleanup and evaluate certificate/key rotation before further distribution.
"@
[IO.File]::WriteAllText((Join-Path $destinationPath 'INSTALL.txt'), $instructions, [Text.Encoding]::UTF8)
Write-Output "Distribution prepared: $destinationPath"
Write-Output "Verify and communicate signer thumbprint separately: $($certificate.Thumbprint)"
