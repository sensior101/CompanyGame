# 프로젝트 구조와 설계 메모

기준: 2026-10-04 main. 기획 정본은 노션 「기획안 (수정본)」이다.

## 폴더 구조 (`CompanyGame/Assets/Scripts`)

| 폴더 | 담당 |
| --- | --- |
| Core | 시간, 씬 전환, 이벤트, 데이터 저장 |
| Economy | 은행, 회사, 화폐, 자산(토지·건축), 주식, 세금, 로또·도박 |
| Gameplay | 아이템, 제작, 직업·경영, 핸드폰(뉴스·SNS 포함), 퀘스트, 낚시, 차량, 펫, 가구 배치 |
| NPC | NPC 관리, SNS NPC 댓글(로컬 LLM 호출) |
| Player | 이동, 상호작용, 카메라, 스탯 |
| UI | 상태바, 시간·돈 표시, 채팅창 등 화면 |
| World | 맵 구성, 포털, 교통, 상호작용 지점 |
| Net | 서버 통신 |
| Editor | 에디터 전용 도구 (빌드에 포함되지 않음) |

저장소 루트: `Server/` (Node + Fastify + SQLite), `Docs/`, `ArtSource/`. 로컬 LLM 서버를 만들면 루트 `AI/`에 두고 모델 파일은 커밋하지 않는다.

비어 있는 클래스는 자리 잡기용이다. 구현할 때 채운다.

## 돈

- 은행 돈 = 계좌 잔액. `PropertyManager.Money`, 앱 결제는 `PropertyWallet`.
- 인벤토리 돈 = 지갑에 든 지폐·동전 아이템. `CashService`.
- 둘 사이는 출금(권종·장수 지정)과 입금(Space)으로만 오간다.

## A. 개발 전에 방향을 정할 것

1. **경제 계산 위치.** 기획은 서버당 10~20명이 경제를 공유한다(주식 호가 체결, 토지 3개·회사 1개 한도). 지금 주가·회사 매출·벌점·잔액은 각자 PC에서 계산한다. 경제 기능은 서버에서 계산하고 Unity는 표시와 입력만 하는 방향으로 만든다. 서버 언어(지금 TypeScript)와 실시간 동기화 라이브러리(Netcode, Mirror, FishNet 등)를 고른다.
2. **게임 시간.** 영업시간, 주식장(09~16시), 뉴스(08시), 세금은 모두 같은 시계를 봐야 하므로 서버 시간 기준으로 한다. 기획 확인 필요: 달력은 계절당 20일이라 "월"이 없는데 세금 "매월 30일", 월급 "매달 5일"이 있다.
3. **은행 이름.** 계좌 잔액이 `PropertyManager`에 있고 `BankManager`도 따로 있다. 기획에서 부동산은 토지 거래이므로 계좌는 `BankManager` 쪽으로 모은다.

## B. 시스템을 붙일 때 같이 처리할 것

4. **매니저 배치.** 폰·주식·시계·회사·SNS·신고 매니저는 커밋된 씬에 없다(에디터 메뉴로 열린 씬에 붙이는 방식). 매니저 생성 방식도 씬 배치, 런타임 자동 생성, 에디터 메뉴로 섞여 있다. Systems 프리팹 하나에서 만들고 씬 전환 시 유지하는 방식으로 통일한다.

## C. 개발하면서 지킬 것

5. **시스템 연결.** 정적 `Instance`와 `FindAnyObjectByType`로 서로 찾는다. 기획의 이벤트 시스템(조건 → 효과 → 뉴스·SNS)은 Core/Event에 독립 모듈로 두고, 시스템 간 알림 방식은 하나로 통일한다. 지금 `MarketEvents`는 주식 안에 있다.
6. **키 입력.** 키가 스크립트마다 직접 적혀 있다(Space는 `PlayerInteraction`, `MapPortal`, `PlayerInventory`가 나눠 씀, Tab은 카메라와 인벤토리 UI). 기획에 Ctrl, C(길게 누르기 포함), F(차량 탑승, 지금은 공격)가 추가된다. `InputSystem_Actions.inputactions`(지금 미사용)에 상황별 키를 모으고 `InputManager`가 맡는다.
7. **아이템 데이터.** `ItemData`(ScriptableObject)는 유지한다. `ItemCategory`에 음식·재료·가구·차량·낚시를 추가하고, 3×3 조합 데이터 구조를 정한다. `itemId`는 서버와 공유하는 고정 ID라 이름 규칙을 합의한다.
8. **큰 파일과 씬.** `InventoryUI`, `CashService`, `PlayerInventory`는 700줄대라 동시 수정 시 충돌이 잦다. `daldongnaemap.unity`는 8만 줄이다. 씬별 담당자를 정하고 수정은 프리팹 단위로 한다.
9. **프로젝트 설정.** asmdef(Core / Economy / Gameplay / UI / Editor)를 나누면 컴파일이 빨라지고 의존 방향을 강제할 수 있다. Unity 쪽 테스트는 아직 없다.

## 해결됨 (2026-10-04)

- `.gitignore`: Claude·Archify 작업 폴더, AI QA 결과물(검증 JSON, 캡처 PNG, 로그) 제외. 기존 결과물 299개 추적 해제.
- 에디터 스크립트를 `Scripts/Editor` 하나로 모음. 일회용 `AgentScripts` 삭제.
- `Assets/Screenshots`, 오타 클래스 `InventorytManager`, 템플릿 `Readme.asset`, 빈 폴더 `.meta` 삭제.
- `Scripts/Net` 추가, `PlayerData` 이동.
- 메시 에셋 16개의 오브젝트 이름을 파일 이름과 맞추고, 생성 코드도 같은 이름을 쓰게 수정.

## 남은 정리

- 루트 `README.md`가 예전 기획 복사본이다. 저장소 안내로 바꾼다.
- `ArtSource` README 일부가 삭제된 `AgentScripts`를 언급한다.
- Unity 6000.4.5f1에 보안 공지가 있다. 출시 전에 팀 전체가 같은 버전으로 올린다.
