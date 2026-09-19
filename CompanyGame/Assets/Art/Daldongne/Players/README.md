# 달동네 플레이어 — 여자 / 남자

`Assets/Scenes/daldongnaemap.unity`에 적용되어 있습니다. Play를 누르고 Game 창을 클릭하면 바로 조작할 수 있습니다.

- `1`: 여자 캐릭터 (크림색 야구모자, 긴 밤색 머리와 리본, 세이지색 재킷, 치마, 레이스업 부츠)
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

Unity 네이티브 로우폴리 메시·URP 재질을 사용합니다. 여자 캐릭터는 제공된 사진을 참고한 모델로, 18,862 삼각형·236개 원본 파츠·관절 9개·관절별 렌더러 9개·재질 1개로 구성됩니다. 최신 장난감 캐릭터 얼굴 참고에 맞춰 큰 원형 눈, 양쪽이 정렬된 짙은 갈색 동공, 아이보리색 흰자와 작은 반사광을 적용했습니다. 얼굴 표면을 따르는 얕은 눈 곡면에 부드러운 위 눈꺼풀 윤곽과 눈마다 짧은 속눈썹 두 가닥을 더했습니다. 여자 캐릭터의 메시·팔레트는 `ReferenceGirl` 폴더, 남자 캐릭터의 재질은 `Materials` 폴더에 있습니다. `DaldongneAvatarMotion`이 관절 회전으로 대기·걷기·달리기 동작을 만듭니다. Humanoid 리타기팅용 스킨드 모델/Animator 클립은 아닙니다.

얼굴은 살구색 버튼 코, 작은 ∧ 모양 입, 둥근 볼과 짧은 턱, 옅은 홍조로 구성했습니다. 이전의 사실적인 쌍꺼풀, 도톰한 입술, 입 주변 S곡선은 최신 귀여운 얼굴로 대체했습니다. 모자·의상·머리카락과 양쪽 옆머리 빈틈 보강, 확인된 걷기·달리기 동작은 유지합니다.

여자·남자 공통 보행은 발의 지지/스윙 구간, 무릎 굴곡, 골반 체중 이동과 회전, 팔의 반대 스윙, 머리 회전 보정, 부드러운 시작·정지를 사용합니다. 확인된 걷기는 유지하고 3.1m/s 초과 달리기에 짧은 양발 공중 구간, 뒤꿈치 회복 궤적, 착지 무릎 완충과 상체 전경을 별도로 적용했습니다. 부츠 외곽으로 바닥 관통을 보정합니다. 기존 9개 관절 구조와 게임 이동 속도는 유지하며, 발목·팔꿈치 관절이나 발의 월드 좌표 고정은 없어 빠른 이동 시 미끄러짐이 일부 남을 수 있습니다. 보행·달리기 4,800프레임 검사 결과는 원본 폴더의 `motion_validation.json`, 실제 활성화·순간이동 검사 결과는 `runtime_motion_validation.json`입니다. `Unity_WalkCycle.gif`와 `Unity_RunCycle.gif`에서 사선·측면 미리보기를 볼 수 있습니다.

여자 모델의 Blender 원본·FBX·GLB와 정면/후면/측면 렌더는 저장소 루트 `ArtSource/Daldongne/Characters`에 있습니다. 전체 생성 진입점은 `build_female_player.py`, 얼굴 전용 모듈은 `build_cute_face.py`입니다. 작은 정원과 새 맵 템플릿도 새 외형을 참조합니다. 재생성: **Tools → Company Game → Characters → Rebuild Reference Girl**. 세부 편집 방법은 원본 폴더 README를 참고하세요.

두 버전 모두 충돌체 반지름 0.35m, 높이 1.8m, 계단 높이 0.23m를 사용합니다. 장식에는 별도의 충돌체가 없습니다. 이전 플레이어 통합 단계에서 Play 모드의 37개 경로를 각 버전으로 양방향 이동하는 148회 검사와 외형·관절·카메라 전환 검사를 통과했습니다. 이 경로 검사는 이번 얼굴 수정에서 재실행한 결과가 아닙니다. 현재 얼굴 모델의 에셋 검사 결과는 원본 폴더의 `unity_asset_validation.json`, 얼굴 확인 이미지는 최신 `Unity_FaceSoft.png`와 `Unity_FaceThreeQuarterSoft.png`입니다.

생성 스크립트: `AgentScripts/DaldongnePlayersBuild.cs`
검증 스크립트: `AgentScripts/DaldongnePlayersQA.cs`
검증 결과: 프로젝트 상위 폴더 `ArtSource/Daldongne/players_validation.json`
