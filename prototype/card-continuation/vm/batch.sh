#!/bin/bash
# PROTOTYPE helper (host), ReparoHome#67: every run of findings/NOTES.md, one after another.
V=$(dirname "$0")
while read -r shape resize scen; do
  [ -z "$shape" ] && continue
  echo "=== $shape-$resize-$scen"; $V/run.sh $shape $resize $scen > /dev/null 2>&1 || echo "  run failed"
done <<'L'
holds None to-e1
attach None to-e2
attach None to-e1
attach Maximize unmax-to-e2
attach Maximize unmax-retry
foreach None retry
foreach None to-e1-pass0
incard None retry
incard None to-e1-pass0
nested None retry
nested None to-e1
L
