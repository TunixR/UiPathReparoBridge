# PROTOTYPE helper (ReparoHome#66), run as SYSTEM through the guest agent: build the Bridge package
# and make the two UiPath projects from #46's SpendingApproval project (SpendApp is #48's build, in proto48\app).
. C:\Users\reparo\UiPath\reparo\proto48\vm\build-env.ps1
$d = 'C:\Users\reparo\UiPath\reparo\proto48'
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
Set-Location "$d\Bridge"
& $dotnet pack -c Release -o "$d\feed" @src -v q 2>&1 | Where-Object { $_ -match 'error|Successfully|warn CS' } | Select-Object -First 15
Copy-Item "$d\feed\Reparo.UiPathBridge.Activities.0.0.1-proto.66.2.nupkg" "C:\Users\reparo\AppData\Local\Programs\UiPath\Studio\Packages\" -Force
Remove-Item -Recurse -Force 'C:\Users\reparo\.nuget\packages\reparo.uipathbridge.activities\0.0.1-proto.66.2' -ErrorAction SilentlyContinue
foreach ($n in 'CardHolds', 'CardInside', 'CardBrowser') {
  robocopy 'C:\Users\reparo\UiPath\reparo\proto46\SpendingApproval' "$d\$n" /E /XD .local /XF Main.xaml GlobalHandler.xaml SpendApp.* /NFL /NDL /NJH /NJS | Out-Null
  $f = "$d\$n\project.uiproj"; (Get-Content $f -Raw) -replace 'Proto46SpendingApproval', "Proto66$n" | Set-Content $f -Encoding UTF8
  $j = Get-Content "$d\$n\project.json" -Raw | ConvertFrom-Json
  $j.name = "Proto66$n"
  $j.dependencies.'Reparo.UiPathBridge.Activities' = '[0.0.1-proto.66.2]'
  $j.entryPoints[0].input = @($j.entryPoints[0].input | Where-Object { $_.name -eq 'app_args' })
  $j | ConvertTo-Json -Depth 20 | Set-Content "$d\$n\project.json" -Encoding UTF8
  "$n : " + (Select-String -Path "$d\$n\project.uiproj" -Pattern '"Name"').Line.Trim() + " deps " + ($j.dependencies | ConvertTo-Json -Compress)
}
Get-ChildItem "$d\feed", "$d\app\SpendApp.exe" | Select-Object Name, Length
