# PROTOTYPE helper: run proto46\<project>\project.json with UiRobot, skipping the packer.
$root = 'C:\Users\reparo\UiPath\reparo\proto46'
$proj = (Get-Content "$root\current.txt" -Raw).Trim()
Remove-Item "$root\trace.log", "$root\app.log", "$root\recovery-*.json" -ErrorAction SilentlyContinue
Get-Process SpendApp -ErrorAction SilentlyContinue | Stop-Process -Force
$t0 = Get-Date
$o = & "$env:LOCALAPPDATA\Programs\UiPath\Studio\UiRobot.exe" execute --file "$root\$proj\project.json" 2>&1
"exit $LASTEXITCODE, took $([int]((Get-Date) - $t0).TotalSeconds)s"
$o | Select-Object -First 15 | ForEach-Object { "$_".Substring(0, [Math]::Min(300, "$_".Length)) }
"--- trace"; Get-Content "$root\trace.log" -ErrorAction SilentlyContinue
"--- app"; Get-Content "$root\app.log" -ErrorAction SilentlyContinue
Get-Process SpendApp -ErrorAction SilentlyContinue | Stop-Process -Force
