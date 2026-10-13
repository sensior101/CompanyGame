#!/bin/bash
# EOS test server (macOS). Closing this window (or Ctrl+C, or any key) ends the server too.
cd "$(dirname "$0")" || exit 1
xattr -dr com.apple.quarantine . 2>/dev/null
APP="CompanyGame.app/Contents/MacOS/CompanyGame"
chmod +x "$APP" eos_test.command eos_test_server.command 2>/dev/null
codesign -v CompanyGame.app 2>/dev/null || codesign --force --deep -s - CompanyGame.app 2>/dev/null
rm -f eos_test_log.txt
echo "EOS 시험 서버를 켭니다. 잠시 기다려 주세요..."
"$APP" -batchmode -nographics -eostest server -eostest-minutes 60 -eostest-log "$PWD/eos_test_log.txt" -logFile "$PWD/unity_server_log.txt" &
SPID=$!
trap 'kill $SPID 2>/dev/null' EXIT
trap 'exit 1' HUP INT TERM

PUID=
for i in $(seq 90); do
  sleep 1
  kill -0 $SPID 2>/dev/null || break
  grep -q FAILED eos_test_log.txt 2>/dev/null && break
  if grep -q "SESSION=" eos_test_log.txt 2>/dev/null; then
    PUID=$(grep -m1 " PUID=" eos_test_log.txt | sed 's/.* PUID=//; s/ .*//')
    break
  fi
done
if [ -z "$PUID" ]; then
  echo
  echo "서버를 켜지 못했습니다. eos_test_log.txt 와 unity_server_log.txt 를 보내 주세요."
  cat eos_test_log.txt 2>/dev/null
  read -r -n 1 -p "아무 키나 누르면 닫힙니다."
  exit 1
fi
echo
echo "============================================================"
echo
echo "    서버 PUID:  $PUID"
echo
echo "============================================================"
echo " 위 PUID를 팀원에게 알려 주세요. (eos_test_log.txt 에도 있습니다)"
echo " 서버는 60분 동안 켜져 있습니다. 끝내려면 아무 키를 누르거나 이 창을 닫으세요."
while kill -0 $SPID 2>/dev/null; do
  read -r -s -n 1 -t 1 && break
done
kill $SPID 2>/dev/null
wait $SPID 2>/dev/null
echo "서버를 껐습니다. eos_test_log.txt 를 보내 주세요."
