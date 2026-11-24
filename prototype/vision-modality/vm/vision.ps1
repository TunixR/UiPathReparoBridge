# PROTOTYPE job (console session, unelevated): proto49\job.txt = "<script> <app|none> [scale%]"
$r = 'C:\Users\reparo\UiPath\reparo\proto49'; $bin = "$r\bin"
$script, $app, $scale = (Get-Content "$r\job.txt" -Raw).Trim().Split(' ')
$out = "$r\out\$script"; Remove-Item -Recurse -Force $out -EA 0; New-Item -ItemType Directory -Force $out | Out-Null
if ($app -eq 'app') { Remove-Item "$r\app.log", "$r\rects.json" -EA 0; Get-Process TargetApp -EA 0 | Stop-Process -Force }
$shell = New-Object -ComObject Shell.Application; if ($app -eq "app") { $shell.MinimizeAll(); Start-Sleep 1 }
if ($scale) { & "$bin\Probe\ProbeCli.exe" scale $scale; Start-Sleep 3 }
if ($app -eq 'app') {
  Start-Process "$bin\TargetApp\TargetApp.exe" -ArgumentList '120 90'
  for ($i = 0; $i -lt 30 -and -not (Test-Path "$r\rects.json"); $i++) { Start-Sleep -Milliseconds 300 }
  Start-Sleep 1
}
& "$bin\Probe\ProbeCli.exe" run "$r\vision-modality\scripts\$script.json" $out
"--- probe.log"; Get-Content "$out\probe.log"
Copy-Item "$r\app.log" "$out\app.log" -EA 0
if ($scale) { & "$bin\Probe\ProbeCli.exe" scale 100 }
Get-Process TargetApp -EA 0 | Stop-Process -Force
if ($app -eq "app") { $shell.UndoMinimizeALL() }
