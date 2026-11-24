#!/bin/bash
# get.sh <guest path> <localfile>
V="virsh -c qemu:///session"
h=$($V qemu-agent-command reparo-win "{\"execute\":\"guest-file-open\",\"arguments\":{\"path\":$(python3 -c 'import json,sys;print(json.dumps(sys.argv[1]))' "$1"),\"mode\":\"rb\"}}" | python3 -c 'import json,sys;print(json.load(sys.stdin)["return"])') || exit 1
python3 - "$2" "$h" <<'P'
import sys,base64,json,subprocess
h=int(sys.argv[2]); out=open(sys.argv[1],'wb')
while True:
    r=json.loads(subprocess.run(["virsh","-c","qemu:///session","qemu-agent-command","reparo-win",json.dumps({"execute":"guest-file-read","arguments":{"handle":h,"count":48000}})],check=True,capture_output=True,text=True).stdout)["return"]
    out.write(base64.b64decode(r.get("buf-b64","")))
    if r.get("eof") or r.get("count",0)==0: break
P
$V qemu-agent-command reparo-win "{\"execute\":\"guest-file-close\",\"arguments\":{\"handle\":$h}}" >/dev/null
