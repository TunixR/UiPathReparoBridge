#!/bin/bash
# PROTOTYPE helper (host): deploy.sh <plain|trycatch|retryscope> <none|continue|retry>  -> SpendingApproval on the VM
set -e
P=$(dirname "$0")/..
S="ssh -F $HOME/VMs/reparo-win/ssh_config reparo-win"
python3 $P/Process/gen.py $P/Process/header.xaml $1 > $P/Process/SpendingApproval/Main.xaml
scp -q -F ~/VMs/reparo-win/ssh_config $P/Process/SpendingApproval/Main.xaml $P/Process/GlobalHandler.xaml reparo-win:C:/Users/reparo/UiPath/reparo/proto46/SpendingApproval/
$S "powershell -File C:\Users\reparo\UiPath\reparo\proto46\vm\geh.ps1 SpendingApproval $2" 2>/dev/null
echo "deployed $1 geh=$2"
