# PROTOTYPE helper: pack a .NET 10 copy of SpendingApproval (proto46\Net10) and show which framework the package targets.
param($Version = '10.0')
$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')
$d = 'C:\Users\reparo\UiPath\reparo\proto46'
Remove-Item -Recurse -Force "$d\Net10", "$d\out\Net10" -ErrorAction SilentlyContinue
robocopy "$d\SpendingApproval" "$d\Net10" /E /XD .local /NFL /NDL /NJH /NJS | Out-Null
Remove-Item "$d\Net10\GlobalHandler.xaml" -ErrorAction SilentlyContinue
$f = "$d\Net10\project.uiproj"; (Get-Content $f -Raw) -replace 'Proto46SpendingApproval', 'Proto46Net10' | Set-Content $f -Encoding UTF8
$f = "$d\Net10\project.json"; $j = Get-Content $f -Raw | ConvertFrom-Json
$j.name = 'Proto46Net10'; $j.PSObject.Properties.Remove('exceptionHandlerWorkflow'); $j | Add-Member -Force NoteProperty dotNetVersion $Version
$j | ConvertTo-Json -Depth 20 | Set-Content $f -Encoding UTF8
$p = Start-Process cmd -ArgumentList "/c uip rpa pack $d\Net10 $d\out\Net10 --output json > $d\pack10.out 2>&1" -PassThru -WindowStyle Hidden
for ($i = 0; $i -lt 240 -and -not $p.HasExited -and -not (Get-ChildItem "$d\out\Net10" -Filter *.nupkg -ErrorAction SilentlyContinue); $i++) { Start-Sleep 1 }
Start-Sleep 3; if (-not $p.HasExited) { taskkill /T /F /PID $p.Id | Out-Null }
Get-Content "$d\pack10.out" | Select-String -Pattern '"Result"|rror|ERR|DotNet|10\.0' | Select-Object -First 8 | ForEach-Object { "$_".Substring(0, [Math]::Min(300, "$_".Length)) }
$pkg = Get-ChildItem "$d\out\Net10" -Filter *.nupkg -ErrorAction SilentlyContinue | Select-Object -First 1
if ($pkg) {
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  $z = [IO.Compression.ZipFile]::OpenRead($pkg.FullName); $z.Entries | Where-Object { $_.FullName -match '^lib/' } | ForEach-Object { "  " + $_.FullName }
  $e = $z.Entries | Where-Object FullName -eq 'content/project.json'; $r = New-Object IO.StreamReader($e.Open()); $pj = $r.ReadToEnd(); $r.Close()
  $e = $z.Entries | Where-Object FullName -like '*.nuspec'; $r = New-Object IO.StreamReader($e.Open()); $ns = $r.ReadToEnd(); $r.Close(); $z.Dispose()
  $pj -split "`n" | Select-String 'dotNet|robotVersion|targetFramework'
  [regex]::Matches($ns, '<group targetFramework="[^"]+"|<dependency id="Reparo[^>]+') | ForEach-Object Value
}
