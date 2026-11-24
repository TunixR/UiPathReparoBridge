# PROTOTYPE helper: try dotNetVersion value forms with net10.ps1; print what each pack says.
foreach ($v in '8.0', 'net10.0', 'Net10', '10') {
  "=== dotNetVersion=$v"
  & C:\Users\reparo\UiPath\reparo\proto46\vm\net10.ps1 -Version $v | Select-Object -First 6
}
