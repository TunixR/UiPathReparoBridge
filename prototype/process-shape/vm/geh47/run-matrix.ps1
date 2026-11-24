# RESEARCH helper (runs as reparo on the console desktop, through console.ps1): pack GehOrder once, then run each
# "<scenario> <geh-mode>" line of geh47\matrix.txt with UiRobot execute, writing geh47\logs\<scenario>-<mode>.log.
$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')
$g = 'C:\Users\reparo\UiPath\reparo\geh47'; $p = "$g\GehOrder"; $out = "$g\out"
New-Item -ItemType Directory -Force "$g\logs" | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (-not (Get-ChildItem $out -Filter *.nupkg -ErrorAction SilentlyContinue)) {
  Remove-Item -Recurse -Force "$p\.local" -ErrorAction SilentlyContinue
  "pack GehOrder (session $((Get-Process -Id $PID).SessionId))"
  $k = Start-Process cmd -ArgumentList "/c uip rpa pack $p $out --output json > $g\pack.out 2>&1" -PassThru -WindowStyle Hidden
  for ($i = 0; $i -lt 300 -and -not (Get-ChildItem $out -Filter *.nupkg -ErrorAction SilentlyContinue); $i++) { Start-Sleep 1 }
  Start-Sleep 3
  if (-not $k.HasExited) { taskkill /T /F /PID $k.Id | Out-Null }
  Get-Content "$g\pack.out" | Select-String -Pattern 'Result|rror' | Select-Object -First 5
}
$pkg = Get-ChildItem $out -Filter *.nupkg | Select-Object -First 1
if (-not $pkg) { "no package"; exit 1 }
"package $($pkg.Name)"
$z = [IO.Compression.ZipFile]::OpenRead($pkg.FullName)
$z.Entries | Where-Object { $_.FullName -match '\.(xaml|dll)$' } | ForEach-Object { "  " + $_.FullName }
$e = $z.GetEntry('content/project.json'); $r = New-Object IO.StreamReader($e.Open()); $pj = $r.ReadToEnd() | ConvertFrom-Json; $r.Close()
"  packaged runtimeOptions.exceptionHandlerWorkflow = '$($pj.runtimeOptions.exceptionHandlerWorkflow)'"
foreach ($d in $z.Entries | Where-Object { $_.FullName -match '^lib/.*GehOrder.*\.dll$' }) {
  $s = $d.Open(); $ms = New-Object IO.MemoryStream; $s.CopyTo($ms); $s.Close()
  "  $($d.Name) holds a GlobalHandler type: $([Text.Encoding]::UTF8.GetString($ms.ToArray()).Contains('GlobalHandler'))"
}
$z.Dispose()
$exec = "$env:LOCALAPPDATA\UiPath\Logs\$(Get-Date -Format yyyy-MM-dd)_Execution.log"
foreach ($line in Get-Content "$g\matrix.txt") {
  if (-not $line.Trim()) { continue }
  $sc, $mode = $line.Trim() -split '\s+'
  Set-Content "$g\scenario.txt" $sc; Set-Content "$g\geh-mode.txt" $mode
  Remove-Item "$g\trace.log" -ErrorAction SilentlyContinue
  Get-ChildItem "$env:USERPROFILE\.nuget\packages" -Directory -Filter 'gehorder*' -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
  $before = if (Test-Path $exec) { (Get-Content $exec).Count } else { 0 }
  $t0 = Get-Date
  $o = & "$env:LOCALAPPDATA\Programs\UiPath\Studio\UiRobot.exe" execute --file $pkg.FullName 2>&1
  $rc = $LASTEXITCODE
  $log = "$g\logs\$sc-$mode.log"
  @("# scenario=$sc geh-mode=$mode package=$($pkg.Name) (UiRobot execute --file, console session)", "# UiRobot exit $rc after $([int]((Get-Date) - $t0).TotalSeconds)s", "", "## trace.log (probe activities, FaultTracker, GEH)") | Set-Content $log
  Get-Content "$g\trace.log" -ErrorAction SilentlyContinue | Add-Content $log
  "", "## UiRobot output" | Add-Content $log
  $o | ForEach-Object { "$_" } | Add-Content $log
  "", "## Robot Execution.log lines of this run (message field)" | Add-Content $log
  if (Test-Path $exec) {
    Get-Content $exec | Select-Object -Skip $before | ForEach-Object {
      $i = $_.IndexOf('{'); if ($i -lt 0) { return }
      try { $m = $_.Substring($i) | ConvertFrom-Json; "$($_.Substring(0,$i))$($m.message)" -replace "`r`n", ' | ' } catch { $_.Substring(0, [Math]::Min(300, $_.Length)) }
    } | Add-Content $log
  }
  "ran $sc $mode -> exit $rc"
}
