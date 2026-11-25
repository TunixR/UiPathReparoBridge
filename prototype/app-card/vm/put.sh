#!/bin/bash
# put.sh <localfile> <guest path>  -> write a file into the guest via the qemu guest agent
V="virsh -c qemu:///session"
h=$($V qemu-agent-command reparo-win "{\"execute\":\"guest-file-open\",\"arguments\":{\"path\":$(python3 -c 'import json,sys;print(json.dumps(sys.argv[1]))' "$2"),\"mode\":\"wb\"}}" | python3 -c 'import json,sys;print(json.load(sys.stdin)["return"])')
python3 - "$1" "$h" <<'P'
import sys,base64,json,subprocess
data=open(sys.argv[1],'rb').read(); h=int(sys.argv[2])
for i in range(0,len(data),48000):
    c=base64.b64encode(data[i:i+48000]).decode()
    subprocess.run(["virsh","-c","qemu:///session","qemu-agent-command","reparo-win",json.dumps({"execute":"guest-file-write","arguments":{"handle":h,"buf-b64":c}})],check=True,capture_output=True)
P
$V qemu-agent-command reparo-win "{\"execute\":\"guest-file-close\",\"arguments\":{\"handle\":$h}}" >/dev/null && echo "put $2 ($(stat -c%s "$1") bytes)"
