# 달동네 플레이어 — 여자 / 남자

`Assets/Scenes/DaldongneWarmMap.unity`에 적용되어 있습니다. Play를 누르고 Game 창을 클릭하면 바로 조작할 수 있습니다.

- `1`: 여자 캐릭터 (청록색 가디건, 단발머리, 크로스백)
- `2`: 남자 캐릭터 (황토색 재킷, 짧은 머리, 백팩)
- `WASD`: 이동 / `Shift`: 달리기
- `F`: 걷기 시점 ↔ 맵 전체 보기
- `R`: 역 앞 시작점으로 돌아가기

## 프리팹

- `PlayerFemale.prefab`, `PlayerMale.prefab`: 이동·충돌·외형 전환 기능을 포함한 플레이어. 씬에는 하나만 배치합니다. 현재 맵에는 기존 `Village walking preview` 오브젝트에 통합되어 있으므로 추가 배치할 필요가 없습니다.
- `FemaleVisual.prefab`, `MaleVisual.prefab`: 외형만 있는 프리팹. NPC 등에 재사용할 수 있습니다.
- 다른 씬에 플레이어 프리팹을 배치할 경우 `DaldongneVillageWalker`의 View Camera와 Spawn을 지정합니다. 카메라가 비어 있으면 MainCamera 태그 카메라를 찾습니다.
- 기본 성별: `DaldongnePlayerAppearance`의 Selected. 게임 중 선택은 해당 실행 동안 유지됩니다.

## 모델과 동작

Unity 네이티브 로우폴리 메시·URP 재질로 제작했습니다. 머리, 의상, 팔다리, 가방은 개별 부품이며 색은 Materials 폴더에서 수정할 수 있습니다. `DaldongneAvatarMotion`이 관절 회전으로 대기·걷기·달리기 동작을 만듭니다. Humanoid 리타기팅용 스킨드 모델/Animator 클립은 아닙니다.

두 버전 모두 충돌체 반지름 0.35m, 높이 1.8m, 계단 높이 0.23m를 사용합니다. 장식에는 별도의 충돌체가 없습니다. Play 모드에서 37개 경로를 각 버전으로 양방향 이동하는 148회 검사, 외형 전환, 관절 동작 및 카메라 전환 검사를 통과했습니다.

생성 스크립트: `AgentScripts/DaldongnePlayersBuild.cs`
검증 스크립트: `AgentScripts/DaldongnePlayersQA.cs`
검증 결과: 프로젝트 상위 폴더 `ArtSource/Daldongne/players_validation.json`
