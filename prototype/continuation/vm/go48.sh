#!/bin/bash
# PROTOTYPE helper (host): go48.sh <Project> <scenario>  -> one console run on reparo-win via the guest agent
SP=$(dirname "$0")
$SP/gexec.sh "Set-Content C:\\Users\\reparo\\UiPath\\reparo\\proto48\\current.txt '$1 $2'; & C:\\Users\\reparo\\UiPath\\reparo\\console.ps1 -Script C:\\Users\\reparo\\UiPath\\reparo\\proto48\\vm\\run48.ps1 -TimeoutSec 300" 330
