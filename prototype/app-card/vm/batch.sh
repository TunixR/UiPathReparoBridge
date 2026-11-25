#!/bin/bash
# PROTOTYPE helper (host), ReparoHome#66: every scenario of findings/NOTES.md, one after another.
V=$(dirname "$0")
while read -r shape close at ov hold scen; do
  [ -z "$shape" ] && continue
  name="$shape-$close-$at-$scen"; [ "$ov" != - ] && name="$name-override$ov"; [ "$hold" != 0 ] && name="$name-hold$hold"
  echo "=== $name"; $V/run.sh $shape $close $at $ov $hold $scen $name > /dev/null 2>&1 || echo "  run failed"
done <<'L'
holds default Event - 0 back-same-activity
holds default Event - 0 back-other-activity
holds default Event - 0 forward-in-card
holds default Event - 0 out-of-card
holds default Event - 0 reject
holds default Event - 0 finished
holds always Event - 0 retry
holds default Process - 0 retry
inside default Event - 0 retry
inside default Event - 3000 retry
inside default Event Never 0 retry
inside always Event Never 0 retry
L
