# 프로젝트 구조

기준: 2026-10-04 `main`, 어셈블리 표와 멀티플레이 구조는 2026-10-19(2b-2). 기획 정본은 노션 [기획안 (수정본)](https://mushroom00.notion.site/3e9f770a0fc780d5a8d8ff291b3fc30a).

## 저장소

| 폴더 | 내용 |
| --- | --- |
| `CompanyGame/` | Unity 6000.4.5f1 프로젝트 |
| `Server/` | 계정 서버(Node 24 + Fastify + SQLite, Cloudflare Worker 사본). 2b-2부터 게임은 쓰지 않음(계정은 EOS). 실시간 플레이는 Unity 안의 게임 서버가 맡음. [Server.md](Server.md) |
| `Docs/` | 이 문서들 |
| `ArtSource/` | Blender·이미지 원본과 생성 스크립트 (Unity가 직접 읽지 않음) |

## 스크립트와 어셈블리 (`CompanyGame/Assets/Scripts`)

폴더마다 asmdef가 있고 참조는 화살표 방향으로만 됩니다. 반대 방향은 컴파일 오류가 납니다.

```
Core → Economy → Gameplay → World → Player → UI
NetCore → Net → Player → UI      (NetCore는 아무것도 참조하지 않음: 프로토콜·전송·서버 본체·월드 파일)
NetCore → Net.Eos → Net, UI     (Net.Eos: EOS 플러그인을 쓰는 곳은 여기뿐)
Gameplay → NPC → UI
```

| 폴더 | 어셈블리 | 담당 | 주요 파일 |
| --- | --- | --- | --- |
| Core | `CompanyGame.Core` | 시간, 키 입력, 세션, 공통 도우미 | `GameTime`, `SimpleGameClock`, `GameInput`, `GameSession`, `GameSystem<T>`, `InputFocus`, `UIEventSystem` |
| NetCore | `CompanyGame.NetCore` | 게임 서버 본체와 프로토콜(Unity 게임 코드와 무관) | `GameServer`, `SyncProtocol`, `SyncTransport`(+`MultiTransport`, `PacketFraming`), `TcpTransport`, `WorldStore`(`world.dat`) |
| Net | `CompanyGame.Net` | 접속·호스팅 | `SyncClient`, `SyncHost`, `NetEntity`, `NetBehaviour`, `NetTransform`, `ServerSettings`, `LocalServerProcess` |
| Net/Eos | `CompanyGame.Net.Eos` | EOS 계정·P2P·세션 | `EosAccount`(Device ID 로그인, ID 토큰), `EosTransport`(P2P 서버·클라이언트), `EosSession`(공개 목록·서버 코드), `EosProbe`(12.4 시험) |
| Economy | `CompanyGame.Economy` | 은행, 회사, 주식, 토지, 소유권, 세금 | `BankManager`, `CompanyManager`, `StockMarketManager`, `LandPlotManager`, `PropertyRegistry` |
| Gameplay | `CompanyGame.Gameplay` | 아이템·인벤토리 상태, 현금, 핸드폰 앱, 신고, SNS | `InventoryManager`, `InventoryState`, `ItemData`, `CashService`, `PhoneManager`, `GameSystems` |
| World | `CompanyGame.World` | 맵 이동, 포털, 정류장, 건물 출입문, 소유 구역, 계단 | `SceneLoadManager`, `MapSpawnPoint`, `MapPortal`, `TransitStop`, `StoreInteractionPoint`, `PropertyZone`, `FloorStairs` |
| Player | `CompanyGame.Player` | 이동, 카메라, 스탯, 전투, 손 아이템, 생성. 게임 서버의 게임 쪽(서버 월드·장부)과 동기화 | `PlayerMovement`, `PlayerCameraController`, `PlayerStats`, `PlayerCombat`, `PlayerSpawner`, `ServerWorld`, `ServerLedger`, `WorldStateSync`, `CharacterSync`, `AvatarSync` |
| NPC | `CompanyGame.NPC` | NPC, NPC 거래 | `NpcTrader` |
| UI | `CompanyGame.UI` | 인벤토리·상점·정류장 메뉴, 채팅, 돈 표시, 핸드폰 화면 | `UI/Inventory/*`, `UI/Interaction/*`, `UI/Phone/*` |
| Editor | 에디터 전용 | 제작 도구 | [Git_Collaboration.md](Git_Collaboration.md#제작-도구-취급) |

아래 계층이 위 계층 상태를 알아야 할 때는 Core를 거칩니다.

- Player가 메뉴·채팅이 열렸는지 알 때: `InputFocus` (UI가 시작할 때 채움)
- Economy가 신고 벌점으로 회사 등록을 막을 때: `CompanyManager.RegistrationAllowed` (ReportManager가 채움)
- World가 플레이어 위치를 알 때: `SceneLoadManager.Traveller` (PlayerSpawner가 채움)

비어 있는 클래스(`TaxManager`, NPC 매니저 등)는 자리 잡기용입니다. `SaveManager.WriteJsonFile`은 기존 저장 소유자가 전달한 JSON을 원자적으로 파일 교체하는 공통 I/O입니다. 콘텐츠의 스키마·파일명·저장 시점은 각 기존 소유자가 유지합니다.

## 실행 중 구성

| 대상 | 방식 |
| --- | --- |
| 매니저 | `GameSystems`가 시작할 때(BeforeSceneLoad) 시계·은행·회사·주식·SNS·신고·토지 매니저를 만듭니다. 씬에 두지 않습니다. 새 매니저는 `GameSystem<T>`를 상속하면 하나만 존재하고 맵을 옮겨도 유지됩니다. |
| EventSystem | `GameSystems`가 가장 먼저 `UIEventSystem.Ensure()`로 하나만 만들고 맵을 옮겨도 유지합니다. 맵 씬에 EventSystem을 넣으면 2개가 됩니다. |
| 플레이어 | `Resources/Player.prefab` 하나를 `PlayerSpawner`가 만들고 맵 사이로 데려갑니다. 맵 씬에는 플레이어를 넣지 않습니다. |
| 맵 씬 | 그 맵 고유의 것(지형, 카메라 `PlayerCameraController` 1개, 스폰 지점, 포털·문·NPC)만 둡니다. 플레이어·EventSystem·채팅·인벤토리 UI는 넣지 않습니다. Play를 그 맵에서 시작하면 `default` 스폰에 섭니다. 카메라 시점(편의점은 1인칭)은 맵마다 다릅니다. |
| 맵 이동 | `SceneLoadManager.TryLoadMap(씬 경로, 스폰 ID)`. 씬은 빌드 목록에 있어야 합니다. 같은 씬 안 이동(층·방)은 `PlayerSpawner.TeleportInScene(스폰 ID)`이고, `StoreInteractionPoint`의 대상 씬을 비워 두면 문이 이 방식으로 동작합니다. |
| 핸드폰 | `Resources/PhoneUI.prefab`을 시작할 때 띄웁니다. R 키. |
| 채팅 | `Resources/ChatUI.prefab`을 시작할 때 띄웁니다. 모든 맵에서 Enter로 열고, 다른 입력창을 편집 중일 때는 열리지 않습니다. |
| 인벤토리 UI | `PlayerInventory`가 처음 만들 때 DontDestroyOnLoad로 두어 맵을 옮겨도 핫바가 남습니다. |
| 키 입력 | `Core/GameInput.cs` 한 파일. 키를 바꾸려면 여기만 고칩니다. |

## 고시원과 소유권

- 고시원 내부 씬은 `Scenes/Interiors/GoshiwonInterior.unity`입니다. 달동네 고시원 정문(`Goshiwon Entrance`)으로 들어가고, 1층 출구로 나오면 `goshiwon_exit`에 섭니다.
- 씬은 메뉴 `CompanyGame/Setup/Build Goshiwon Interior`(`Editor/Goshiwon/GoshiwonInteriorBuilder`)가 상자 도형으로 만들고, 벽지·카펫·마루 등 텍스처도 같은 메뉴가 `Art/Goshiwon/Textures`에 코드로 생성합니다(다시 돌려도 같은 결과). 다시 실행하면 씬을 통째로 덮어쓰므로, 손으로 꾸미기 시작한 뒤에는 실행하지 않습니다. 집문서 아이템·스프라이트·달동네 출입문은 `Run Goshiwon Setup`(`GoshiwonSetup`)이 만듭니다.
- 1~4층(층고 3m), 가구 없는 빈 방 20개(101·102, 201~206, 301~306, 401~406). 복도는 덩굴 벽지·나무 징두리·카펫, 방은 크림색 벽·나무 마루·커튼 창입니다. 1층 로비에는 접수대와 고시원 주인이 있습니다. 주인은 편의점 점원과 같은 구성(`MaleVisual` + 앞치마 + 명찰)이고 `DialogueData`로 말을 겁니다.
- 층 사이는 복도 남쪽 끝 계단을 걸어서 오르내립니다(층 선택 메뉴 없음). 각 층 계단 홀에서 계단을 바라보면 왼쪽이 올라가는 계단, 오른쪽이 내려가는 계단이고(머리 위 ↑/↓ 표지판), 한 층은 9단씩 두 번 꺾입니다. 계단 디딤판은 보이기만 하고, 밟는 것은 계단마다 하나씩 있는 보이지 않는 경사 충돌체(`Walk Ramp`, 29°)입니다. 1층 계단 밑은 벽, 4층 빈 쪽은 난간으로 막습니다. `FloorStairs`/`FloorStairsMenu`는 이제 한대 로비에서만 씁니다.
- 방 문은 지금처럼 Space로 순간이동합니다(`door_201` ↔ `room_201`).
- 스폰 ID: `goshiwon_entry`, `floor_1~4`(각 층 계단 홀), `room_201`(방 안), `door_201`(복도 문 앞).

### 소유권 (집문서·땅문서)

- 소유 상태는 `PropertyRegistry`(Economy, `GameSystems`가 생성)가 부동산 ID → 소유자 이름으로 들고 있습니다. 이미 주인이 있으면 다른 사람은 등록할 수 없고, 포기는 주인만 할 수 있습니다. `CaptureState/RestoreState`는 저장 시스템이 생기면 연결합니다(지금은 세션 동안만 유지).
- 문서는 `ItemCategory.Document` 아이템이고 `propertyId`, `propertyName`을 가집니다. 고시원 집문서 20종은 `Resources/Inventory/Deeds/`(예: `deed_house_goshiwon_201` "고시원 201호 집문서"). 땅문서는 `land_<부지ID>` 규칙으로 같은 방식으로 추가합니다(스프라이트 `Art/Items/Deeds/deed_land.png`만 준비됨).
- 문서를 핫바에서 들고 Space를 누르면 등록, 내 문서를 5초 누르면 게이지가 찬 뒤 포기합니다. 문·NPC·정류장 근처이거나 메뉴·핸드폰이 열려 있으면 그쪽이 Space를 가져갑니다. 처리는 플레이어에 자동으로 붙는 `UI/Property/PropertyUseController`입니다.
- 시작 시 주인 없는 첫 고시원 방 문서 1장이 인벤토리에 들어옵니다(달동네 고시원은 시작 구역).
- 소유 구역은 `PropertyZone`(박스, `propertyId`, `displayName`)입니다. 방·땅 구분이 없습니다. 플레이어가 남의 소유 구역에 새로 들어가면 시스템 로그 "OO님이 고시원 201호에 무단으로 침입하였습니다."가 뜹니다. 빈 곳이나 내 소유는 로그가 없습니다. `seedOwner`는 테스트용 미리 지정 주인입니다(지금 202호 = "이웃").
- 플레이어 이름은 `GameSession.LocalPlayerName`(기본 "Player") 하나에서 읽습니다.
- 멀티플레이를 붙일 때: 등록·포기를 방장이 판정하고(`GameSession.IsAuthority`), 소유자를 이름 대신 고유 플레이어 ID로 바꾸고, 무단침입 로그를 모두에게 보내야 합니다.

## NPC 거래

- 모든 NPC가 같은 거래 창(`UI/Trade/TradeWindow`)을 씁니다. NPC 오브젝트에 `NpcTrader`를 붙이고 거래 목록을 넣으면, 근처에서 Space로 열립니다. 범위가 NPC를 따라가므로 움직이는 NPC도 됩니다. `DialogueData`와 `openAfterDialogue`를 함께 설정하면 기존 `PlayerInteraction`이 인사 후 같은 창을 엽니다.
- 거래 한 줄(`TradeOffer`)은 왼쪽 `give`(플레이어가 내는 것)와 오른쪽 `get`(받는 것)입니다. 각 칸은 아이템 에셋이나 화폐 금액(`cash`)과 개수입니다. 화폐도 아이템이라, 돈이 왼쪽이면 사는 거래, 오른쪽이면 파는 거래입니다.
- 대화·퀘스트 등 코드에서는 `PlayerInteraction.Local.OpenTrade(목록)`으로 엽니다.
- 편의점 판매 목록은 점원 `ConvenienceClerk`에 있습니다.

## 돈

- 은행 계좌 = `BankManager.Money`. 주식·쇼핑 앱 결제는 `BankWallet`(`IWallet`)으로 계좌에서 나갑니다.
- 지갑 현금 = 인벤토리의 지폐·동전 아이템. `CashService`가 합계·지불·거스름돈을 계산합니다.
- 둘 사이는 출금(권종·장수 지정)과 입금으로만 오갑니다.

## 공통 시스템 로그

- 잔액은 기존 `BankManager.AddMoney/TrySpend`가 변경하고 `MoneyChanged`에 금액과 `MoneyChangeReason`을 전달합니다. 현금은 기존 `CashService`와 `InventoryState`가 처리하며, 성공한 현금 거래는 `CashService.TransactionCompleted`에 소유 인벤토리·증감 금액·사유를 전달합니다.
- 공통 `TradeSession`은 드래그한 상품을 인벤토리에 놓아 거래가 확정될 때 현금 거래 이벤트를 보냅니다. 취소·실패·0원 거래는 수입/지출 성공 로그를 만들지 않습니다. 일부 수량만 놓아 전체 거래가 확정된 경우에도 한 번만 기록합니다.
- 기존 `ChatUIManager`가 위 이벤트를 받아 **사유와 금액**을 표시합니다. 통장 잔액과 소지 현금을 구분하며, 해당 로컬 인벤토리의 거래만 표시합니다. 입출금도 이 경로를 사용하고 콘텐츠 UI에서 금액 로그를 중복 호출하지 않습니다. 별도 `TransactionLogManager`는 없습니다.
- `ShowSystemMessage`가 채팅 기록과 팝업을 함께 출력합니다. 팝업이 사라져도 Enter로 여는 기록은 남습니다. 기존 공개 `ShowSystemMessage(string)`와 `ShowPopup(string,string)` 호출은 유지됩니다.
- 범죄는 기존 `ReportManager.CrimeOccurred` 및 `ReportResolved` 이벤트로 전달합니다. `PropertyUseController`의 무단침입, `PlayerStats.TakeDamage`의 다른 플레이어에 의한 실제 피해(폭행/치명상), 신고 판정이 이 경로를 사용합니다. `ChatUIManager`는 `CrimeType`이 지정된 시스템 로그만 빨간색으로 표시하며, 뒤의 일반 로그에는 색이 이어지지 않습니다. 관찰 로그 자체는 신고나 벌점을 자동으로 만들지 않습니다.
- 거래 중 `ControlLock`이 채팅 입력을 잠가도 로그 구독은 유지하고, 채팅 객체가 제거될 때 해제합니다. 씬마다 별도 로그 관리자를 만들지 않습니다.
- 실제 금전 거래 API와 공통 거래 확정은 `GameSession.IsAuthority`를 검사합니다. `BankManager.SetMoney`는 초기화·수동 조정·상태 반영용으로 유지하며 거래 성공 로그를 만들지 않습니다. 클라이언트 요청/서버 응답 전송은 아직 구현되지 않았습니다.

## 책·거래 책임 정리 (2026-10-10)

### 파일 위치와 어셈블리

| 위치 | 기존 파일과 책임 |
| --- | --- |
| `Gameplay/Item/Book` | `BookInstanceData`, `BookSaveService`, `LibraryCatalog`: 책 데이터·저장 내용·도서관 규칙 |
| `Gameplay/Item/Book/UI` | `BookReader`, `LibraryDesk`: 책 읽기·편집 화면과 도서관 NPC 흐름 |
| `Gameplay/Item` | `ItemInstance`, `ItemData`, `InventoryState`: 공통 아이템 데이터와 실제 슬롯 변경 |
| `Gameplay/Item/World/UI` | `WorldDroppedItem`: 월드 드랍·설치·줍기와 현장 상호작용 |
| `Gameplay/Trade` | `TradeSession`, `RentalTradeSession`: 모든 거래의 커서·확정·취소·대여 가능 여부 |
| `UI/Trade` | `TradeWindow`, `TradePointer`: 공통 거래 화면·18개 페이지·마우스 입력 |
| `UI/Inventory` | `InventoryUI`, `InventoryItemSelector`, `InventorySlotPointer` 등 인벤토리 표시·선택·입력 |

이동한 8개 `.cs`는 `.meta` GUID와 클래스명·네임스페이스·어셈블리를 유지합니다. 책/월드 아이템의 `UI` 하위 폴더 두 곳에는 기존 `CompanyGame.UI`를 가리키는 `.asmref`가 있습니다. 콘텐츠 위치를 정리하면서 Player·NPC·UI 참조를 Gameplay 어셈블리로 끌어들여 순환 참조를 만드는 일을 막습니다. 새 런타임 C# 파일이나 관리자는 만들지 않았습니다.

### 공통 흐름과 중복 제거

- 책 표시: `BookInstanceData.Tooltip` → `ItemStack.Tooltip` / `TradeItem.Tooltip` → 인벤토리·선택창·구매·대여·회수 화면. 제목과 `저자 : 이름`을 함께 표시합니다. 빈 책은 기존 아이템명, 저자 정보가 없는 작성된 책은 `저자 : 미상`입니다. 대여 상태는 이 공통 문구 뒤에만 붙습니다.
- 선택창은 기존 `InventorySlotPointer`의 읽기 전용 호버 모드를 사용합니다. 공통 인벤토리 툴팁은 내용에 맞춰 높이를 늘리고 선택창 위에 표시합니다. 우클릭·드래그로 선택 조건을 우회하지 않습니다.
- 거래 확정/취소: `TradeSession`의 임시 상태 → `InventoryState.TryPlaceStack` → 성공한 모델 적용 → 기존 `CashService` 이벤트. 거래 코드가 슬롯 필드를 직접 쓰지 않습니다. 고유 식별자 중복·현금 합계 오버플로·슬롯 용량 검사도 인벤토리에서 처리합니다. 일부 스택 배치·취소·정확한 권종 규칙은 유지합니다.
- 카페: `DialogueData` 인사 → `NpcTrader` 목록 → 기존 `PlayerInteraction.OpenTrade`. 말만 하는 직원은 `DialogueData`만 씁니다. `LibraryDesk`는 책 서비스에 사용합니다. 기존 씬/API 호환을 위해 옛 카페 필드는 유지하며, 옛 컴포넌트가 남은 씬도 `NpcTrader`로 연결합니다.
- 저장: `BookSaveService`와 `LibraryCatalog`가 저장 내용을 만들고 기존 `SaveManager.WriteJsonFile`을 호출합니다. `books.json`, `library.json` 경로·스키마·오류 처리 정책은 유지합니다.
- 수입/지출과 시스템 로그는 위의 `BankManager` / `CashService` → `ChatUIManager` 경로를 계속 사용합니다. 콘텐츠에 별도 잔액·로그 관리자를 추가하지 않습니다.

### 전체 점검 범위와 한계

이번 정리에서 수정한 기존 코드:

- `BookInstanceData.cs`, `InventoryState.cs`, `TradeSession.cs`, `TradeWindow.cs`: 공통 책 툴팁, 모델을 통한 배치/취소, 드랍 후 호버 갱신.
- `InventoryUI.Tooltip.cs`, `InventoryUI.Selection.cs`, `InventorySlotPointer.cs`: 두 줄 툴팁 크기·표시 순서와 선택창 호버 연결.
- `NpcTrader.cs`, `PlayerInteraction.cs`, `LibraryDesk.cs`: 공통 인사 후 거래 연결 및 기존 직렬화 호환.
- `SaveManager.cs`, `BookSaveService.cs`, `LibraryCatalog.cs`: 저장 파일 교체 I/O 재사용.
- 코드 내용 변경 없이 이동한 파일: `BookReader.cs`, `ItemInstance.cs`, `RentalTradeSession.cs`, `TradePointer.cs`, `WorldDroppedItem.cs`. 위에서 수정한 `LibraryDesk.cs`, `TradeSession.cs`, `TradeWindow.cs`도 함께 이동했습니다.
- `CivicLibraryInterior.unity`: 카페 직원 두 명의 컴포넌트를 기존 `NpcTrader`/`DialogueData`로 정리. `DefaultVolumeProfile.asset`: 누락된 샘플 하위 에셋만 정리.
- 기존 제작/검증 파일 `ConfigureLibraryServices.cs`, `VerifyLibraryLedger.cs`, `VerifyLibraryInput.cs`, `VerifySharedTradeScenes.cs`와 관련 문서 갱신. 검증 결과·스크린샷은 무시되는 `QA/` 출력이며 런타임에 포함되지 않습니다.

신규 소스 파일은 없습니다. 이동 폴더의 Unity 메타데이터와 기존 UI 어셈블리 연결용 `.asmref` 두 개만 추가되었습니다.

- 프로젝트 C# 157개(런타임 141개, 기존 Editor 도구 16개)의 폴더 책임·호출부·직접 상태 변경·중복 공통 처리를 점검했습니다. 미구현 자리 잡기 클래스와 직렬화 호환 어댑터는 별도 대체 구현으로 판단하지 않았습니다.
- 씬·프리팹·설정 에셋 1,845개의 스크립트 GUID와 157개 스크립트의 어셈블리 매핑을 검사합니다. 이동 GUID 8개를 이전 값과 대조합니다. 목록·해시는 `ArtSource/WorldDistricts/CivicLibraryV4/QA/ScriptArchitectureAudit.json`, 참조 검사는 `ScriptReferenceAudit.json`에 기록합니다.
- `DefaultVolumeProfile.asset`에서 설치된 URP 템플릿의 누락된 샘플 참조도 확인했습니다. 미해결 GUID 4개와 빈 스크립트 참조 5개, 총 9개를 Unity의 `AssetDatabase.RemoveScriptableObjectsWithMissingScript`로 정리했습니다. 유효한 볼륨 컴포넌트 19개의 직렬화 값과 프로필 GUID는 전후 동일합니다(`QA/ProfileCleanup.json`).
- 기존 `InventoryModelQA` 32개, `CashModelQA` 76개, 도서관 모델 64개, 입력 파이프라인을 통한 키보드·마우스 68개, 독립된 두 씬의 공통 거래 13개, 총 253개 검증이 통과했습니다. 입력 검증은 임시 Input System 장치를 사용하며 종료 시 원래 입력 장치와 플레이어 데이터를 복원합니다. 157개 파일의 모든 게임 동작을 실행 검증했다는 의미는 아닙니다. (멀티플레이는 이 점검 당시 미구현. 지금 구조는 아래 "멀티플레이 구조")

## 멀티플레이 구조 (5단계 기준, 2026-10-19)

정본은 [Multiplayer_Redesign.md](Multiplayer_Redesign.md)입니다. 요약:

- **게임 서버(동기화 계층)가 정본입니다.** 혼자 하기(1인 앱 안 서버), 서버 열기(앱 안 서버), 서버 프로세스(`-gameserver`) 셋 다 같은 `GameServer`를 씁니다. 월드와 캐릭터는 서버의 `worlds/<id>/world.dat`에 저장됩니다.
- 서버에서 정하는 것: 시계·주가·회사·소유권·땅·SNS·신고(`ServerWorld`, 4c), 은행·주식·인벤토리·드롭·탈것·도서관(`ServerLedger` 등, 4d). 클라이언트는 요청(`SharedWriteGate` → `WorldStateSync`)을 보내고 결과로 화면을 맞춥니다.
- `GameSession.Mode`는 `Host`(기본, 오프라인과 앱 안 서버의 주인)와 `Client`(다른 프로세스의 서버에 접속한 동안)뿐입니다. `GameSession.IsAuthority`는 이 PC가 시계·경제를 계산할 때(오프라인, 서버 프로세스, 앱 안 서버의 주인) 참입니다.
- **오프라인 경로**: 에디터에서 맵 씬을 바로 플레이하면 세션 없이 이 PC가 계산하고 PC 파일(`world/world.json`, `player/<id>.json`)에 저장합니다(개발용).
- **시민·교통(5단계, 12.14)**은 각 PC가 "시드 + 월드 시계"로 똑같이 계산합니다. `NpcClock`(월드 초, 서버에서는 월드 시계 그대로, 클라이언트는 시계 섹션으로 맞춤)과 시드(월드 id 해시)로, 교통은 608초 주기 계획표(`TrafficPlan`), 시민은 한 주기짜리 일정표(`CitizenWalker.Plan`)를 맵을 열 때 만들어 그립니다. 신호등도 같은 시계라 PC마다 같습니다. 플레이어 앞에서 멈추는 반응은 각 PC의 그리기에서만 합니다. 전용 메시지는 없습니다.
- 옛 방(Cloudflare·Steam 방, 방장 넘기기, `NetSession` 계열)은 4e에서 지웠습니다.
- **계정과 접속(2b-2)**: 계정은 EOS PUID입니다(`EosAccount`, 시작할 때 Device ID 로그인. 설정 파일이 없거나 `-noeos`면 PC GUID). 혼자 하기가 아닌 서버는 TCP와 EOS P2P를 함께 받고(`MultiTransport`), EOS 세션으로 공개 목록과 서버 코드를 냅니다. 서버는 Hello의 계정을 EOS(P2P 상대 PUID 또는 ID 토큰)로 확인합니다.
