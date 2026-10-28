#!/bin/bash
# EOS test client (macOS). Closing this window (or Ctrl+C) ends the test too.
cd "$(dirname "$0")" || exit 1
xattr -dr com.apple.quarantine . 2>/dev/null
APP="CompanyGame.app/Contents/MacOS/CompanyGame"
chmod +x "$APP" eos_test.command eos_test_server.command 2>/dev/null
codesign -v CompanyGame.app 2>/dev/null || codesign --force --deep -s - CompanyGame.app 2>/dev/null
read -r -p "서버 PUID를 붙여 넣고 Enter: " PUID
PUID=$(echo "$PUID" | tr -d '[:space:]')
if [ -z "$PUID" ]; then
  echo "PUID가 비어 있습니다."
  read -r -n 1 -p "아무 키나 누르면 닫힙니다."
  exit 1
fi
rm -f eos_test_log.txt
echo "시험 중입니다. 1~2분 걸립니다... (창을 닫으면 시험도 끝납니다)"
"$APP" -batchmode -nographics -eostest client "$PUID" -eostest-log "$PWD/eos_test_log.txt" -logFile "$PWD/unity_client_log.txt" &
CPID=$!
trap 'kill $CPID 2>/dev/null' EXIT
trap 'exit 1' HUP INT TERM
wait $CPID
echo
cat eos_test_log.txt
echo
echo "결과 파일: $PWD/eos_test_log.txt"
echo "이 파일을 보내 주세요."
read -r -n 1 -p "아무 키나 누르면 닫힙니다."
