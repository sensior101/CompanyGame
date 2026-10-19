CompanyGame 다른 PC 접속 시험 (2b-2, EOS 공개 서버 목록과 서버 코드)

서버 여는 쪽 (Windows PC)
1. Game2b2.zip 을 풀고 CompanyGame.exe 를 실행합니다. (방화벽 창이 뜨면 "허용")
2. 서버 열기 > "공개" 선택 > 서버 시작 > 캐릭터를 만들어 들어갑니다.
3. ESC 를 누르면 "서버 코드"(6자)가 보입니다. 접속하는 쪽에 알려 줍니다.
   (서버는 게임을 꺼도 계속 돕니다. 끄려면 게임의 서버 열기 화면에서 "끄기")

접속하는 쪽 (맥북이나 다른 Windows PC, 서버와 다른 PC여야 합니다)
맥만 처음 한 번: 압축을 푼 뒤 터미널에서
     cd ~/Downloads/Game2b2Mac
     xattr -dr com.apple.quarantine . && chmod +x CompanyGame.app/Contents/MacOS/*
   ("손상되었다"고 나오면 같은 폴더에서: codesign --force --deep -s - CompanyGame.app)
1. 게임을 실행합니다. (맥: CompanyGame.app, Windows: CompanyGame.exe)
2. 참가하기 > "공개 서버" 목록에서 서버를 눌러 들어갑니다. 캐릭터를 만듭니다.
3. 서로 캐릭터가 보이고, 움직이면 상대 화면에서도 움직이는지 확인합니다.
4. ESC 의 "연결:" 줄(직접/중계, 왕복 ms)을 메모합니다.
5. ESC > 서버 나가기 > 참가하기 > "서버 코드" 칸에 코드를 넣고 접속해 한 번 더 들어갑니다.

끝나면 양쪽 로그를 보내 주세요.
- Windows: %USERPROFILE%\AppData\LocalLow\DefaultCompany\CompanyGame\Player.log
  서버 여는 쪽은 같은 폴더의 server\server.log 도 함께
- 맥: ~/Library/Logs/DefaultCompany/CompanyGame/Player.log
