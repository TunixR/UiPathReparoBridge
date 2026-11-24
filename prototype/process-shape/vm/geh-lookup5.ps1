$f = Get-ChildItem "$env:LOCALAPPDATA\Programs\UiPath\Studio" -Recurse -Filter UiPath.Project.dll | Select-Object -First 1
"file: $($f.FullName)"
$b = [IO.File]::ReadAllBytes($f.FullName)
foreach ($enc in [Text.Encoding]::UTF8, [Text.Encoding]::Unicode) {
  $t = $enc.GetString($b)
  foreach ($m in [regex]::Matches($t, '[\x20-\x7e]{0,60}[eE]xceptionHandler[\x20-\x7e]{0,60}')) { $enc.WebName + ": " + $m.Value }
}
$e = Get-ChildItem "$env:LOCALAPPDATA\Programs\UiPath\Studio" -Recurse -Filter UiPath.Executor.Core.dll | Select-Object -First 1
$t = [Text.Encoding]::Unicode.GetString([IO.File]::ReadAllBytes($e.FullName))
foreach ($m in [regex]::Matches($t, '[\x20-\x7e]{0,80}[eE]xceptionHandler[\x20-\x7e]{0,80}')) { "executor: " + $m.Value }
