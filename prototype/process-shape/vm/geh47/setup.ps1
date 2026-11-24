# RESEARCH helper (runs as SYSTEM through the guest agent): build the probe package, make the GehOrder project.
$g = 'C:\Users\reparo\UiPath\reparo\geh47'
$env:DOTNET_CLI_HOME = "$g\dn"; $env:NUGET_PACKAGES = "$g\nuget"
& 'C:\Program Files\dotnet\dotnet.exe' pack "$g\Bridge\GehOrder.Activities.csproj" -c Release -o "$g\feed" 2>&1 | Select-Object -Last 3
Copy-Item "$g\feed\Reparo.GehOrder.Activities.0.0.1.nupkg" 'C:\Users\reparo\AppData\Local\Programs\UiPath\Studio\Packages\' -Force
$p = "$g\GehOrder"
if (-not (Test-Path "$p\project.json")) {
  New-Item -ItemType Directory -Force $p | Out-Null
  Get-ChildItem C:\Users\reparo\UiPath\reparo\proto46\GehProbe -Force | Where-Object Name -notin '.local','Main.xaml','GlobalHandler.xaml' | Copy-Item -Destination $p -Recurse -Force
}
Copy-Item "$g\src-project\Main.xaml", "$g\src-project\GlobalHandler.xaml" $p -Force
$j = Get-Content "$p\project.json" -Raw | ConvertFrom-Json
$j.name = 'GehOrder'
$j.PSObject.Properties.Remove('exceptionHandlerWorkflow')
$j.designOptions.PSObject.Properties.Remove('exceptionHandlerWorkflow')
$j.runtimeOptions | Add-Member -Force NoteProperty exceptionHandlerWorkflow 'GlobalHandler.xaml'
$j.dependencies.PSObject.Properties.Remove('Reparo.UiPathBridge.Activities')
$j.dependencies | Add-Member -Force NoteProperty 'Reparo.GehOrder.Activities' '[0.0.1]'
$j.entryPoints[0].input = @()
$j | ConvertTo-Json -Depth 20 | Set-Content "$p\project.json" -Encoding UTF8
$u = Get-Content "$p\project.uiproj" -Raw | ConvertFrom-Json; $u.Name = 'GehOrder'
$u | ConvertTo-Json -Depth 5 | Set-Content "$p\project.uiproj" -Encoding UTF8
$e = Get-Content "$p\entry-points.json" -Raw | ConvertFrom-Json; $e.entryPoints[0].input.properties = [pscustomobject]@{}
$e | ConvertTo-Json -Depth 10 | Set-Content "$p\entry-points.json" -Encoding UTF8
icacls $g /grant 'reparo:(OI)(CI)F' /T /Q | Out-Null
"project ready:"; Get-Content "$p\project.uiproj"; ($j.runtimeOptions | ConvertTo-Json)
