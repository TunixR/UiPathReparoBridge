#!/bin/bash
# gexec.sh "<powershell command>"  -> run via qemu guest agent (as SYSTEM), print stdout/stderr
V="virsh -c qemu:///session"
cmd=$(python3 -c 'import json,sys; print(json.dumps({"execute":"guest-exec","arguments":{"path":"powershell.exe","arg":["-NoProfile","-Command",sys.argv[1]],"capture-output":True}}))' "$1")
pid=$($V qemu-agent-command reparo-win "$cmd" | python3 -c 'import json,sys; print(json.load(sys.stdin)["return"]["pid"])')
for i in $(seq 1 ${2:-60}); do
  r=$($V qemu-agent-command reparo-win "{\"execute\":\"guest-exec-status\",\"arguments\":{\"pid\":$pid}}")
  if echo "$r" | grep -q '"exited":true'; then
    echo "$r" | python3 -c 'import json,sys,base64; r=json.load(sys.stdin)["return"]; print(base64.b64decode(r.get("out-data","")).decode("utf-8","replace")); print(base64.b64decode(r.get("err-data","")).decode("utf-8","replace")); print("exit",r.get("exitcode"))'
    exit 0
  fi
  sleep 1
done
echo timeout
