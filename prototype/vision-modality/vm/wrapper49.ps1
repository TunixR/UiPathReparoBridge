$r = 'C:\Users\reparo\UiPath\reparo\proto49'
$job = (Get-Content "$r\console.job" -Raw).Trim()
"session $((Get-Process -Id $PID).SessionId) user $env:USERNAME" | Out-File "$r\console.out"
& $job *>> "$r\console.out"
New-Item "$r\console.done" -ItemType File -Force | Out-Null
