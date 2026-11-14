# 달동네 플레이어 — 여자 / 남자

`Assets/Scenes/daldongnaemap.unity`에 적용되어 있습니다. Play를 누르고 Game 창을 클릭하면 바로 조작할 수 있습니다.

- `1`: 여자 캐릭터 / `2`: 남자 캐릭터 (둘 다 기본 옷: 반팔 티 + 긴바지)
- `WASD`: 이동 / `Shift`: 달리기
- `F`: 걷기 시점 ↔ 맵 전체 보기
- `R`: 역 앞 시작점으로 돌아가기

## 프리팹

- `PlayerFemale.prefab`, `PlayerMale.prefab`: 이동·충돌·외형 전환 기능을 포함한 플레이어. 씬에는 하나만 배치합니다. 현재 맵에는 기존 `Village walking preview` 오브젝트에 통합되어 있으므로 추가 배치할 필요가 없습니다.
- `FemaleVisual.prefab`, `MaleVisual.prefab`: 외형만 있는 프리팹. NPC 등에 재사용할 수 있습니다.
- 다른 씬에 플레이어 프리팹을 배치할 경우 `DaldongneVillageWalker`의 View Camera와 Spawn을 지정합니다. 카메라가 비어 있으면 MainCamera 태그 카메라를 찾습니다.
- 기본 성별: `DaldongnePlayerAppearance`의 Selected. 게임 중 선택은 해당 실행 동안 유지됩니다.

## 모델과 동작

현재 외형은 Meshy 관절형 몸(`MeshyMale`·`MeshyFemale`, Humanoid 뼈대 54개, 스킨드 메시 1개)입니다. 원본과 생성 방법은 저장소 루트 `ArtSource/Daldongne/Characters/README.md`를 보세요.

## 옷 (2026-11-11)

`Outfits/` 폴더에 기본 옷(반팔 티 + 긴바지)이 있습니다. 프리팹 루트의 `AvatarOutfit`이 옷을 입힙니다. 옷은 몸과 같은 뼈에 스키닝되어 어떤 자세에서도 몸을 따라 휘고, 옷이 덮는 피부는 숨깁니다. 만드는 방법은 `ArtSource/Daldongne/Characters/Outfits_20261111/README.md`에 있습니다.
