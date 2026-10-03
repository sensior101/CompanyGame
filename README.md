# CompanyGame

늪지대컴퍼니 기반 3D 멀티플레이 경제 시뮬레이션 게임입니다.
기획 정본은 노션 [기획안 (수정본)](https://mushroom00.notion.site/3e9f770a0fc780d5a8d8ff291b3fc30a)입니다. 예전 기획 복사본은 `Docs/Game_Plan_Legacy.md`에 있습니다.

## 시작하기

1. Unity Hub → Add project from disk → 저장소의 `CompanyGame/` 폴더 (`Assets`, `ProjectSettings`가 있는 곳)
2. 에디터 버전 **6000.4.5f1**
3. `Assets/Scenes/daldongnaemap`을 열고 ▶

| 키 | 동작 |
| --- | --- |
| WASD / Shift | 이동 / 달리기 |
| Space | 상호작용 (문, 정류장, 포털) |
| E / R | 인벤토리 / 핸드폰 |
| Tab | 1인칭 ↔ 3인칭 |
| 마우스 왼쪽 | 공격·사용 |

키 설정은 전부 `Core/GameInput.cs` 한 파일에 있습니다.

## 저장소 구성

| 폴더 | 내용 |
| --- | --- |
| `CompanyGame/` | Unity 프로젝트 |
| `Server/` | Node + Fastify + SQLite 서버 (로그인·프로필). [Server/README.md](Server/README.md) |
| `Docs/` | 협업 규칙, 서버 API, DB 설계 |
| `ArtSource/` | Blender·이미지 원본과 생성 스크립트 |

## 스크립트 구조 (`CompanyGame/Assets/Scripts`)

폴더마다 asmdef가 있고, 참조는 아래 화살표 방향으로만 가능합니다. 반대 방향 참조는 컴파일 오류가 납니다.

```
Core → Economy → Gameplay → World → Player → UI
Core → Net, NPC
```

| 폴더 | 어셈블리 | 담당 |
| --- | --- | --- |
| Core | `CompanyGame.Core` | 게임 시간, 키 입력, 세션(싱글/방장/참가자), 저장·이벤트, 공통 UI 도우미 |
| Net | `CompanyGame.Net` | 서버·멀티 통신 |
| Economy | `CompanyGame.Economy` | 은행, 회사, 주식, 토지, 세금 |
| Gameplay | `CompanyGame.Gameplay` | 아이템·인벤토리 상태, 현금(지폐), 핸드폰 앱, 신고, SNS, 퀘스트 |
| World | `CompanyGame.World` | 맵 이동, 포털, 정류장, 상점 지점 |
| Player | `CompanyGame.Player` | 이동, 카메라, 스탯, 전투, 손에 든 아이템, 플레이어 생성 |
| NPC | `CompanyGame.NPC` | NPC |
| UI | `CompanyGame.UI` | 인벤토리·상점·정류장 메뉴, 채팅, 돈 표시, 핸드폰 화면 |
| Editor | (에디터 전용) | 제작 도구. [Docs/Git_Collaboration.md](Docs/Git_Collaboration.md#제작-도구-취급) |

아래 계층이 위 계층의 상태가 필요하면 Core의 공통 통로를 씁니다. 예: Player 스크립트는 메뉴가 열렸는지 `InputFocus`로 확인합니다.

## 실행 중 구성

- **매니저**: `Gameplay/GameSystems.cs`가 시작할 때 시계·은행·회사·주식·SNS·신고·토지 매니저를 만듭니다. 씬에 배치하지 않습니다. 새 매니저는 `class XxxManager : GameSystem<XxxManager>`로 만들면 하나만 존재하고 맵을 옮겨도 유지됩니다.
- **플레이어**: `Resources/Player.prefab` 하나를 `PlayerSpawner`가 생성하고 맵 사이로 데려갑니다. 맵 씬에는 카메라와 스폰 지점(`MapSpawnPoint`, Play 시작용 `default`)만 둡니다.
- **핸드폰**: `Resources/PhoneUI.prefab`을 시작할 때 띄웁니다. 메뉴 CompanyGame → Setup → Build Phone UI Prefab으로 다시 만들 수 있습니다.
- **돈**: 은행 계좌는 `BankManager`, 들고 다니는 지폐·동전은 인벤토리 아이템(`CashService`)입니다. 둘 사이는 출금·입금으로만 오갑니다.
- **멀티**: 기본은 방장 호스트입니다. `GameSession.IsAuthority`가 참인 쪽(싱글·방장)만 시간과 경제를 계산합니다. 네트워크 라이브러리는 `Net`에 붙입니다.

## 협업

브랜치·커밋·씬 작업 규칙은 [Docs/Git_Collaboration.md](Docs/Git_Collaboration.md)를 따릅니다.
한 씬은 한 번에 한 명만 수정하고, 자주 바뀌는 부분은 프리팹으로 빼서 작업합니다.
