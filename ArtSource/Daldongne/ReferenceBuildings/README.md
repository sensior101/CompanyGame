# 참고 사진 기반 건물 리모델링

2026-09-19. 적용 씬: `CompanyGame/Assets/Scenes/daldongnaemap.unity`.
기존 건물 프리팹의 GUID와 배치 피벗을 유지한 채 여섯 건물의 메시를 교체했다.
씬에서 참조하는 마을 프리팹에 반영되므로 별도 배치 작업이 필요 없다.

## 반영 내용

- **Gosiwon / 사진 1:** 4층 붉은 벽돌, 아치 장식 창틀, 층별 밝은 몰딩, 1층 차양·유리문·셔터, 평지붕과 옥탑, 실외기·물탱크·벤치·화분.
- **Convenience / 사진 2:** 노랑·초록·주황 띠 간판, 돌출 간판, 분할 유리 입면과 양개문, 24 표시, 실외기·배관. 현재 맵의 따뜻한 낮 조명에 맞춘 외관이며 실내 영업 공간은 구현하지 않았다.
- **Home_A / Home_C / 사진 3:** 푸른 기와, 위층 벽돌·아래층 회벽, 외부 계단과 위층 현관, 창살·화분 난간·실외기·작은 벽화. A는 초록 문, C는 파란 문.
- **Home_B / Home_D / 사진 4:** 두 벽돌 매스, 콘크리트 평지붕, 작은 마당과 대문, 외부 계단, 발코니·빨래·가스 배관·덩굴·벤치.

사진의 건축적 특징을 현재 맵의 로우폴리 스타일과 기존 대지 크기에 맞춰 제작했다.
색상만 바꾼 기존 모델이 아니라 새 메시와 재질이다. 같은 재질의 작은 부품은 기능별 메시로 합쳤다.

## 고시원 진입 수정

기존 고시원 대지는 주변보다 약 3m 높지만 기존 이동 검사에는 정문까지의 경로가 없었다.
진입 방향의 `WallCap_Crest0` 상단은 Y=12.43m로, 보행면 Y=12m보다 43cm 높아
CharacterController의 23cm stepOffset으로 넘을 수 없었다.

마을 프리팹 `00_Terrain/Gosiwon_Access`에 폭 1.8m의 동쪽 보행로와 정문 앞 테라스를 추가했다.
상단 길(CrestSquare) → 동쪽 보행로 → 정문 앞 테라스 → 고시원 문으로 이어진다.
담장에 진입 틈을 만들고 방해되는 윗단을 낮췄으며 통로의 나무·화분을 옮겼다.
보행로에는 실제 바닥·난간 Collider를 넣었다. 플레이어 반지름, 키, stepOffset, slopeLimit는 변경하지 않았다.

## 검증

- Unity 6000.4.5f1 Play Mode, 현재 `PlayerMovement`의 CharacterController 사용.
- 남녀 캐릭터의 기존 37개 경로 양방향: 148개 검사.
- 새 진입로 2구간, 중앙·좌우 3개 차선, 남녀 양방향: 24개 검사.
- 시작 지점 → 고시원 정문 → 시작 지점: 남녀 × 걷기 3m/s·달리기 4.5m/s, 4회 왕복(8회 편도). 각 편도 안에서는 순간이동 없이 연결된 체크포인트를 통과한다.
- 연속 이동의 구간 검사 152개를 포함해 **총 324개 검사, 실패 0개**.
- `navigation-validation.json`: 위치 오차, 높이 오차, 머리 충돌, 실패 시 주변 Collider 이름.
- `asset-validation.json`: 프리팹·메시·재질·충돌 메시 참조와 Unity 컴파일 상태.
- 각 건물의 `.png`: 맵 배치 화면. `_Model.png`: 건물만 표시한 검토 화면.
- `Gosiwon_Access_Top.png`: 정문 연결로를 포함한 상단 맵.

## 수정 원본과 재생성

새 모델의 편집 원본은 Unity 네이티브 에셋이다.
이전 전체 맵 `.blend` / `.glb` / `.unitypackage`는 이번 변경을 포함하지 않는 이전 스냅샷이다.
기존 전체 맵 임포터로 현재 프리팹을 덮어쓰지 않는다.

- `CompanyGame/Assets/Art/Daldongne/WarmVillage/ReferenceBuildings/`: 생성된 메시·재질. 런타임에 메시를 생성하지 않는다.
- `CompanyGame/Assets/Art/Daldongne/WarmVillage/Prefabs/Buildings/`: 수정한 여섯 건물 프리팹.

> 2026-10-04 정리: 생성·검사에 쓰던 `CompanyGame/AgentScripts`와 Unity Pipeline 패키지(`unity command run_script`)는 저장소에서 제거했습니다. 만들어진 프리팹·메시는 그대로 남아 있습니다. 다시 생성해야 하면 커밋 `ccfd0c6`의 `CompanyGame/AgentScripts`를 복원하고 `com.unity.pipeline` 패키지를 다시 추가하세요.

첫 생성은 다수의 Unity 에셋 임포트로 시간이 걸린다. CLI 전송 시간이 초과되어도 Unity의 작업이 계속될 수 있으므로 완료 상태를 확인한 뒤 재실행한다.
이번 변경은 로컬 작업 트리에만 반영했으며 커밋·푸시는 하지 않았다.
