#requires -Version 5.1
[CmdletBinding()]
param([switch]$TrustCertificateOnly)

$ErrorActionPreference = 'Stop'

$packages = @(Get-ChildItem -LiteralPath $PSScriptRoot -File | Where-Object { $_.Extension -in '.msix', '.appx' })
$certificates = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cer' -File)
if ($packages.Count -ne 1 -or $certificates.Count -ne 1) {
    throw 'Expected exactly one MSIX/AppX and one public CER beside this installer.'
}
if (Get-ChildItem -LiteralPath $PSScriptRoot -Recurse -File -Force | Where-Object { $_.Extension -in '.pfx', '.p12', '.key', '.pem' }) {
    throw 'Private-key material found. Do not distribute this folder; remove it only after owner approval.'
}
$package = $packages[0]
$certificatePath = $certificates[0].FullName
if ([Security.Cryptography.X509Certificates.X509Certificate2]::GetCertContentType($certificatePath) -ne [Security.Cryptography.X509Certificates.X509ContentType]::Cert) {
    throw 'The CER must contain only a public certificate, not a PFX or certificate bundle.'
}
$certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($certificatePath)
try {
    $constraints = @($certificate.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.19' })
    $eku = @($certificate.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.37' })
    if ($certificate.HasPrivateKey -or $constraints.Count -ne 1 -or $constraints[0].CertificateAuthority -or
        $eku.Count -ne 1 -or $eku[0].EnhancedKeyUsages.Value -notcontains '1.3.6.1.5.5.7.3.3' -or
        $certificate.NotBefore -gt (Get-Date) -or $certificate.NotAfter -le (Get-Date)) {
        throw 'Expected a currently valid, non-CA code-signing public certificate.'
    }
    $signature = Get-AuthenticodeSignature -LiteralPath $package.FullName
    if (-not $signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint -or
        $signature.Status -notin 'Valid', 'UnknownError', 'NotTrusted') {
        throw "Package signature is missing, rejected, or does not match the CER: $($signature.Status)."
    }
    if ($signature.Status -ne 'Valid') {
        Write-Warning "Package signature is UNVERIFIED ($($signature.Status)); its integrity cannot yet be certified. Trust requires your explicit approval, and installation requires a Valid signature afterward."
    }
    $storePath = "Cert:\LocalMachine\TrustedPeople\$($certificate.Thumbprint)"
    if ($TrustCertificateOnly) {
        $principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
        if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Certificate trust requires administrator approval.' }
        if (-not (Test-Path -LiteralPath $storePath)) {
            Write-Host "Subject: $($certificate.Subject)`nExpires: $($certificate.NotAfter)`nThumbprint: $($certificate.Thumbprint)"
            Write-Host 'This trusts this publisher for ALL users in LocalMachine\TrustedPeople, never Root.'
            Write-Host 'Verify the thumbprint with the publisher through a separate trusted channel.'
            $approval = Read-Host 'To approve the public certificate import, type its complete thumbprint (anything else cancels)'
            if ($approval -cne $certificate.Thumbprint) { throw 'Certificate import cancelled.' }
            Import-Certificate -FilePath $certificatePath -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
        }
        return
    }
    if (-not (Test-Path -LiteralPath $storePath)) {
        Write-Host 'Windows will request elevation for public-certificate trust only. Installation remains in this user session.'
        $powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $process = Start-Process -FilePath $powershell -Verb RunAs -Wait -PassThru -ArgumentList @(
            '-NoProfile', '-File', ('"{0}"' -f $PSCommandPath), '-TrustCertificateOnly'
        )
        if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $storePath)) { throw 'Certificate trust was not approved or failed.' }
    }
    $signature = Get-AuthenticodeSignature -LiteralPath $package.FullName
    if ($signature.Status -ne 'Valid' -or -not $signature.SignerCertificate -or
        $signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) {
        throw "Package signature is not trusted and valid: $($signature.Status)."
    }
    $dependencies = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Dependencies') -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -in '.appx', '.msix' })
    foreach ($dependency in $dependencies) {
        $dependencySignature = Get-AuthenticodeSignature -LiteralPath $dependency.FullName
        if ($dependencySignature.Status -ne 'Valid') {
            throw "Dependency signature must be trusted and Valid: $($dependency.Name) ($($dependencySignature.Status))."
        }
    }
    Write-Host "Package: $($package.FullName)`nSHA256: $((Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash)"
    Write-Host "Publisher: $($certificate.Subject)`nDependencies: $($dependencies.Name -join ', ')"
    if ((Read-Host 'Type INSTALL to install this package and the listed dependencies for the current user') -cne 'INSTALL') { throw 'Installation cancelled.' }
    $arguments = @{ Path = $package.FullName; ErrorAction = 'Stop' }
    if ($dependencies.Count) { $arguments.DependencyPath = [string[]]$dependencies.FullName }
    Add-AppxPackage @arguments
    Write-Output 'INSTALL-DONE'
}
finally {
    $certificate.Dispose()
}
