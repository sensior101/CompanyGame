EOS 연결 시험 (CompanyGame, macOS - Intel / Apple Silicon 공용)

처음 한 번만 (인터넷에서 받은 앱이라 macOS가 막습니다)
1. 압축을 풉니다. (EosTestMac 폴더가 생깁니다)
2. 터미널을 열고 아래 두 줄을 붙여 넣습니다. (cd 뒤에 EosTestMac 폴더를 끌어다 놓아도 됩니다)
     cd ~/Downloads/EosTestMac
     xattr -dr com.apple.quarantine . && chmod +x *.command CompanyGame.app/Contents/MacOS/*
3. 그래도 "열 수 없음" 창이 뜨면: 시스템 설정 > 개인정보 보호 및 보안 > 아래쪽의 "그래도 열기"를 누르고 다시 실행합니다.
   ("손상되었다"고 나오면 터미널에서 같은 폴더에서: codesign --force --deep -s - CompanyGame.app)

시험
1. eos_test.command 를 더블클릭합니다. (방화벽 창이 뜨면 "허용")
2. 받은 서버 PUID를 붙여 넣고 Enter를 누릅니다. 1~2분 뒤 결과가 나옵니다.
3. 같은 폴더의 eos_test_log.txt 를 보내 주세요.
(서버를 여는 쪽은 eos_test_server.command 를 실행하고 화면에 나온 PUID를 알려 줍니다. 키를 누르거나 창을 닫으면 서버도 꺼집니다.)
