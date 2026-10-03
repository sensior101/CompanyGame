# 프로젝트 구조

기준: 2026-10-04 `main`. 기획 정본은 노션 [기획안 (수정본)](https://mushroom00.notion.site/3e9f770a0fc780d5a8d8ff291b3fc30a).

## 저장소

| 폴더 | 내용 |
| --- | --- |
| `CompanyGame/` | Unity 6000.4.5f1 프로젝트 |
| `Server/` | Node 24 + Fastify + SQLite 서버. [Server.md](Server.md) |
| `Docs/` | 이 문서들 |
| `ArtSource/` | Blender·이미지 원본과 생성 스크립트 (Unity가 직접 읽지 않음) |

## 스크립트와 어셈블리 (`CompanyGame/Assets/Scripts`)

폴더마다 asmdef가 있고 참조는 화살표 방향으로만 됩니다. 반대 방향은 컴파일 오류가 납니다.

```
Core → Economy → Gameplay → World → Player → UI
Core → Net → UI
Gameplay → NPC → UI
```

| 폴더 | 어셈블리 | 담당 | 주요 파일 |
| --- | --- | --- | --- |
| Core | `CompanyGame.Core` | 시간, 키 입력, 세션, 공통 도우미 | `GameTime`, `SimpleGameClock`, `GameInput`, `GameSession`, `GameSystem<T>`, `InputFocus`, `UIEventSystem` |
| Net | `CompanyGame.Net` | 서버·멀티 통신 | `PlayerData` (로그인 API 클라이언트, 아직 어떤 코드·씬에서도 쓰지 않음) |
| Economy | `CompanyGame.Economy` | 은행, 회사, 주식, 토지, 세금 | `BankManager`, `CompanyManager`, `StockMarketManager`, `LandPlotManager` |
| Gameplay | `CompanyGame.Gameplay` | 아이템·인벤토리 상태, 현금, 핸드폰 앱, 신고, SNS | `InventoryManager`, `InventoryState`, `ItemData`, `CashService`, `PhoneManager`, `GameSystems` |
| World | `CompanyGame.World` | 맵 이동, 포털, 정류장, 건물 출입문 | `SceneLoadManager`, `MapSpawnPoint`, `MapPortal`, `TransitStop`, `StoreInteractionPoint` |
| Player | `CompanyGame.Player` | 이동, 카메라, 스탯, 전투, 손 아이템, 생성 | `PlayerMovement`, `PlayerCameraController`, `PlayerStats`, `PlayerCombat`, `PlayerSpawner` |
| NPC | `CompanyGame.NPC` | NPC, NPC 거래 | `NpcTrader` |
| UI | `CompanyGame.UI` | 인벤토리·상점·정류장 메뉴, 채팅, 돈 표시, 핸드폰 화면 | `UI/Inventory/*`, `UI/Interaction/*`, `UI/Phone/*` |
| Editor | 에디터 전용 | 제작 도구 | [Git_Collaboration.md](Git_Collaboration.md#제작-도구-취급) |

아래 계층이 위 계층 상태를 알아야 할 때는 Core를 거칩니다.

- Player가 메뉴·채팅이 열렸는지 알 때: `InputFocus` (UI가 시작할 때 채움)
- Economy가 신고 벌점으로 회사 등록을 막을 때: `CompanyManager.RegistrationAllowed` (ReportManager가 채움)
- World가 플레이어 위치를 알 때: `SceneLoadManager.Traveller` (PlayerSpawner가 채움)

비어 있는 클래스(`SaveManager`, `TaxManager`, NPC 매니저 등)는 자리 잡기용입니다.

## 실행 중 구성

| 대상 | 방식 |
| --- | --- |
| 매니저 | `GameSystems`가 시작할 때(BeforeSceneLoad) 시계·은행·회사·주식·SNS·신고·토지 매니저를 만듭니다. 씬에 두지 않습니다. 새 매니저는 `GameSystem<T>`를 상속하면 하나만 존재하고 맵을 옮겨도 유지됩니다. |
| EventSystem | `GameSystems`가 가장 먼저 `UIEventSystem.Ensure()`로 하나만 만들고 맵을 옮겨도 유지합니다. 맵 씬에 EventSystem을 넣으면 2개가 됩니다. |
| 플레이어 | `Resources/Player.prefab` 하나를 `PlayerSpawner`가 만들고 맵 사이로 데려갑니다. 맵 씬에는 플레이어를 넣지 않습니다. |
| 맵 씬 | 그 맵 고유의 것(지형, 카메라 `PlayerCameraController` 1개, 스폰 지점, 포털·문·NPC)만 둡니다. 플레이어·EventSystem·채팅·인벤토리 UI는 넣지 않습니다. Play를 그 맵에서 시작하면 `default` 스폰에 섭니다. 카메라 시점(편의점은 1인칭)은 맵마다 다릅니다. |
| 맵 이동 | `SceneLoadManager.TryLoadMap(씬 경로, 스폰 ID)`. 씬은 빌드 목록에 있어야 합니다. |
| 핸드폰 | `Resources/PhoneUI.prefab`을 시작할 때 띄웁니다. R 키. |
| 채팅 | `Resources/ChatUI.prefab`을 시작할 때 띄웁니다. 모든 맵에서 Enter로 열고, 다른 입력창을 편집 중일 때는 열리지 않습니다. |
| 인벤토리 UI | `PlayerInventory`가 처음 만들 때 DontDestroyOnLoad로 두어 맵을 옮겨도 핫바가 남습니다. |
| 키 입력 | `Core/GameInput.cs` 한 파일. 키를 바꾸려면 여기만 고칩니다. |

## NPC 거래

- 모든 NPC가 같은 거래 창(`UI/Interaction/TradeWindow`)을 씁니다. NPC 오브젝트에 `NpcTrader`를 붙이고 거래 목록을 넣으면, 근처에서 Space로 열립니다. 범위가 NPC를 따라가므로 움직이는 NPC도 됩니다.
- 거래 한 줄(`TradeOffer`)은 왼쪽 `give`(플레이어가 내는 것)와 오른쪽 `get`(받는 것)입니다. 각 칸은 아이템 에셋이나 화폐 금액(`cash`)과 개수입니다. 화폐도 아이템이라, 돈이 왼쪽이면 사는 거래, 오른쪽이면 파는 거래입니다.
- 대화·퀘스트 등 코드에서는 `PlayerInteraction.Local.OpenTrade(목록)`으로 엽니다.
- 편의점 판매 목록은 점원 `ConvenienceClerk`에 있습니다.

## 돈

- 은행 계좌 = `BankManager.Money`. 주식·쇼핑 앱 결제는 `BankWallet`(`IWallet`)으로 계좌에서 나갑니다.
- 지갑 현금 = 인벤토리의 지폐·동전 아이템. `CashService`가 합계·지불·거스름돈을 계산합니다.
- 둘 사이는 출금(권종·장수 지정)과 입금으로만 오갑니다.

## 멀티플레이 방향

기본은 방장 호스트(스타듀밸리·마인크래프트 방식)입니다. 싱글, 방장 호스트, 온라인 랜덤 매칭을 나중에 고를 수 있게 해 두었습니다.

- `GameSession.Mode`: `Single`, `Host`(기본), `Client`. 랜덤 매칭도 방장을 찾는 방법일 뿐이라 Host/Client입니다.
- `GameSession.IsAuthority`가 참인 쪽(싱글·방장)만 시간과 경제를 계산합니다. 지금은 게임 시계에 적용되어 있고, 주가·매출은 시계를 따라 움직입니다.
- 네트워크 라이브러리는 아직 없습니다. 붙일 때는 `Net`에서 접속 시 `GameSession.Begin(Host/Client)`을 부르고, 방장이 보낸 상태를 참가자 화면에 반영합니다. 자세한 순서는 [Remaining_Work.md](Remaining_Work.md).
