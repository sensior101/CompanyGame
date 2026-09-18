# CompanyGame Unity 스크립트 기능 구현 총정리

**대상:** `CompanyGame/Assets/Scripts`의 C# 파일 49개  
**기준 씬:** `CompanyGame/Assets/Scenes/DaldongneWarmMap.unity`  
**목적:** 기존 파일 틀마다 책임을 하나씩 정하고, 구현 순서와 완료 조건을 제시한다.  
**기획 전제:** [달동네 게임 기획안](Daldongne_Game_Proposal.md)의 ‘골목 탐색 → 주민 부탁 → 가게 변화’를 첫 플레이 범위로 삼는다. 회사·은행·주식 등은 프로젝트의 파일명에서 추정한 후속 확장 후보이며 게임 규칙이 확정된 것은 아니다.

## 상태 읽는 법

- **구현됨:** 현재 씬에서 동작하는 로직이 있다. 게임 전체 기능이 완성됐다는 뜻은 아니다.
- **부분 구현:** 필드 또는 최소 초기화만 있다.
- **틀:** 클래스 선언 외에 실제 동작이 없거나 `Start`/`Update`가 비어 있다.

현 상태에서 동작하는 핵심은 `DaldongneVillageWalker`, `DaldongneMapCamera`, `DaldongnePlayerAppearance`, `DaldongneAvatarMotion`이다. `GameManager`는 싱글턴 중복 제거, `PlayerManager`는 컴포넌트 참조 수집, `PlayerStats`·`TimeManager`·`EconomyManager`는 값 보관까지만 한다. 나머지는 대부분 틀이다. **클래스명이나 파일명이 있다고 기능이 구현된 것은 아니다.**

## 1. 먼저 결정할 구조

1. **이동의 주인은 하나.** 현재 씬에서는 `DaldongneVillageWalker`가 `CharacterController`와 입력을 소유한다. `PlayerMovement`에 두 번째 `CharacterController.Move`를 넣으면 중복 이동한다. 본게임으로 전환할 때는 `PlayerMovement`가 이동을 맡고 `DaldongneVillageWalker`의 입력·이동은 제거하거나 이 컴포넌트를 맵 검사용으로만 남긴다.
2. **카메라의 주인도 하나.** 현재 `DaldongneMapCamera`와 워커의 카메라 전환이 동작한다. `PlayerCameraController`를 활성화할 때 두 스크립트가 같은 카메라의 위치를 매 프레임 쓰지 않도록 상태를 통합한다.
3. **입력은 `InputManager`에서 액션으로 전달.** 지금은 맵 카메라, 워커, 외형 스크립트가 각각 키를 읽는다. 대화창이나 휴대폰이 열렸을 때 걷기·외형 전환이 동시에 실행되지 않도록 `Explore`/`UI` 입력 상태를 분리한다.
4. **정적 정의와 실행 상태를 분리.** 아이템·퀘스트·대화·회사·주식의 기본 정보는 `ScriptableObject` 데이터 에셋 후보, 소유량·진행도·가격 등 변하는 값은 일반 직렬화 상태 데이터로 둔다. 저장 상태 클래스에 `MonoBehaviour`를 붙이지 않는다.
5. **상호작용은 공통 규약 하나.** NPC·가게·게시판·아이템을 플레이어가 같은 거리 판정과 안내 UI로 사용한다. 각 시스템이 별도 키 입력을 읽지 않는다.
6. **안정된 ID를 사용.** `bakery_owner`, `quest_bakery_01`, `item_flour` 같은 ID를 데이터와 저장 파일에 공유한다. 씬 오브젝트 이름이나 표시용 한글 이름을 저장 키로 쓰지 않는다.

## 2. Core — 게임 흐름·시간·저장·이벤트 (14개)

| 파일 | 현재 | 구현할 책임과 주요 기능 |
|---|---|---|
| `Core/GameManager.cs` | 부분 구현 | `Boot`, `Title`, `Exploration`, `Dialogue`, `Paused` 상태를 관리한다. 시작 시 필수 서비스 참조를 검사하고 입력·UI 상태 전환을 조정한다. 기존 싱글턴 중복 제거는 유지한다. |
| `Core/InputManager.cs` | 틀 | Unity Input System의 이동·달리기·상호작용·취소·전체보기·외형 선택 액션을 한곳에서 받아 현재 게임 상태에 맞게 전달한다. 대화 중 이동 잠금, 키보드/패드 바인딩 변경을 고려한다. |
| `Core/SceneLoadManager.cs` | 틀 | 타이틀↔마을 씬 전환, 비동기 로드 진행률, 중복 로드 방지, 로드 후 플레이어 시작 위치 복원을 맡는다. |
| `Core/TimeManager.cs` | 부분 구현 | 현재 `day/hour/minute`를 단일 게임 시계로 만든다. 시간 진행·일시정지·하루 넘김·`TimeChanged` 발행, 저장·복원을 제공한다. 현실 시간과 게임 시간 배율은 설정값으로 둔다. |
| `Core/DayNightManager.cs` | 틀 | `TimeManager` 값을 받아 태양 방향·조도·하늘/실내 조명·가로등을 바꾼다. 직접 시간을 증가시키지 않는다. |
| `Core/EventBus/EventBusManager.cs` | 틀 | 타입이 정해진 이벤트 구독·해제·발행 경로를 제공한다. 씬 전환/비활성화 시 구독이 남지 않도록 한다. 빈 매 프레임 `Update`는 불필요하다. |
| `Core/EventBus/GameEvents.cs` | 틀 | `QuestAccepted`, `QuestCompleted`, `ItemAdded`, `MoneyChanged`, `TimeChanged`, `RelationshipChanged`처럼 공통 이벤트의 데이터 형식을 정의한다. 현재 `MonoBehaviour`이지만 이벤트 정의는 일반 타입으로 바꾸는 편이 적합하다. |
| `Core/SaveManager.cs` | 틀 | 여러 `SaveData`를 하나의 버전 있는 저장 루트로 모아 직렬화·역직렬화한다. 임시 파일에 쓴 뒤 교체하고, 손상 파일/이전 버전 처리, 새 게임, 자동 저장 시점을 책임진다. |
| `Core/SaveData/PlayerSaveData.cs` | 틀 | 플레이어 위치·방향·선택 외형·능력치·인벤토리 참조를 저장하는 순수 데이터. 현재 `MonoBehaviour`를 제거해야 한다. |
| `Core/SaveData/QuestSaveData.cs` | 틀 | 퀘스트 ID별 상태(`Locked/Active/Completed`), 목표 카운트, 보상 수령 여부를 보관한다. 퀘스트 정의 자체를 복사하지 않는다. |
| `Core/SaveData/NPCSaveData.cs` | 틀 | NPC ID별 관계도·완료 대화·필요하면 현재 위치/일정 상태를 보관한다. |
| `Core/SaveData/EconomySaveData.cs` | 틀 | 지갑 잔액과 거래 기록/후속 경제 상태를 보관한다. 현금의 원천은 `EconomyManager` 하나로 정한다. |
| `Core/SaveData/PropertySaveData.cs` | 틀 | 소유 또는 개선한 건물 ID와 개선 단계, 배치한 장식 ID를 보관한다. 씬 오브젝트 참조를 직접 저장하지 않는다. |
| `Core/SaveData/TimeSaveData.cs` | 틀 | 날짜·시각·시간 배율 등 `TimeManager` 복원에 필요한 값만 보관한다. |

**Core 완료 기준:** 새 게임 → 베이커리 의뢰 완료 → 종료·재실행 → 위치·외형·시간·의뢰·가게 변화가 동일하게 돌아온다. 타이틀↔마을 이동을 반복해도 매니저가 중복되지 않는다.

## 3. Player·World — 조작과 마을 시점 (9개)

| 파일 | 현재 | 구현할 책임과 주요 기능 |
|---|---|---|
| `Player/PlayerManager.cs` | 부분 구현 | 현재 참조 수집을 유지하고, 필수 `Movement/Stats/Interaction` 존재 검증, 게임 상태에 따른 활성화, 저장·복원 연결을 맡는다. 다른 매니저의 규칙을 직접 실행하지 않는다. |
| `Player/PlayerMovement.cs` | 틀 | 본게임 이동 담당으로 채택할 경우 카메라 기준 이동, 달리기, 중력, 회전, 계단 이동, 이동 잠금, 안전 위치 복귀를 구현한다. 현재 워커와 같은 오브젝트에서 동시에 이동시키지 않는다. |
| `Player/PlayerStats.cs` | 부분 구현 | 현재 체력·스트레스·지능·매력·체력·농사·요리 수치의 초기값과 상하한, 증감 함수, 이벤트, 저장값 적용을 구현한다. 첫 플레이에서 실제로 쓰지 않는 스탯은 UI에 노출하지 않는다. |
| `Player/PlayerInteraction.cs` | 틀 | 가장 가까운 사용 가능 대상 탐색, 거리·방향 판정, 대상 강조, `Interact` 호출, 대화 중 중복 실행 방지를 맡는다. |
| `Player/PlayerCameraController.cs` | 틀 | 본게임 카메라의 걷기/전체보기 상태, 플레이어 추적, 장애물 가림 대책, 대화 시 시점 조정을 맡는다. 기존 맵 카메라와 기능을 합치거나 교체해야 한다. |
| `World/DaldongneVillageWalker.cs` | 구현됨 | 현재 맵의 `CharacterController` 보행·달리기·중력·추적 카메라·리셋 기능이다. 처음에는 유지하되 입력과 카메라 상태의 외부 제어 API를 추가한다. `PlayerMovement`로 이관하면 이동 부분을 이중 실행하지 않는다. |
| `World/DaldongneMapCamera.cs` | 구현됨 | 마우스 휠 확대, 오른쪽 회전, 가운데 이동, `T` 위에서 보기, `Home` 기본 시점을 제공한다. 본게임에서는 대화·휴대폰 UI가 마우스를 사용할 때 조작을 잠그는 연결이 필요하다. |
| `World/DaldongnePlayerAppearance.cs` | 구현됨 | 여자·남자 외형 활성화가 동작한다. 선택값 저장·복원, 게임 메뉴와 입력 연동, 선택 중 애니메이션·카메라 상태 유지가 후속 작업이다. |
| `World/DaldongneAvatarMotion.cs` | 구현됨 | 부품 관절을 회전해 대기·걷기 동작을 만든다. 속도/지면 상태 연동, 전환 부드러움, 계단에서 발 모양 확인을 개선한다. Humanoid Animator가 아닌 현 모델 방식에 맞춘다. |

**Player·World 완료 기준:** 남녀 어느 외형이든 대화 대상 앞까지 이동해 상호작용할 수 있고, 대화 중에는 이동/카메라 키가 대화 입력과 충돌하지 않는다. 기존 맵의 37개 통로에서 캐릭터 충돌 크기 0.35m×1.8m를 유지한다.

## 4. Gameplay — 첫 플레이 루프 (11개)

| 파일 | 현재 | 구현할 책임과 주요 기능 |
|---|---|---|
| `Gameplay/InteractionManager.cs` | 틀 | NPC·게시판·가게·줍는 물품이 공유하는 상호작용 요청을 받아 범위/조건을 검사하고 실제 대상 기능에 전달한다. `PlayerInteraction`은 대상을 찾고, 이 매니저는 요청의 일관성을 관리한다. |
| `Gameplay/Quest/QuestData.cs` | 틀 | 퀘스트 ID, 제목, 목표 유형·대상 ID·필요 수량, 선행 조건, 보상, 완료 대사/가게 변화 정의. 현재 `MonoBehaviour` 대신 `ScriptableObject` 정의 에셋이 적합하다. |
| `Gameplay/Quest/QuestManager.cs` | 틀 | 정의 등록, 수락·목표 진행·완료·보상 지급, 이벤트 연동, 저장 상태 복원을 담당한다. 아이템 제출은 인벤토리 감소와 퀘스트 완료가 함께 성공해야 한다. |
| `Gameplay/Item/ItemData.cs` | 틀 | 아이템 ID, 표시명, 설명, 아이콘, 최대 중첩 수, 용도/가격의 정적 정의. `ScriptableObject` 후보. |
| `Gameplay/Item/ItemManager.cs` | 틀 | 월드 아이템 생성·획득·소모·전달 요청 및 아이템 정의 조회. 소유 개수 자체는 인벤토리가 관리한다. |
| `Gameplay/Item/InventorytManager.cs` | 틀 | 슬롯/아이템 ID별 수량, 용량·중첩 제한, `CanAdd/Add/CanRemove/Remove`와 UI 알림을 맡는다. 이름의 `Inventoryt` 오타를 `InventoryManager`로 정리하되 Unity 참조를 확인하며 이관한다. |
| `Gameplay/Job/JobManager.cs` | 틀 | 반복 가능한 심부름/아르바이트의 수락·진행·정산·일일 제한을 맡는다. 첫 3개 스토리 의뢰는 `QuestManager`에 두고 반복 일만 분리한다. |
| `Gameplay/Phone/PhoneManager.cs` | 틀 | 휴대폰 화면 열기/닫기, 탭 전환, 알림 배지와 입력 잠금. 퀘스트 목록·지도·메시지 UI의 진입점으로 쓴다. |
| `Gameplay/Phone/SNSSystem.cs` | 틀 | 주민/가게 소식 피드, 게시물 잠금 해제와 반응의 상태를 맡는다. 첫 플레이에서는 의뢰 완료 후 소식 1건을 보여주는 정도로 시작한다. |
| `Gameplay/Consulting/ConsultingManager.cs` | 틀 | 후속 가게 성장 기능: 주민/상점 요청을 분석해 개선안·비용·효과를 제시하고 선택 결과를 건물 상태에 반영한다. MVP에서는 필요하지 않다. |
| `Gameplay/PenaltyManager.cs` | 틀 | 기한을 둔 일이나 계약에 실패했을 때 적용할 규칙을 한곳에서 처리한다. 기한·벌점 규칙이 정해지기 전에는 구현을 보류하고 퀘스트 실패를 강제하지 않는다. |

**Gameplay 완료 기준:** 역에서 출발해 게시판 확인 → 물품 획득 → 베이커리 전달 → 완료 대사·진열 변화·저장까지 하나의 경로로 진행된다. 동일 보상을 두 번 받을 수 없다.

## 5. NPC — 주민과 대화 (5개)

| 파일 | 현재 | 구현할 책임과 주요 기능 |
|---|---|---|
| `NPC/NPCManager.cs` | 틀 | NPC ID와 씬 인스턴스 등록·조회, 생성/퇴장, 대화·일정 컴포넌트 참조 연결. 베이커리 주인과 카페 주민 2명부터 시작한다. |
| `NPC/DialogueData.cs` | 틀 | 대화 노드 ID, 화자, 본문, 선택지, 조건, 다음 노드, 이벤트/퀘스트 연결의 정적 정의. `ScriptableObject` 또는 JSON 정의 중 하나로 통일한다. |
| `NPC/DialogueManager.cs` | 틀 | 대화 시작·문장 넘김·선택지·조건 판정·종료, `QuestManager` 연결, UI 출력과 입력 잠금을 맡는다. |
| `NPC/NPCRelationshipManager.cs` | 틀 | NPC별 친밀도, 증감 사유, 단계별 대사/의뢰 잠금 해제, 저장·복원을 맡는다. 첫 플레이에서는 필요하다면 주민 1명의 상태 변화만 쓴다. |
| `NPC/NPCScheduleManager.cs` | 틀 | 시각에 따른 NPC 위치/행동 상태를 예약하고 `TimeManager` 변경을 구독한다. 계단이 많은 맵에는 AI 이동을 바로 가정하지 말고, 먼저 지정 지점 전환 또는 수동 경유점을 쓴다. NavMesh는 별도 제작·검증이 필요하다. |

**NPC 완료 기준:** 주민에게 한 번 말을 걸어 의뢰를 받고, 완료 후 다른 대사가 나오며, 게임을 재실행해도 대화 상태가 유지된다.

## 6. Economy — 가게 운영 확장 (9개)

| 파일 | 현재 | 구현할 책임과 주요 기능 |
|---|---|---|
| `Economy/EconomyManager.cs` | 부분 구현 | 현재 `money` 필드를 `TrySpend/AddMoney` 같은 단일 거래 API로 감싼다. 음수 잔액·중복 보상 방지, 돈 변경 이벤트, 저장·복원을 제공한다. |
| `Economy/Company/CompanyData.cs` | 틀 | 회사/가게 ID, 종류, 기본 자본, 상품·직원·운영 단계 등 정적 정의. 실제 잔액과 매출은 실행 상태에 둔다. 회사 경영 장르 확정 후 데이터 범위를 결정한다. |
| `Economy/Company/CompanyManager.cs` | 틀 | 소유 가게 목록, 영업 상태, 시설 개선 단계, 일일 결과와 시각적 변화 적용. MVP에서는 베이커리의 한 단계 변화만 담당하게 시작한다. |
| `Economy/Property/LandPloManager.cs` | 틀 | 이름상 토지 필지 관리용. 필지 ID·경계·사용 가능 여부·건물 연결을 맡길 수 있다. `LandPlo` 오타는 `LandPlotManager`로 변경을 검토하되 에셋 참조를 함께 확인한다. MVP 범위 밖. |
| `Economy/Property/PropertyManager.cs` | 틀 | 소유권·개선 레벨·건물 잠금/해제, 공간 사용 조건, 저장·복원. 가게의 수익 계산과 건물의 소유 상태를 구분한다. MVP에서는 베이커리 장식 변화 정도만 필요하다. |
| `Economy/Bank/BankManager.cs` | 틀 | 예금·인출·대출·상환 및 이자 계산을 제안한다. 현재 **파일명은 `BankManager.cs`, 클래스명은 `Bank`**이므로 컴포넌트/참조 관계부터 바로잡아야 한다. MVP 범위 밖. |
| `Economy/StockMarket/StockData.cs` | 틀 | 종목 ID·이름·가격 변동 규칙·위험도 같은 정적 정의. 실제 보유 수량과 현재 가격은 별도 실행 상태. MVP 범위 밖. |
| `Economy/StockMarket/StockMarketManager.cs` | 틀 | 매수·매도, 잔고/보유 수량 확인, 가격 갱신과 거래 기록. 돈 이동은 `EconomyManager`를 통해 원자적으로 처리한다. MVP 범위 밖. |
| `Economy/Tax/TaxManager.cs` | 틀 | 과세 대상·시점·계산·납부·미납 상태를 맡길 수 있다. 세금 규칙과 재미상 필요성이 결정되기 전에는 만들지 않는다. MVP 범위 밖. |

**Economy 완료 기준(후속):** 한 거래가 잔액과 소유 상태를 함께 갱신하며, 저장 후 재실행해도 돈·소유권이 일치한다. 은행/주식/세금은 각 시스템의 게임 규칙 확정 뒤 검증 기준을 추가한다.

## 7. UI — 플레이어에게 상태 보여주기 (1개)

| 파일 | 현재 | 구현할 책임과 주요 기능 |
|---|---|---|
| `UI/UIManager.cs` | 틀 | HUD, 상호작용 안내, 대화창, 의뢰 목표, 인벤토리/휴대폰, 돈·시간 표시를 패널 단위로 열고 닫는다. 매니저 상태를 읽어 표시하되 게임 규칙이나 재화를 직접 변경하지 않는다. 텍스트·버튼의 마우스/키보드 포커스도 관리한다. |

**UI 완료 기준:** 첫 플레이에서 화면만 보고 다음 목적지를 알 수 있고, 대화·인벤토리·전체 맵 화면을 열었다 닫아도 입력이 정상으로 돌아온다.

## 8. 실제로 연결할 첫 의뢰

```text
역 앞 플레이어
  → PlayerInteraction: 게시판이 범위 안인지 감지
  → InteractionManager: 게시판 사용 조건 확인
  → UIManager: 의뢰 목록 표시
  → QuestManager: quest_bakery_01 수락
  → ItemManager / InventoryManager: 물품 획득·소유량 갱신
  → NPCManager / DialogueManager: 베이커리 주인에게 전달
  → QuestManager: 목표 완료, 보상 1회 지급
  → CompanyManager 또는 PropertyManager: 베이커리 진열 1단계 변경
  → SaveManager: 진행·재화·외형·가게 상태 기록
```

데이터 에셋에는 위 ID와 표시 문구를 넣고, 실행 중 상태는 매니저가 소유한다. UI와 NPC는 상태 변경 이벤트를 구독해 화면/대사를 갱신한다. 저장 로드 시에는 퀘스트·가게 상태를 먼저 복원한 뒤 UI와 NPC 표시를 새로 계산한다.

## 9. 구현 순서

| 단계 | 먼저 구현할 파일 | 플레이 가능한 결과 |
|---|---|---|
| 0. 정리 | `GameManager`, `InputManager`, `PlayerManager`, 기존 `World/*` 연동 | 이동·카메라·입력 소유자 하나씩 정리. 맵 플레이 유지 |
| 1. 상호작용 | `PlayerInteraction`, `InteractionManager`, `UIManager` | 게시판/NPC 접근 시 안내와 사용 가능 |
| 2. 첫 대화·의뢰 | `DialogueData/Manager`, `QuestData/Manager`, `NPCManager` | 베이커리 주인에게 의뢰 수락·완료 |
| 3. 물품·보상 | `ItemData/Manager`, `InventorytManager`, `EconomyManager` | 물품 획득·제출과 보상 1회 지급 |
| 4. 눈에 보이는 변화 | `CompanyManager` 또는 `PropertyManager` 한쪽 선택 | 진열대/조명이 의뢰 완료 후 바뀜 |
| 5. 저장 | `SaveManager`, 필요한 `SaveData` | 종료 후 다시 열어도 상태 유지 |
| 6. 확장 | `TimeManager`, `DayNightManager`, `NPCScheduleManager`, `PhoneManager`, `SNSSystem`, `JobManager` | 시간대·주민 일정·반복 의뢰 |
| 7. 장르 확정 뒤 | 은행·주식·세금·토지·컨설팅·벌점 | 회사/경제 시뮬레이션 확장 |

**첫 개발 스프린트의 최소 목표:** 단계 0~2만 구현해 ‘걸어가서 NPC에게 한 번 말을 걸고 의뢰를 수락하는 것’까지 연결한다. 의뢰가 끝까지 동작하기 전 경제 확장 코드를 먼저 늘리지 않는다.

## 10. 구현 전 확인할 위험과 기준

- **현재 Player 계열은 맵 워커와 별개 틀이다.** 씬에 `PlayerManager`/`PlayerMovement`를 무작정 추가하면 기존 워커와 충돌할 수 있다. 한 오브젝트의 이동 컴포넌트는 하나만 활성화한다.
- **`SaveData` 6개는 현재 모두 `MonoBehaviour`다.** 씬 컴포넌트의 인스턴스 ID를 직렬화하는 방식으로 저장하지 말고 순수 데이터로 전환한다. 파일 형식에 버전 번호를 둔다.
- **`GameEvents`도 현재 `MonoBehaviour`다.** 이벤트 타입 정의와 이벤트 전달자를 분리한다. 씬 재로드 후 구독이 남는지 확인한다.
- **오타/이름 불일치:** `InventorytManager`, `LandPloManager`, `BankManager.cs` 안의 `Bank` 클래스. 이름 수정은 Unity의 `.meta` 참조와 프리팹/씬 참조를 점검하면서 한다.
- **AI 이동:** 계단과 연결로의 플레이어 통과는 검증됐지만 NPC NavMesh는 만들어지지 않았다. 자동 보행을 넣을 때 계단 링크·옹벽·난간의 길찾기를 별도로 시험한다.
- **플랫폼:** 현재 조작은 PC 키보드/마우스 기준이다. 모바일을 목표로 하면 가상 조작과 UI를 별도 설계한다.

## 근거 파일

- [기획안](Daldongne_Game_Proposal.md)
- [마을 에셋·동선 안내](../ArtSource/Daldongne/WARM_VILLAGE.md)
- [플레이어 프리팹 안내](../CompanyGame/Assets/Art/Daldongne/Players/README.md)
- [Unity 스크립트 폴더](../CompanyGame/Assets/Scripts/)
