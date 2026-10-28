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
| GitHub 브랜치 보호 | 문서 규칙(작업자는 main·Dev에 직접 push 금지, PR 없이 관리자가 합침)에 맞게 저장소 설정을 정합니다. 관리자 권한이 필요합니다. |

## 2. 개발

### 멀티플레이 (테스트 서버 구현됨, 2026-10-12)

자체 Node 서버(`Server/`, WebSocket)로 방 만들기·참가·방장 이관이 동작합니다. 방장에게는 특별한 권한이 없고, 공용 월드 변경은 op로 보내 서버 순서대로 모두에게 적용합니다. 시작 화면(`Scenes/Title.unity`: 방 만들기·참가하기·설정)에서 계정 가입, 방 만들기·참가를 하고, 게임 중 ESC 메뉴에서 방장 넘기기·방 나가기를 합니다. 사용법은 [Server.md](Server.md)의 "2인 테스트".

| 항목 | 내용 |
| --- | --- |
| 시작 화면 남은 것 | 볼륨 같은 일반 설정은 게임에 아직 없어서 설정 화면에는 서버·이름·프로필 슬롯·계정 초기화만 있습니다. 마지막으로 있던 맵 위치로 이어서 시작하는 기능은 없습니다(항상 달동네 기본 위치). |
| 실제 2인 플레이 확인 | 클라이언트 두 대로 이관, 땅 구매 경쟁, 재접속을 Play에서 확인해야 합니다. |
| 신뢰 한계 | 참가자는 방장 스냅샷을 그대로 믿고, 서버는 인벤토리와 개인 데이터를 검증 없이 저장합니다. 테스트 서버 한계로 남깁니다. |
| 원격 아바타 | 항상 남자 외형입니다. |
| 피드백 문구 | 차 잠금·해체, SNS 좋아요는 실패해도 결과 문구가 없습니다. |

### 기능

| 항목 | 위치 |
| --- | --- |
| 저장·불러오기: 월드(`WorldSave`)와 개인(`PersonalSave`)은 구현됨. 예전 `SaveManager`, `SaveData` 빈 클래스 정리 | Core, Gameplay |
| 이벤트 시스템 (기획 5장: 조건 → 효과 → 뉴스·SNS) | Core/Event. 지금 주식 안의 `MarketEvents`를 여기로 옮깁니다. |
| 3×3 제작 슬롯과 조합 데이터 | Gameplay |
| 아르바이트, 낚시, 로또·도박 | Gameplay, Economy |
| 차량 나머지 차종(소나티네 등), 펫, 가구 배치 | Gameplay |
| 부동산·건축(한대건설), 세금 | Economy |
| NPC별 거래 목록 (`NpcTrader`), NPC, SNS 댓글용 로컬 LLM | NPC, 저장소 루트 `AI/` (모델 파일은 커밋하지 않음) |
| 키 추가: Ctrl 앉기, Q 퀘스트, C 차량 등록, F 차량 탑승 | `Core/GameInput.cs`. F는 지금 아이템 줍기라 정리가 필요합니다. |

### 고시원·소유권 후속 (2026-10-04 고시원 작업에서 남은 것)

| 항목 | 내용 |
| --- | --- |
| 땅문서와 땅 구역 | 시스템(`PropertyZone`, `PropertyRegistry`)과 스프라이트(`Art/Items/Deeds/deed_land.png`)만 있습니다. 땅용 별도 맵을 만든 뒤 부지마다 `PropertyZone`(ID `land_<부지ID>`)과 "<이름> 땅문서" 아이템을 추가합니다. |
| 202호 테스트 주인 | 혼자 무단침입 로그를 보려고 202호 `seedOwner`를 "이웃"으로 두었습니다. 출시 전에 비웁니다. |
| 고시원 인테리어 | 방은 빈 3×3m입니다. 가구 배치를 시작하면 `Build Goshiwon Interior` 메뉴를 다시 돌리지 않습니다(씬을 덮어씀). |

### 차·NPC·교통·충돌 후속 (2026-10-12)

| 항목 | 내용 |
| --- | --- |
| 충돌 2단계 | `_temp/team/2026-10-12_vehicles-npc-multiplayer/collision_report.md`를 보고 `MapColliderRebuilder.Apply`를 실행할지 정합니다. 합쳐진 거리 소품 메시는 먼저 분할해야 합니다. |
| 교통 확장 | BusinessDistrict만 있습니다. 좌회전 없음, 비스듬한 교차로의 횡단보도 13곳 생략. |
| 시민 | 벤치는 CivicDistrict에만 있어 바깥에서 거의 앉지 않습니다. 도서관 시민은 책 고르는 동작이 없습니다. |

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
- 고시원: NPC 거래창이 열린 상태의 Space, 인벤토리가 가득 찼을 때 시작 집문서 재시도, 3인칭 카메라가 좁은 방·복도 벽을 뚫는지는 아직 Play로 확인하지 않았습니다.
- 플레이 종료 시 `Some objects were not cleaned up ... Inventory Session` 오류가 나옵니다. 고시원 작업 전부터 있었는지 확인이 필요합니다.
