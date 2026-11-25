# PROTOTYPE helper (ReparoHome#67 (as #66)), run in reparo's console session via console.ps1.
# proto48\current.txt holds "<Project> <scenario>"; scenarios\<scenario>.json, when present, becomes the fake done.
$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')
$root = 'C:\Users\reparo\UiPath\reparo\proto48'
$proj, $scen = (Get-Content "$root\current.txt" -Raw).Trim() -split '\s+'
Remove-Item "$root\trace.log", "$root\app.log", "$root\recovery-*.json", "$root\uip.out", "$root\fake-done.json", "$root\app-cmd.txt", "$root\shot-*.png" -ErrorAction SilentlyContinue
Get-Process SpendApp, msedge -ErrorAction SilentlyContinue | Stop-Process -Force
if (Test-Path "$root\scenarios\$scen.json") { Copy-Item "$root\scenarios\$scen.json" "$root\fake-done.json" }
Set-Location "$root\$proj"
$t0 = Get-Date
$p = Start-Process cmd -ArgumentList "/c uip rpa run --file-path Main.xaml --log-level Information --output json > $root\uip.out 2>&1" -PassThru -WindowStyle Hidden
# uip never returns under the console helper after a run: wait for the Process to end in the trace, then kill it.
for ($i = 0; $i -lt 240; $i++) {
  Start-Sleep 1
  if ($p.HasExited) { break }
  if ((Test-Path "$root\trace.log") -and (Select-String -Path "$root\trace.log" -Pattern 'Process completed|outer Try Catch caught' -Quiet)) { Start-Sleep 4; break }
}
if (-not $p.HasExited) { taskkill /T /F /PID $p.Id | Out-Null }
$log = "$root\runs\$scen.log"
New-Item -ItemType Directory -Force "$root\runs" | Out-Null
& {
  "### $proj / $scen  ($(Get-Date -Format s), $([int]((Get-Date) - $t0).TotalSeconds) s)"
  if (Test-Path "$root\scenarios\$scen.json") { "--- fake done"; Get-Content "$root\scenarios\$scen.json" }
  "--- trace"; Get-Content "$root\trace.log" -ErrorAction SilentlyContinue
  "--- app"; Get-Content "$root\app.log" -ErrorAction SilentlyContinue
  "--- screenshots"; Get-ChildItem "$root\shot-*.png" -ErrorAction SilentlyContinue | ForEach-Object { $_.Name }
  "--- recovery-1.json"; Get-Content "$root\recovery-1.json" -ErrorAction SilentlyContinue
  "--- uip (errors)"; Get-Content "$root\uip.out" -ErrorAction SilentlyContinue | Select-String -Pattern 'rror|xception|Result' | Select-Object -First 12 | ForEach-Object { $_.Line.Substring(0, [Math]::Min(500, $_.Line.Length)) }
} | Set-Content $log
Get-Content $log
Get-Process SpendApp, msedge -ErrorAction SilentlyContinue | Stop-Process -Force
