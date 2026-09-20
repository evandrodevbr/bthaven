$who = [Security.Principal.WindowsIdentity]::GetCurrent()
Write-Output "user=$($who.Name)"
Write-Output "groups:"
$who.Groups | ForEach-Object { Write-Output "  $($_.Value)" }
# Inspection is read-only; never terminate another installer or dismiss its approval prompt.
$elev = Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'cmd.exe' }
foreach ($e in $elev) { Write-Output "cmd: $($e.ProcessId) :: $($e.CommandLine)" }
