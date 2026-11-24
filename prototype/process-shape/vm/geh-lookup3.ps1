$d = "$env:LOCALAPPDATA\Programs\UiPath\Studio"
foreach ($f in Get-ChildItem $d -Recurse -Include *.dll,*.xaml,*.nupkg,*.zip -ErrorAction SilentlyContinue | Where-Object { $_.Length -lt 60MB }) {
  $b = [IO.File]::ReadAllBytes($f.FullName)
  foreach ($enc in [Text.Encoding]::UTF8, [Text.Encoding]::Unicode) {
    $t = $enc.GetString($b)
    $m = [regex]::Matches($t, 'x:Property Name="(errorInfo|result)" Type="[^"]+"')
    if ($m.Count) { "$($f.FullName): $(($m | ForEach-Object { $_.Value } | Sort-Object -Unique) -join ' | ')"; break }
  }
}
