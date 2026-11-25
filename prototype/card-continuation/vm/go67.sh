#!/bin/bash
# PROTOTYPE helper (host): go67.sh <scenario>  -> one console run of Card67 on reparo-win via the guest agent
SP=$(dirname "$0")
$SP/gexec.sh "Set-Content C:\\Users\\reparo\\UiPath\\reparo\\proto48\\current.txt 'Card67 $1'; & C:\\Users\\reparo\\UiPath\\reparo\\console.ps1 -Script C:\\Users\\reparo\\UiPath\\reparo\\proto48\\vm\\run67.ps1 -TimeoutSec 300" 330
