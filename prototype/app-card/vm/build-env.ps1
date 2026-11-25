# PROTOTYPE helper: dotnet as SYSTEM, with its own package folder and reparo's NuGet cache as an offline source.
$env:DOTNET_CLI_HOME = 'C:\Users\reparo\UiPath\reparo\proto48\dotnethome'
$env:NUGET_PACKAGES = 'C:\Users\reparo\UiPath\reparo\proto48\nuget'
$env:DOTNET_NOLOGO = '1'; $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$src = '--source', 'C:\Users\reparo\.nuget\packages'
