#requires -Version 5.1
# Checks syntax and private-key rejection only; never installs packages or changes certificate stores.
$ErrorActionPreference = 'Stop'
foreach ($script in Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1' -File) {
    $tokens = $null
    $parseErrors = $null
    [void][Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count) { throw "$($script.Name): $($parseErrors.Message -join '; ')" }
}
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('BTHaven-packaging-check-' + [guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $temporary | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install-package.ps1') -Destination (Join-Path $temporary 'Install.ps1')
    foreach ($name in 'test.msix', 'test.cer', 'private.pfx') {
        [IO.File]::WriteAllText((Join-Path $temporary $name), 'not a real package, certificate or key')
    }
    $rejected = $false
    try { & (Join-Path $temporary 'Install.ps1') } catch {
        if ($_.Exception.Message -notlike 'Private-key material found.*') { throw }
        $rejected = $true
    }
    if (-not $rejected) { throw 'Installer accepted a distribution containing private-key material.' }
    Write-Output 'Packaging syntax and private-key rejection checks passed; no certificate or installation operations performed.'
} finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
}
