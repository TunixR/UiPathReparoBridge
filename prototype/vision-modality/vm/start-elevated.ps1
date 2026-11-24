# PROTOTYPE helper (over SSH, elevated): start TargetApp elevated in the console session
$r = 'C:\Users\reparo\UiPath\reparo\proto49'
Get-Process TargetApp -EA 0 | Stop-Process -Force; Remove-Item "$r\app.log", "$r\rects.json" -EA 0
schtasks /Create /F /TN ReparoElevated49 /SC ONCE /ST 00:00 /RU reparo /IT /RL HIGHEST /TR "$r\bin\TargetApp\TargetApp.exe 120 90 --tag elevated" | Out-Null
schtasks /Run /TN ReparoElevated49 | Out-Null
for ($i = 0; $i -lt 30 -and -not (Test-Path "$r\rects.json"); $i++) { Start-Sleep -Milliseconds 300 }
Get-Content "$r\app.log"
