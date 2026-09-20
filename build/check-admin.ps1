Write-Output '--- Administrators members ---'
Get-LocalGroupMember -SID 'S-1-5-32-544' | ForEach-Object { Write-Output $_.Name }
Write-Output '--- token integrity/admin groups ---'
$g = & (Join-Path $env:SystemRoot 'System32\whoami.exe') /groups /fo CSV
$g | Select-String -Pattern 'S-1-16-|S-1-5-32-544' | ForEach-Object { Write-Output $_.Line }
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Write-Output "current process is admin: $isAdmin"
