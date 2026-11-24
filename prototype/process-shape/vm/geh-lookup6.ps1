$d = "$env:APPDATA\npm\node_modules\@uipath\rpa-tool\node_modules\@uipath\studio-helm-win32-x64\tools"
Get-ChildItem $d -File -Filter *.dll | Where-Object { $_.Name -match 'Project|Package|Pack|Executor|Robot|Studio' } | ForEach-Object {
  $b = [IO.File]::ReadAllBytes($_.FullName)
  foreach ($enc in [Text.Encoding]::UTF8, [Text.Encoding]::Unicode) {
    $ms = [regex]::Matches($enc.GetString($b), '[\x20-\x7e]{0,40}[eE]xceptionHandler(Workflow)?[\x20-\x7e]{0,40}')
    $v = $ms | ForEach-Object Value | Sort-Object -Unique | Select-Object -First 8
    if ($v) { "== $($_.Name) $($enc.WebName)"; $v }
  }
}
