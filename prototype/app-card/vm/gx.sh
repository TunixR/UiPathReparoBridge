#!/bin/bash
# gx.sh <local .ps1> [timeoutSec] [args]: copy a script into proto48\vm and run it as SYSTEM via the guest agent
D=$(dirname "$0"); n=$(basename "$1")
$D/put.sh "$1" "C:\\Users\\reparo\\UiPath\\reparo\\proto48\\vm\\$n" >/dev/null
$D/gexec.sh "& 'C:\\Users\\reparo\\UiPath\\reparo\\proto48\\vm\\$n' ${3:-}" ${2:-120}
