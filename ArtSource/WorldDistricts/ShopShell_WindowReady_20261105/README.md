# 창문 없는 상가 기본형

실내 26 × 16.4m, 천장 높이 3.3m, 벽 두께 20cm. 기본형은 창문 0개와 중앙 양개 슬라이딩 출입문으로 구성했다.

- [기본 Blender 모델](ShopShell_NoWindows.blend)
- [내부 확인 렌더](Previews/03_Interior_Cutaway.png)
- [문 닫힘](Previews/01_Exterior_Closed.png) · [문 열림](Previews/02_Exterior_Open.png)
- [창문 설치 구조 예제](WindowPlacement_Demo.blend)
- [창문 위치 설정 예제](WindowLayout.example.json)

기본 파일에는 바닥, 천장, Front·Back·Left·Right 벽 루트, AutomaticDoor 루트와 LeftLeaf·RightLeaf가 분리되어 있다. 문은 프레임 1에서 닫혀 있고 45~80에서 열려 있다. Blender 타임라인 1~120을 재생하면 개폐를 볼 수 있다. 실제 게임의 접근 감지·네트워크 제어는 이후 기존 LobbyAutomaticDoor와 연결해야 한다.

문틀 사이 폭은 1.96m, 문짝 이동 거리는 각 1.03m, 열린 문짝 사이 폭은 2.06m이다. 문짝은 벽 안쪽 레일을 따라 이동한다.

창문은 벽 식별자 wall, 벽 중심 기준 가로 위치 u, 바닥에서 창문 아래까지 sill, 너비 width, 높이 height로 지정한다. 모든 길이는 미터다. 가로 방향은 해당 벽 루트의 로컬 X 방향이며, 정확한 원점과 회전은 layout.json에 기록했다.

WindowLayout.example.json의 windows 배열을 편집한 뒤 다음 명령을 저장소 루트에서 실행하면 ShopShell_CustomLayout.blend를 생성한다.

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' -b --factory-startup --addons cycles --python 'ArtSource/WorldDistricts/ShopShell_WindowReady_20261105/build_shell.py' -- --layout 'ArtSource/WorldDistricts/ShopShell_WindowReady_20261105/WindowLayout.example.json'
```

창문 항목을 이동하면 이전 위치의 벽이 복구되고 새 위치에 구멍이 생성된다. 항목을 삭제하면 해당 구멍이 막힌다. 빈 배열은 기본형을 생성한다. 문과의 겹침, 다른 창문과의 겹침, 벽 바깥 배치 및 잘못된 크기는 거부한다. 예제는 Blender 제작용이며 플레이어용 설치 UI·저장·호스트 검증·동기화는 아직 미구현이다.

## 작업 보고

1. 기존 파일 수정은 없다. 원본 ShopInterior_Empty.blend의 SHA-256 일치를 검사했다.
2. 새 build_shell.py는 상가 벽의 개구부 재구성, 문짝 분리와 제작 검증을 담당한다. 모델, 렌더, 배치 데이터와 QA 결과도 이 폴더에 저장했다. 기존 가구 함수는 벽별 개구부 책임을 갖지 않아 별도 제작 스크립트로 구성했다.
3. 기존 HouseholdLowPoly의 init, box, rgba 및 CommercialLowPoly의 camera_scene을 재사용했다. LobbyAutomaticDoor는 향후 연결 대상으로 확인했다.
4. 창문 배치 데이터 검증 → 벽별 직사각형 구간 계산 → 벽·창틀·유리 생성 → Blender 파일 저장 흐름이다. 벽의 양면에 동일한 구멍이 생성된다.
5. 기본 형상·재질·카메라 생성은 기존 함수를 재사용했다. 게임 공통 관리자를 추가하지 않았다.
6. 기존 게임 API·저장 데이터·호출부 변경은 없다. 이번 산출물은 Blender 모델과 제작 도구다.
7. 창문 설치·이동·철거의 벽 재구성, 잘못된 배치 5종 거부, 개구부 면적, 벽 관통 레이, 메시 폐쇄성과 퇴화 면, 문짝 개폐 간격을 검사했다. 저장한 파일의 재개방 검사도 수행했다. Unity 반입, 자동문 접근 감지, 플레이어 창문 배치 및 멀티플레이 동작은 미검증·미연결이다.
