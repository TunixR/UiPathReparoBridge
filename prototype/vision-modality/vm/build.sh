#!/bin/bash
# PROTOTYPE helper (host): build.sh <project>... -> sync the source and dotnet build each on the VM
P=$(cd "$(dirname "$0")/.." && pwd)
tar czf /tmp/proto49-src.tgz -C "$P/.." --exclude=findings vision-modality
scp -q -F ~/VMs/reparo-win/ssh_config /tmp/proto49-src.tgz reparo-win:C:/Users/reparo/UiPath/reparo/proto49/src.tgz
ssh -F ~/VMs/reparo-win/ssh_config reparo-win "cd C:\\Users\\reparo\\UiPath\\reparo\\proto49; tar xzf src.tgz" 2>/dev/null
for p in "$@"; do
  ssh -F ~/VMs/reparo-win/ssh_config reparo-win "\$env:Path=[Environment]::GetEnvironmentVariable('Path','Machine'); cd C:\\Users\\reparo\\UiPath\\reparo\\proto49; tar xzf src.tgz; \$env:DOTNET_NOLOGO=1; \"== $p\"; dotnet build vision-modality\\$p --no-incremental -c Release -o bin\\$p -v q 2>&1 | Select-String -Pattern 'error|Elapsed' | Select -Unique -First 12" 2>&1 | grep -v -e WARNING -e 'This session' -e 'server may'
done
