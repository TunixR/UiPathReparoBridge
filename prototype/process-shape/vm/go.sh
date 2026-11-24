#!/bin/bash
# PROTOTYPE helper (host): go.sh <project> [console]  -> runs the project on the VM
S="ssh -F $HOME/VMs/reparo-win/ssh_config reparo-win"
$S "Set-Content C:\\Users\\reparo\\UiPath\\reparo\\proto46\\current.txt '$1'" 2>/dev/null
if [ "$2" = console ]; then
  $S 'powershell -File C:\Users\reparo\UiPath\reparo\console.ps1 -Script C:\Users\reparo\UiPath\reparo\proto46\vm\run.ps1 -TimeoutSec 420' 2>&1 | grep -v -e WARNING -e 'This session' -e 'server may'
else
  $S 'powershell -File C:\Users\reparo\UiPath\reparo\proto46\vm\run.ps1' 2>&1 | grep -v -e WARNING -e 'This session' -e 'server may'
fi
