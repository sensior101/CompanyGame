CompanyGame 멀티 (공개 서버 목록 / 서버 코드로 입장)

모드 세 가지 (타이틀 메뉴)
- 혼자 하기: 나 혼자. 다른 사람은 못 들어옵니다.
- 서버 열기: 게임이 켜져 있는 동안 친구가 들어옵니다. 게임을 끄면 저장하고 서버도 꺼집니다.
- 전용 서버: 게임을 꺼도 PC가 켜져 있는 동안 서버가 계속 돕니다. 끄려면 전용 서버 메뉴에서 "끄기".
화질이 느리거나 끊기면: 설정(또는 게임 중 ESC) > 화질을 낮추세요.

서버 여는 쪽 (Windows PC 또는 맥)
1. 게임을 실행합니다. (Windows: 방화벽 창이 뜨면 "허용". 맥: 아래 "맥만 처음 한 번" 먼저)
2. 서버 열기(또는 전용 서버) > "공개" 선택 > 서버 시작 > 캐릭터를 만들어 들어갑니다.
3. ESC 를 누르면 "서버 코드"(6자)가 보입니다. 접속하는 쪽에 알려 줍니다.

접속하는 쪽 (맥북이나 다른 Windows PC, 서버와 다른 PC여야 합니다)
맥만 처음 한 번: 압축을 푼 뒤 터미널에서
     cd ~/Downloads/Mac
     xattr -dr com.apple.quarantine . && chmod +x CompanyGame.app/Contents/MacOS/*
   ("손상되었다"고 나오면 같은 폴더에서: codesign --force --deep -s - CompanyGame.app)
1. 게임을 실행합니다. (맥: CompanyGame.app, Windows: CompanyGame.exe)
2. 참가하기 > "공개 서버" 목록에서 서버를 눌러 들어갑니다. 캐릭터를 만듭니다.
3. 서로 캐릭터가 보이고, 움직이면 상대 화면에서도 움직이는지 확인합니다.
4. ESC 의 "연결:" 줄(직접/중계, 왕복 ms)을 메모합니다.
5. ESC > 서버 나가기 > 참가하기 > "서버 코드" 칸에 코드를 넣고 접속해 한 번 더 들어갑니다.

끝나면 양쪽 로그를 보내 주세요.
- Windows: %USERPROFILE%\AppData\LocalLow\DefaultCompany\CompanyGame\Player.log
  서버 여는 쪽은 서버 로그도 함께. 프로필 슬롯 1이면 같은 폴더의 server\server.log,
  슬롯 2~4면 profile2\server\server.log ~ profile4\server\server.log
- 맥: ~/Library/Logs/DefaultCompany/CompanyGame/Player.log
  (Finder에서 Cmd+Shift+G 로 경로 붙여 넣기. 게임을 다시 켜면 덮어써지니 켜기 전에 빼 주세요.
   바로 전 실행은 같은 폴더의 Player-prev.log)
  맥 서버 로그: ~/Library/Application Support/DefaultCompany/CompanyGame/server/server.log
  (슬롯 2~4면 .../CompanyGame/profile2/server/server.log ~ profile4)
