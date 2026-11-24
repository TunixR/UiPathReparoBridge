# PROTOTYPE helper: run proto46\<project>\Main.xaml (project name in proto46\current.txt; optional proto46\args.json).
$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')
$root = 'C:\Users\reparo\UiPath\reparo\proto46'
$proj = (Get-Content "$root\current.txt" -Raw).Trim()
Remove-Item "$root\trace.log", "$root\app.log", "$root\recovery-*.json", "$root\uip.out" -ErrorAction SilentlyContinue
Get-Process SpendApp -ErrorAction SilentlyContinue | Stop-Process -Force
Set-Location "$root\$proj"
$a = if (Test-Path "$root\args.json") { "--input-arguments-file $root\args.json" } else { "" }
"run $proj in session $((Get-Process -Id $PID).SessionId) $a"
$p = Start-Process cmd -ArgumentList "/c uip rpa run --file-path Main.xaml --log-level Information $a --output json > $root\uip.out 2>&1" -PassThru -WindowStyle Hidden
$p | Wait-Process -Timeout 300 -ErrorAction SilentlyContinue
Start-Sleep 3
if (-not $p.HasExited) { "(uip did not exit; killed)"; taskkill /T /F /PID $p.Id | Out-Null }
Get-Content "$root\uip.out" | Where-Object { $_ -notmatch '^\s*$' } | Select-Object -Last 40
"--- trace"; Get-Content "$root\trace.log" -ErrorAction SilentlyContinue
"--- app"; Get-Content "$root\app.log" -ErrorAction SilentlyContinue
Get-Process SpendApp -ErrorAction SilentlyContinue | Stop-Process -Force
