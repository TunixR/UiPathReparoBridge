$root = 'C:\Users\reparo\UiPath\reparo\proto46'
foreach ($n in 'Probe','SpendingApproval','GehProbe') {
  $f = "$root\$n\project.uiproj"
  if (Test-Path $f) {
    (Get-Content $f -Raw) -replace '"Name": "SmokeNotepad"', "`"Name`": `"Proto46$n`"" | Set-Content $f -Encoding UTF8
    Remove-Item -Recurse -Force "$root\$n\.local" -ErrorAction SilentlyContinue
    "$n " + (Select-String -Path $f -Pattern '"Name"').Line.Trim()
  }
}
