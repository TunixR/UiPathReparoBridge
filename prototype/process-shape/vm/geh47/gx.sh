#!/bin/bash
# RESEARCH helper (host): gx.sh <local.ps1> [timeoutSec] [args] -> copy the script to geh47\vm and run it as SYSTEM
D=$(dirname "$0"); n=$(basename "$1")
$D/put.sh "$1" "C:\\Users\\reparo\\UiPath\\reparo\\geh47\\vm\\$n" >/dev/null
$D/gexec.sh "& 'C:\\Users\\reparo\\UiPath\\reparo\\geh47\\vm\\$n' ${3:-}" ${2:-120}
