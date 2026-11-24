Remove-Item C:\Users\reparo\UiPath\reparo\proto46\args.json, C:\Users\reparo\UiPath\reparo\proto46\prestart.txt -ErrorAction SilentlyContinue
$d = "$env:LOCALAPPDATA\Programs\UiPath\Studio"
foreach ($f in Get-ChildItem $d -Filter "UiPath.Studio*.dll") {
  $t = [Text.Encoding]::Unicode.GetString([IO.File]::ReadAllBytes($f.FullName))
  $m = [regex]::Matches($t, "[A-Za-z]*[Ee]xceptionHandler[A-Za-z]*") | ForEach-Object { $_.Value } | Sort-Object -Unique
  if ($m) { "$($f.Name): $($m -join ', ')" }
}
$lib = Get-ChildItem "$env:USERPROFILE\.nuget\packages\uipath.system.activities.runtime.windows" -Recurse -Filter UiPath.System.Activities.dll | Select-Object -First 1
"lib: $($lib.FullName)"
$t = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($lib.FullName))
[regex]::Matches($t, "(ExceptionHandlerArgs|ErrorAction|ErrorInfo)[A-Za-z]*") | ForEach-Object { $_.Value } | Sort-Object -Unique
