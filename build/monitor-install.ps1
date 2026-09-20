$logPath = Join-Path $PSScriptRoot 'install.log'
foreach ($i in 1..20) {
  Start-Sleep 8
  $lmTrustedPeople = Get-ChildItem Cert:\LocalMachine\TrustedPeople -ErrorAction SilentlyContinue | Where-Object { $_.Subject -eq 'CN=AppPublisher' }
  $pkg = Get-AppxPackage 57EE6F58* -ErrorAction SilentlyContinue
  $log = Get-Content -LiteralPath $logPath -Raw -ErrorAction SilentlyContinue
  Write-Output ("t=" + ($i * 8) + "s trustedPeople=" + [bool]$lmTrustedPeople + " pkgs=" + (@($pkg).Count))
  if ($log -match 'INSTALL-DONE') { Write-Output 'LOG DONE'; break }
}
Write-Output '=== install.log ==='
if (Test-Path -LiteralPath $logPath) { Get-Content -LiteralPath $logPath }
Write-Output '=== Get-AppxPackage 57EE6F58* ==='
Get-AppxPackage 57EE6F58* | ForEach-Object { Write-Output "$($_.PackageFullName) Status=$($_.Status) Install=$($_.InstallLocation)" }
Write-Output '=== LM stores ==='
Write-Output ("LM Root: " + [bool](Get-ChildItem Cert:\LocalMachine\Root | Where-Object { $_.Subject -eq 'CN=AppPublisher' }))
Write-Output ("LM TrustedPeople: " + [bool](Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object { $_.Subject -eq 'CN=AppPublisher' }))
Write-Output '=== procs ==='
Get-Process BTHaven* -ErrorAction SilentlyContinue | ForEach-Object { Write-Output "PID $($_.Id) title='$($_.MainWindowTitle)' path=$($_.Path)" }
