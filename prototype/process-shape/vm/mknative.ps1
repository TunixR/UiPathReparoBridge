# PROTOTYPE helper: make proto46\Native, a copy of SpendingApproval for the plain-UiPath version of the Process.
$d = 'C:\Users\reparo\UiPath\reparo\proto46'
if (-not (Test-Path "$d\Native")) {
  robocopy "$d\SpendingApproval" "$d\Native" /E /XD .local /NFL /NDL /NJH /NJS | Out-Null
  $f = "$d\Native\project.uiproj"; (Get-Content $f -Raw) -replace 'Proto46SpendingApproval', 'Proto46Native' | Set-Content $f -Encoding UTF8
  $f = "$d\Native\project.json"; (Get-Content $f -Raw) -replace 'Proto46SpendingApproval', 'Proto46Native' | Set-Content $f -Encoding UTF8
}
Remove-Item "$d\Native\GlobalHandler.xaml" -ErrorAction SilentlyContinue
Set-Content "$d\args.json" '{"app_args": "--late-approve 999"}'
"Native ready"
