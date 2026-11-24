# PROTOTYPE helper (ReparoHome#49): console.ps1 with its own job slot, so it never collides with other sessions' jobs.
# Usage (over SSH): powershell -File ...\proto49\vision-modality\vm\console49.ps1 -Script <job.ps1> [-TimeoutSec 600]
param([Parameter(Mandatory)][string]$Script, [int]$TimeoutSec = 600)
$r = 'C:\Users\reparo\UiPath\reparo\proto49'
Remove-Item "$r\console.out", "$r\console.done" -ErrorAction SilentlyContinue
Set-Content "$r\console.job" $Script
schtasks /Create /F /TN ReparoConsole49 /SC ONCE /ST 00:00 /RU reparo /IT /RL LIMITED /TR "powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File $r\vision-modality\vm\wrapper49.ps1" 2>&1 | Out-Null
schtasks /Run /TN ReparoConsole49 | Out-Null
$t = 0; while (-not (Test-Path "$r\console.done") -and $t -lt $TimeoutSec) { Start-Sleep 1; $t++ }
if (Test-Path "$r\console.out") { Get-Content "$r\console.out" }
if (-not (Test-Path "$r\console.done")) { Write-Error "timed out after $TimeoutSec s"; exit 1 }
