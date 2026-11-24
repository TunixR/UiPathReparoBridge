#!/bin/bash
# PROTOTYPE helper (host): deploy.sh <plain|trycatch|retryscope> <none|continue|retry>  -> SpendingApproval on the VM
set -e
P=$(dirname "$0")/..
S="ssh -F $HOME/VMs/reparo-win/ssh_config reparo-win"
python3 $P/Process/gen.py $P/Process/header.xaml $1 > $P/Process/SpendingApproval/Main.xaml
scp -q -F ~/VMs/reparo-win/ssh_config $P/Process/SpendingApproval/Main.xaml $P/Process/GlobalHandler.xaml reparo-win:C:/Users/reparo/UiPath/reparo/proto46/SpendingApproval/
if [ "$2" = none ]; then
  $S '$d="C:\Users\reparo\UiPath\reparo\proto46"; Remove-Item $d\geh-mode.txt -EA SilentlyContinue; Remove-Item $d\SpendingApproval\GlobalHandler.xaml; $j=Get-Content $d\SpendingApproval\project.json -Raw | ConvertFrom-Json; $j.PSObject.Properties.Remove("exceptionHandlerWorkflow"); $j | ConvertTo-Json -Depth 20 | Set-Content $d\SpendingApproval\project.json' 2>/dev/null
else
  $S "\$d='C:\Users\reparo\UiPath\reparo\proto46'; Set-Content \$d\geh-mode.txt '$2'; \$j=Get-Content \$d\SpendingApproval\project.json -Raw | ConvertFrom-Json; \$j | Add-Member -Force NoteProperty exceptionHandlerWorkflow 'GlobalHandler.xaml'; \$j | ConvertTo-Json -Depth 20 | Set-Content \$d\SpendingApproval\project.json" 2>/dev/null
fi
echo "deployed $1 geh=$2"
