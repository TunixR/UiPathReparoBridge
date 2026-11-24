# RESEARCH helper (SYSTEM, guest agent): decompile a Studio DLL with ilspycmd into geh47\src\<name> and zip it (kept on the VM, not committed).
param($Dll = 'UiPath.Executor.Core.dll')
$o = "C:\Users\reparo\UiPath\reparo\geh47\src\$($Dll -replace '\.dll$','')"
Remove-Item -Recurse -Force $o -ErrorAction SilentlyContinue; New-Item -ItemType Directory -Force $o | Out-Null
$env:DOTNET_CLI_HOME = 'C:\Users\reparo\UiPath\reparo\geh47\dn'
& C:\Users\reparo\UiPath\reparo\geh47\tools\ilspycmd.cmd -p -o $o "C:\Users\reparo\AppData\Local\Programs\UiPath\Studio\$Dll" 2>&1 | Select-Object -Last 3
$n = (Get-ChildItem $o -Recurse -Filter *.cs).Count; "$n files"
Get-ChildItem $o -Recurse -Filter *.cs | Select-String -Pattern 'ExceptionHandler|ErrorAction\.|GlobalHandler' -List | ForEach-Object { $_.Path.Replace($o,'') }
Compress-Archive -Force -Path "$o\*" -DestinationPath "$o.zip"
