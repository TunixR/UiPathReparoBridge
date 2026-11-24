. C:\Users\reparo\UiPath\reparo\proto48\vm\build-env.ps1
Set-Location C:\Users\reparo\UiPath\reparo\proto48\TypesProbe
& 'C:\Program Files\dotnet\dotnet.exe' restore @src 2>&1 | Select-Object -Last 3
& 'C:\Program Files\dotnet\dotnet.exe' build --no-restore -v q -o bin\out; & "C:\Program Files\dotnet\dotnet.exe" bin\out\TypesProbe.dll 2>&1 | Tee-Object -FilePath ..\types-probe.txt
