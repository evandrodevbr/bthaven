$lm_tp = Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object { $_.Subject -eq 'CN=AppPublisher' }
$lm_root = Get-ChildItem Cert:\LocalMachine\Root | Where-Object { $_.Subject -eq 'CN=AppPublisher' }
Write-Output "LM TrustedPeople: $([bool]$lm_tp)"
Write-Output "LM Root: $([bool]$lm_root)"
Write-Output '--- package ---'
Get-AppxPackage *BTHaven* | ForEach-Object { Write-Output "Name=$($_.Name) Version=$($_.Version) Status=$($_.Status) Install=$($_.InstallLocation)" }
Write-Output '--- process ---'
Get-Process BTHaven* -ErrorAction SilentlyContinue | ForEach-Object { Write-Output "PID $($_.Id) $($_.ProcessName) title='$($_.MainWindowTitle)'" }
