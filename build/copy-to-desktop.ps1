#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackagePath,
    [string]$Destination = (Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'BTHaven-Instalador-Publico')
)

$ErrorActionPreference = 'Stop'

# A freshly signed package has an intact signature whose chain ends in a root the
# machine does not trust yet: that is the normal state before installation, and
# Get-AuthenticodeSignature reports it as UnknownError/NotTrusted. Accept those,
# reject anything that means the file or signature itself is damaged.
function Test-SignatureIntact {
    param([Parameter(Mandatory)][System.Management.Automation.Signature]$Signature)

    if (-not $Signature.SignerCertificate) { return $false }
    if ($Signature.Status -eq 'Valid') { return $true }
    if ($Signature.Status -notin 'UnknownError', 'NotTrusted') { return $false }

    $chain = [Security.Cryptography.X509Certificates.X509Chain]::new()
    try {
        $null = $chain.Build($Signature.SignerCertificate)
        $acceptable = 'UntrustedRoot', 'RevocationStatusUnknown', 'OfflineRevocation'
        return @($chain.ChainStatus | Where-Object { $_.Status.ToString() -notin $acceptable }).Count -eq 0
    } finally {
        $chain.Dispose()
    }
}

$package = Get-Item -LiteralPath $PackagePath
if ($package.PSIsContainer -or $package.Extension -notin '.msix', '.appx') { throw 'Select one signed MSIX/AppX package.' }
$signature = Get-AuthenticodeSignature -LiteralPath $package.FullName
if (-not (Test-SignatureIntact -Signature $signature)) {
    throw "A signed, unmodified package is required: $($signature.Status)."
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
    if (-not (Test-SignatureIntact -Signature (Get-AuthenticodeSignature -LiteralPath $dependency.FullName))) {
        throw "Damaged or unsigned dependency: $($dependency.FullName)"
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
3. Open INSTALL.cmd as the user who will use BTHaven (do not run the whole installer as a different administrator).
4. If trust is missing, Windows requests administrator elevation for the CER import only.
   Review the certificate and type its complete thumbprint to approve LocalMachine\TrustedPeople.
   This affects all users. No private key is imported and Root is never modified.
5. Back in the original user window, review the package and dependencies, then type INSTALL.
   Declining either prompt stops installation. Organization policies may block installation; ask your administrator.
6. If PowerShell policy blocks the script, have your administrator review/sign or approve the scripts.
   This installer does not change execution policy or bypass organization policy.

Only the package, public CER, installer scripts, these instructions and signed dependencies belong here.
Existing Desktop installers, PFX files and installed certificates were NOT deleted or changed.
If an older PFX was shared, its owner must approve cleanup and evaluate certificate/key rotation before further distribution.
"@
[IO.File]::WriteAllText((Join-Path $destinationPath 'INSTALL.txt'), $instructions, [Text.Encoding]::UTF8)
Write-Output "Distribution prepared: $destinationPath"
Write-Output "Verify and communicate signer thumbprint separately: $($certificate.Thumbprint)"
