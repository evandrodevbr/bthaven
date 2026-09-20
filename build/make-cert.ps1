#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$PfxPath,
    [Security.SecureString]$Password
)

$ErrorActionPreference = 'Stop'
if ($Password -and -not $PfxPath) { throw 'Password is only accepted with an explicit PfxPath.' }
if ($PfxPath) {
    if ([IO.Path]::GetExtension($PfxPath) -ne '.pfx') { throw 'Private-key backup must use a .pfx filename.' }
    if (Test-Path -LiteralPath $PfxPath) { throw 'Refusing to overwrite an existing private-key backup.' }
    $PfxPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($PfxPath)
}
[xml]$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\src\BTHaven.App\Package.appxmanifest') -Raw
$subject = [string]$manifest.Package.Identity.Publisher
$existing = @(Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object {
    $constraints = @($_.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.19' })
    $_.Subject -eq $subject -and $_.HasPrivateKey -and $_.NotBefore -le (Get-Date) -and $_.NotAfter -gt (Get-Date) -and
        $constraints.Count -eq 1 -and -not $constraints[0].CertificateAuthority
} | Sort-Object NotAfter -Descending)
if ($existing.Count) {
    $cert = $existing[0]
} else {
    Write-Host "A signing certificate for $subject will be created in CurrentUser\My. No trust store is modified."
    if ((Read-Host 'Type CREATE to approve certificate creation') -cne 'CREATE') { throw 'Certificate creation cancelled.' }
    $keyPolicy = if ($PfxPath) { 'Exportable' } else { 'NonExportable' }
    $cert = New-SelfSignedCertificate -Subject $subject -CertStoreLocation 'Cert:\CurrentUser\My' `
        -Type Custom -KeyUsage DigitalSignature -KeyExportPolicy $keyPolicy -HashAlgorithm SHA256 -KeyLength 2048 `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}') -NotAfter (Get-Date).AddYears(1)
}
Write-Output "Signing certificate: $($cert.Thumbprint); expires $($cert.NotAfter)"
Write-Output "Sign from CurrentUser\My using /p:AppxPackageSigningEnabled=true /p:PackageCertificateThumbprint=$($cert.Thumbprint)"
if ($PfxPath) {
    Write-Warning "Private-key export requested: $PfxPath. Never put this file in a distribution folder."
    if ((Read-Host "Type EXPORT to approve exporting private key $($cert.Thumbprint)") -cne 'EXPORT') { throw 'Private-key export cancelled.' }
    if (-not $Password) { $Password = Read-Host 'Private-key backup password' -AsSecureString }
    if ($Password.Length -eq 0) { throw 'Private-key backup password must not be empty.' }
    # A default non-exportable key cannot be exported later; use store-based signing instead.
    Export-PfxCertificate -Cert $cert -FilePath $PfxPath -Password $Password -NoClobber | Out-Null
    Write-Output "Private backup created: $PfxPath"
}
