@echo off
chcp 65001 > nul
cd /d "%~dp0"
echo 달동네 전용 서버를 켭니다. 끌 때는 이 창에 stop 을 치고 Enter 를 누르세요. (명령 목록: help)
"%~dp0CompanyGameServer.exe" -batchmode -nographics -console -gameserver -serversettings "server\settings.json" -logFile "server\server.log"
echo 서버가 꺼졌습니다. 자세한 기록은 server\server.log 에 있습니다.
pause
