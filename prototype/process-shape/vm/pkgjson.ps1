# PROTOTYPE helper: print content/project.json from proto46\out\<project>\*.nupkg
param($Proj)
Add-Type -AssemblyName System.IO.Compression.FileSystem
$pkg = Get-ChildItem "C:\Users\reparo\UiPath\reparo\proto46\out\$Proj" -Filter *.nupkg | Select-Object -First 1
$z = [IO.Compression.ZipFile]::OpenRead($pkg.FullName)
$e = $z.Entries | Where-Object FullName -eq 'content/project.json'
$r = New-Object IO.StreamReader($e.Open()); $r.ReadToEnd(); $r.Close(); $z.Dispose()
