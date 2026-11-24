# PROTOTYPE helper: pack proto46\<project> and run the package with UiRobot (the path a Robot job takes).
$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')
$root = 'C:\Users\reparo\UiPath\reparo\proto46'
$proj = (Get-Content "$root\current.txt" -Raw).Trim()
Remove-Item "$root\trace.log", "$root\app.log", "$root\recovery-*.json" -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force "$root\out\$proj" -ErrorAction SilentlyContinue
Get-Process SpendApp -ErrorAction SilentlyContinue | Stop-Process -Force
"pack $proj"
$p = Start-Process cmd -ArgumentList "/c uip rpa pack $root\$proj $root\out\$proj --output json > $root\pack.out 2>&1" -PassThru -WindowStyle Hidden
for ($i = 0; $i -lt 240 -and -not (Get-ChildItem "$root\out\$proj" -Filter *.nupkg -ErrorAction SilentlyContinue); $i++) { Start-Sleep 1 }
Start-Sleep 3
if (-not $p.HasExited) { taskkill /T /F /PID $p.Id | Out-Null }
Get-Content "$root\pack.out" | Select-String -Pattern 'Result|rror' | Select-Object -First 5
$pkg = Get-ChildItem "$root\out\$proj" -Filter *.nupkg | Select-Object -First 1
"package $($pkg.Name)"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$z = [IO.Compression.ZipFile]::OpenRead($pkg.FullName); $z.Entries | Where-Object { $_.FullName -match '\.(xaml|dll)$|project.json' } | ForEach-Object { "  " + $_.FullName }; $z.Dispose()
$t0 = Get-Date
$o = & "$env:LOCALAPPDATA\Programs\UiPath\Studio\UiRobot.exe" execute --file $pkg.FullName 2>&1
"exit $LASTEXITCODE, took $([int]((Get-Date) - $t0).TotalSeconds)s"
$o | Select-Object -First 15
"--- robot log"
Get-ChildItem "$env:LOCALAPPDATA\UiPath\Logs" -Filter *Execution.log -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1 | ForEach-Object { Get-Content $_.FullName -Tail 12 | ForEach-Object { $_.Substring(0, [Math]::Min(400, $_.Length)) } }
"--- trace"; Get-Content "$root\trace.log" -ErrorAction SilentlyContinue
"--- app"; Get-Content "$root\app.log" -ErrorAction SilentlyContinue
Get-Process SpendApp -ErrorAction SilentlyContinue | Stop-Process -Force
