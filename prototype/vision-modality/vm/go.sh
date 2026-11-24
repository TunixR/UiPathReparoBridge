#!/bin/bash
# PROTOTYPE helper (host): go.sh <script> <app|none> [scale%] -> sync, run the vision job in the console, fetch out/<script>
P=$(cd "$(dirname "$0")/.." && pwd); F=$P/findings/$1${3:+-$3}
S="ssh -F $HOME/VMs/reparo-win/ssh_config reparo-win"
tar czf /tmp/proto49-src.tgz -C "$P/.." --exclude=findings vision-modality
scp -q -F ~/VMs/reparo-win/ssh_config /tmp/proto49-src.tgz reparo-win:C:/Users/reparo/UiPath/reparo/proto49/src.tgz
$S "cd C:\\Users\\reparo\\UiPath\\reparo\\proto49; tar xzf src.tgz; Set-Content job.txt '$1 $2 $3'" 2>/dev/null
$S "powershell -File C:\\Users\\reparo\\UiPath\\reparo\\proto49\\vision-modality\\vm\\console49.ps1 -Script C:\\Users\\reparo\\UiPath\\reparo\\proto49\\vision-modality\\vm\\vision.ps1 -TimeoutSec 300" 2>&1 | grep -v -e WARNING -e 'This session' -e 'server may' | tee /tmp/proto49-$1.txt
rm -rf "$F"; mkdir -p "$F"; scp -q -r -F ~/VMs/reparo-win/ssh_config "reparo-win:C:/Users/reparo/UiPath/reparo/proto49/out/$1/*" "$F/"; cp /tmp/proto49-$1.txt "$F/console.txt"; scp -q -F ~/VMs/reparo-win/ssh_config "reparo-win:C:/Users/reparo/UiPath/reparo/proto49/app.log" "$F/app.log"
