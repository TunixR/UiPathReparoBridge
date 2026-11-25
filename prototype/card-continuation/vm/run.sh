#!/bin/bash
# PROTOTYPE helper (host), ReparoHome#67: one run end to end.
#   vm/run.sh <shape> <resize> <scenario> [log name]
# Generates Main.xaml, copies it and the scenario to reparo-win, runs it on the console, fetches the log and shots.
set -e
D=$(cd "$(dirname "$0")/.." && pwd); V=$D/vm
name=${4:-$1-$2-$3}
G='C:\Users\reparo\UiPath\reparo\proto48'
T=/tmp/claude-1000/Main-67.xaml
python3 $D/Process/gen.py $D/Process/header.xaml "$1" "$2" > $T 2>/dev/null
$V/put.sh $T "$G\\Card67\\Main.xaml" >/dev/null
$V/put.sh $D/scenarios/$3.json "$G\\scenarios\\$3.json" >/dev/null
$V/go67.sh $3 > /dev/null
$V/get.sh "$G\\runs\\$3.log" $D/findings/$name.log
mkdir -p $D/findings/shots
for f in $(grep -o 'shot-[0-9]*-[a-z-]*\.png' $D/findings/$name.log | sort -u); do $V/get.sh "$G\\$f" $D/findings/shots/$name-$f; done
cat $D/findings/$name.log
