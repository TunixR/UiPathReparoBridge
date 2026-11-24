# PROTOTYPE job (console session, unelevated): run VisionRun\Main.xaml with uip, TargetApp in front
$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')
$r = 'C:\Users\reparo\UiPath\reparo\proto49'; $out = "$r\out\uipath"
Remove-Item -Recurse -Force $out, "$r\app.log", "$r\rects.json", "$r\uip.out" -EA 0
Get-Process TargetApp -EA 0 | Stop-Process -Force
$shell = New-Object -ComObject Shell.Application; $shell.MinimizeAll(); Start-Sleep 1
Start-Process "$r\bin\TargetApp\TargetApp.exe" -ArgumentList '120 90'
for ($i = 0; $i -lt 30 -and -not (Test-Path "$r\rects.json"); $i++) { Start-Sleep -Milliseconds 300 }
Set-Location "$r\VisionRun"
$p = Start-Process cmd -ArgumentList "/c uip rpa run --file-path Main.xaml --log-level Information --output json > $r\uip.out 2>&1" -PassThru -WindowStyle Hidden
function Done { (Test-Path "$out\probe.log") -and (Select-String -Path "$out\probe.log" -Pattern '=== done' -Quiet) }
for ($i = 0; $i -lt 240 -and -not (Done) -and -not $p.HasExited; $i++) { Start-Sleep 1 }
"waited $i s, uip exited: $($p.HasExited)"
Start-Sleep 4
if (-not $p.HasExited) { "(uip did not exit; killed)"; taskkill /T /F /PID $p.Id | Out-Null }
Get-Content "$r\uip.out" -EA 0 | Where-Object { $_ -notmatch '^\s*$' } | Select-Object -Last 15
Copy-Item "$r\app.log" "$out\app.log" -EA 0
Get-Process TargetApp -EA 0 | Stop-Process -Force
$shell.UndoMinimizeALL()
