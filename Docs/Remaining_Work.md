# 남은 작업

기준: 2026-10-04 `main`. 끝난 항목은 지우고, 새로 생긴 항목은 아래 표에 추가합니다.

## 1. 팀이 정할 것

| 항목 | 내용 |
| --- | --- |
| 멀티 라이브러리 | 방장 호스트용 Netcode for GameObjects, FishNet, Mirror 중 하나. 온라인 매칭을 하면 Unity Relay/Lobby 같은 서비스도 함께 고릅니다. |
| 기획의 "월" | 달력은 계절당 20일인데 세금 "매월 30일", 월급 "매달 5일"이 있습니다. 기준을 정해야 `GameTime`에 넣을 수 있습니다. |
| 아이템 ID 규칙 | `ItemData.itemId`는 저장·통신에 쓰는 고정 ID입니다. 이름 규칙(예: `food.triangle_kimbap`)을 합의합니다. |
| 씬 담당자 | 씬마다 수정 담당자를 정해 [Git_Collaboration.md](Git_Collaboration.md)에 표로 적습니다. |
| Unity 버전 | 6000.4.5f1에 보안 공지가 있습니다. 출시 전에 팀 전체가 같은 버전으로 올립니다. |
| GitHub 브랜치 보호 | 문서 규칙(main·Dev 직접 push 금지)을 저장소 설정으로 켭니다. 관리자 권한이 필요합니다. |

## 2. 개발

### 멀티플레이 (구조는 준비됨)

1. 라이브러리를 `Net` 어셈블리에 추가하고, 방 만들기·참가 시 `GameSession.Begin(Host/Client)`을 부릅니다.
2. 방장 → 참가자로 보낼 상태: 게임 시간, 계좌 잔액, 주가, 회사 매출, 뉴스·SNS, 다른 플레이어 위치.
3. 계산을 바꾸는 쓰기(은행 입출금, 주식 매매, 토지 구매, 회사 등록)는 `GameSession.IsAuthority`일 때만 실행하고, 참가자는 방장에게 요청을 보냅니다.
4. 플레이어가 여러 명이 되므로 `PlayerSpawner`가 접속한 사람 수만큼 플레이어를 만들게 바꿉니다.

### 기능

| 항목 | 위치 |
| --- | --- |
| 로그인 화면과 `PlayerData` 연결 | Net, UI |
| 저장·불러오기 (`SaveManager`, `SaveData`) | Core |
| 이벤트 시스템 (기획 5장: 조건 → 효과 → 뉴스·SNS) | Core/Event. 지금 주식 안의 `MarketEvents`를 여기로 옮깁니다. |
| 3×3 제작 슬롯과 조합 데이터 | Gameplay |
| 아르바이트, 낚시, 로또·도박 | Gameplay, Economy |
| 차량, 펫, 가구 배치 | Gameplay |
| 부동산·건축(한대건설), 세금 | Economy |
| NPC별 거래 목록 (`NpcTrader`), NPC, SNS 댓글용 로컬 LLM | NPC, 저장소 루트 `AI/` (모델 파일은 커밋하지 않음) |
| 키 추가: Ctrl 앉기, Q 퀘스트, C 차량 등록, F 차량 탑승 | `Core/GameInput.cs`. F는 지금 아이템 줍기라 정리가 필요합니다. |

## 3. 정리 잔여

| 항목 | 내용 |
| --- | --- |
| `Docs/Unity_Script_Implementation_Plan.md` | 예전 폴더 구조와 상태 기준입니다. 경로·상태를 지금 구조로 갱신하거나 [Architecture.md](Architecture.md)로 대체합니다. |
| `Docs/Database/` | 코드 근거 이름이 일부 예전 것입니다 (`PropertyManager` → `BankManager`). 방장 호스트 방식과의 관계는 [Server.md](Server.md#db-설계-문서와의-관계). |
| `SmallMapTemplate` 씬 | 카메라에 `PlayerCameraController`가 없습니다. Validate Maps가 알려 줍니다. |
| `Art/Daldongne/Players/PlayerMale·PlayerFemale.prefab` | 예전 플레이어 프리팹입니다. 지금 플레이어는 `Resources/Player.prefab`이고, 캐릭터 검사 도구만 이 둘을 봅니다. |
| `DaldongneVillageWalker` | 예전 호환용 컴포넌트가 플레이어 프리팹에 붙어 있습니다. `PlayerMovement`로 바꾸면 지울 수 있습니다. |
| `com.unity.ai.inference` 패키지 | 프로젝트 코드에서 쓰지 않습니다. 로컬 LLM에 쓸 계획이 없으면 뺍니다. |
| 큰 이미지·사운드의 LFS | png 등은 일반 Git에 있습니다. 아트가 늘기 전에 LFS로 옮길지 정합니다. |

## 4. 확인 필요

- 편의점 1인칭 시점 수정과 인벤토리 오류 수정(커밋 `83b9f32`)은 Play로 다시 확인해야 합니다.
