#!/bin/bash
# PROTOTYPE helper (host), ReparoHome#66: one scenario end to end.
#   vm/run.sh <holds|inside> <default|always> <Event|Process> <CloseOverride|-> <TrackHoldMs> <scenario> [log name]
# Generates Main.xaml, copies it and the scenario to reparo-win, runs it on the console, fetches the log and shots.
set -e
D=$(cd "$(dirname "$0")/.." && pwd); V=$D/vm
proj=$(case "$1" in holds) echo CardHolds;; inside) echo CardInside;; browser-*) echo CardBrowser;; esac)
name=${7:-$1-$2-$3-$6}
G='C:\Users\reparo\UiPath\reparo\proto48'
python3 $D/Process/gen.py $D/Process/header.xaml "$1" "$2" "$3" "$4" "$5" > /tmp/claude-1000/Main-66.xaml 2>/dev/null
$V/put.sh /tmp/claude-1000/Main-66.xaml "$G\\$proj\\Main.xaml" >/dev/null
$V/put.sh $D/scenarios/$6.json "$G\\scenarios\\$6.json" >/dev/null
$V/go66.sh $proj $6 > /dev/null
$V/get.sh "$G\\runs\\$6.log" $D/findings/$name.log
mkdir -p $D/findings/shots
for f in $(grep -o 'shot-[0-9]*-[a-z-]*\.png' $D/findings/$name.log | sort -u); do $V/get.sh "$G\\$f" $D/findings/shots/$name-$f; done
cat $D/findings/$name.log
