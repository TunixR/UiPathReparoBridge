# PROTOTYPE helper (over SSH): pack the Bridge, put it in Studio's local feed, and make the VisionRun project
$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')
$r = 'C:\Users\reparo\UiPath\reparo\proto49'; $env:DOTNET_NOLOGO = 1
dotnet pack "$r\vision-modality\Bridge" -c Release -o "$r\feed" -v q 2>&1 | Select-String 'error|nupkg'
Copy-Item "$r\feed\Reparo.UiPathBridge.Activities.0.0.1-vision.1.nupkg" "$env:LOCALAPPDATA\Programs\UiPath\Studio\Packages\" -Force
Get-ChildItem "$env:USERPROFILE\.nuget\packages\reparo.uipathbridge.activities" -Directory -Filter '0.0.1-vision*' -EA 0 | Remove-Item -Recurse -Force
$p = "$r\VisionRun"; New-Item -ItemType Directory -Force $p | Out-Null
foreach ($f in 'project.json', 'project.uiproj', 'entry-points.json', 'bindings_v2.json') { Copy-Item "$r\..\proto46\Probe\$f" $p -Force }
Copy-Item "$r\vision-modality\VisionRun\Main.xaml" $p -Force
(Get-Content "$p\project.json" -Raw) -replace '"Proto46Probe"', '"Proto49Vision"' -replace '0\.0\.1-proto\.2', '0.0.1-vision.1' -replace '6d2778d0-87f8-4373-bc7a-ef910a930490', '6d2778d0-87f8-4373-bc7a-ef910a930449' | Set-Content "$p\project.json" -Encoding UTF8
(Get-Content "$p\project.uiproj" -Raw) -replace 'Proto46Probe', 'Proto49Vision' | Set-Content "$p\project.uiproj" -Encoding UTF8
Remove-Item -Recurse -Force "$p\.local" -EA 0
Select-String -Path "$p\project.json" -Pattern 'Reparo|"name"' | % { $_.Line.Trim() }
