$f = "$env:LOCALAPPDATA\Programs\UiPath\Studio\es\UiPath.Studio.Plugin.Workflow.resources.dll"
$b = [IO.File]::ReadAllBytes($f)
foreach ($enc in [Text.Encoding]::UTF8, [Text.Encoding]::Unicode) {
  $t = $enc.GetString($b)
  $i = $t.IndexOf('x:Property Name="errorInfo"')
  while ($i -ge 0) {
    $s = $t.LastIndexOf('<Activity ', $i); $e = $t.IndexOf('</Activity>', $i)
    "===== at $i ($($enc.EncodingName))"; $t.Substring($s, $e - $s + 11)
    $i = $t.IndexOf('x:Property Name="errorInfo"', $i + 10)
  }
}
