# PROTOTYPE helper: run C:\Users\reparo\UiPath\reparo\proto46\<project>\Main.xaml; project name read from proto46\current.txt
$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')
$root = 'C:\Users\reparo\UiPath\reparo\proto46'
$proj = (Get-Content "$root\current.txt" -Raw).Trim()
Remove-Item "$root\trace.log" -ErrorAction SilentlyContinue
Set-Location "$root\$proj"
"run $proj in session $((Get-Process -Id $PID).SessionId)"
cmd /c "uip rpa run --file-path Main.xaml --output json 2>&1"
"--- trace"
Get-Content "$root\trace.log" -ErrorAction SilentlyContinue
