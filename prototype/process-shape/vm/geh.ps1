# PROTOTYPE helper: geh.ps1 <project> <none|continue|retry>. Sets or clears the Global Exception Handler in project.uiproj
# (where Studio 27 keeps it; the packer rebuilds project.json from it) and in project.json, and writes geh-mode.txt.
param($Proj, $Mode)
$d = 'C:\Users\reparo\UiPath\reparo\proto46'
foreach ($f in "$d\$Proj\project.uiproj", "$d\$Proj\project.json") {
  $j = Get-Content $f -Raw | ConvertFrom-Json
  $k = if ($f -like '*.uiproj') { 'ExceptionHandlerWorkflow' } else { 'exceptionHandlerWorkflow' }
  $j.PSObject.Properties.Remove($k)
  if ($Mode -ne 'none') { $j | Add-Member -Force NoteProperty $k 'GlobalHandler.xaml' }
  if ($f -like '*.json') {
    $j.designOptions.PSObject.Properties.Remove($k)
    if ($Mode -ne 'none') { $j.designOptions | Add-Member -Force NoteProperty $k 'GlobalHandler.xaml' }
  }
  $j | ConvertTo-Json -Depth 20 | Set-Content $f -Encoding UTF8
}
if ($Mode -eq 'none') { Remove-Item "$d\geh-mode.txt", "$d\$Proj\GlobalHandler.xaml" -ErrorAction SilentlyContinue }
else { Set-Content "$d\geh-mode.txt" $Mode }
"$Proj geh=$Mode"; Get-Content "$d\$Proj\project.uiproj"
