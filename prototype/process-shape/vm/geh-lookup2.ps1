$d = "$env:LOCALAPPDATA\Programs\UiPath\Studio"
foreach ($f in Get-ChildItem $d -Filter "*.dll" -Recurse -Depth 1 | Where-Object { $_.Name -match "Project|Robot|Executor|Workflow" }) {
  $b = [IO.File]::ReadAllBytes($f.FullName)
  $m = @([regex]::Matches([Text.Encoding]::UTF8.GetString($b), "[a-zA-Z]*[eE]xceptionHandler[A-Za-z]*") + [regex]::Matches([Text.Encoding]::Unicode.GetString($b), "[a-zA-Z]*[eE]xceptionHandler[A-Za-z]*")) | ForEach-Object { $_.Value } | Sort-Object -Unique
  if ($m) { "$($f.Name): $($m -join ', ')" }
}
$x = Get-ChildItem "$env:USERPROFILE\.nuget\packages\uipath.system.activities" -Recurse -Filter *.xml | Select-Object -First 3
foreach ($f in $x) { Select-String -Path $f.FullName -Pattern "ExceptionHandlerArgs|ErrorAction" | Select-Object -First 6 | ForEach-Object { $_.Line.Trim() } }
