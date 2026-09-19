# 작은 맵 제작과 씬 이동

Unity Hub에서 `companyGame/CompanyGame`을 Unity `6000.4.5f1`로 엽니다.

## 시작 씬과 예제

- `Assets/Scenes/daldongnaemap.unity`: 기존 마을. 기존 건물의 위치와 조명, 카메라, 플레이어를 유지하며 Hierarchy를 정리했습니다.
- `Assets/Scenes/Maps/DaldongnePocketGarden.unity`: 빵집 하나와 나무 세 그루를 배치한 독립적인 작은 맵입니다. 전체 마을 프리팹은 포함하지 않습니다.
- `Assets/Scenes/Templates/SmallMapTemplate.unity`: 바닥, 경계, 플레이어, 카메라, 조명, 기본 스폰 지점이 있는 제작용 씬입니다.
- `Assets/Scenes/Templates/SmallMap.scenetemplate`: 위 씬을 사용하는 Unity Scene Template입니다. 원본 메시·재질·캐릭터는 복제하지 않고 참조합니다.

마을과 예제는 빌드 씬 목록에 등록되어 있으며 마을이 첫 번째입니다. 기존 `SampleScene` 항목은 유지합니다. 제작용 템플릿은 빌드 대상에서 제외합니다.

## 공통 Hierarchy

```text
Map_<씬 이름>
├─ 00_Systems
├─ 10_World
│  ├─ Terrain
│  ├─ Buildings
│  ├─ Vegetation
│  └─ Props
├─ 20_Gameplay
│  ├─ Player
│  ├─ NPCs
│  ├─ Interactables
│  ├─ Portals
│  └─ SpawnPoints
├─ 30_Presentation
│  ├─ Cameras
│  ├─ Lighting
│  └─ Volumes
└─ 90_Development
```

위 구조는 새 작은 맵의 기준입니다. 기존 마을에서는 `10_World` 아래에 기존 마을 프리팹을 유지하고 그 내부에서 건물·나무 등을 분류합니다. 기존 목적지 안내 마커는 `90_Development`에 둡니다. 숫자는 정렬 순서이며 실행 순서를 뜻하지 않습니다.

## 작은 맵 추가

1. 현재 씬을 저장하고 Play Mode를 종료합니다.
2. **Tools → Company Game → Maps → Create Small Map**을 선택합니다.
3. `Assets/Scenes/Maps` 아래에 겹치지 않는 씬 이름으로 저장합니다.
4. 새 씬의 `10_World`에 프리팹을 배치합니다. 시작 지점은 `20_Gameplay/SpawnPoints/Spawn_Default`, 플레이어는 `20_Gameplay/Player/Map Player`입니다.
5. 배치를 마친 뒤 저장하고 Play로 확인합니다. 이 메뉴로 만든 씬은 빌드 씬 목록에 자동 등록됩니다.

Unity의 **File → New Scene**에서도 **Company Game - Small Map** 템플릿을 선택할 수 있습니다. 이 방법을 사용할 때는 씬을 새 경로에 저장하고 빌드 씬 목록에 직접 추가해야 합니다. 제작용 `SmallMapTemplate.unity`를 새 맵으로 덮어쓰지 마세요.

새 씬에는 `DaldongneVillageWalker`가 붙은 활성 플레이어를 하나만 두고, 그 씬의 카메라를 `View Camera`와 `Overview`에 연결합니다. 템플릿에는 이 연결이 되어 있습니다. 스폰을 이동할 때는 `Spawn_Default`와 플레이어의 위치, `DaldongneVillageWalker`의 `Spawn` 값을 함께 변경해야 해당 씬을 바로 실행할 때와 다른 씬에서 진입할 때 같은 곳에서 시작합니다.

## 건물과 나무 재사용

`Assets/Art/Daldongne/WarmVillage/Prefabs/Buildings`의 건물 프리팹을 Hierarchy에 드래그하면 건물 하나를 하나의 묶음으로 이동할 수 있습니다. 창문·벽·지붕은 필요한 경우에만 프리팹 내부를 펼쳐 수정합니다. 여러 부품이 있다는 것 자체는 정상이며, 부품마다 별도의 원본 모델 파일이 생긴다는 뜻은 아닙니다.

나무는 같은 위치의 줄기·가지·잎을 묶고 공통 프리팹을 참조합니다. `Prefabs/Trees/Tree_Common.prefab` 또는 `Tree_Extra.prefab`을 여러 번 배치해서 재사용하세요. 한 번만 다르게 바꾸려면 씬 인스턴스에서 오버라이드를 사용하고, 모든 배치에 반영할 변경은 Prefab Mode에서 원본을 수정합니다.

기존 마을은 건물 프리팹 13개와 나무 인스턴스 48개(일반 34개, 추가형 14개)로 정리했습니다. 기존 나무의 잎 크기와 모양 차이는 인스턴스 오버라이드로 보존하므로, 원본 프리팹에서 같은 속성을 바꿔도 해당 오버라이드가 우선합니다. 메시와 재질은 기존 에셋을 공유합니다. 부품을 하나의 메시로 합치는 최적화 작업은 수행하지 않았습니다.

마을 프리팹 내부의 최상위 분류는 `00_Terrain`, `10_Buildings`, `20_Nature`, `30_Props`, `40_Colliders`입니다. 건물 내부는 구조·지붕·문과 창문·간판·세부 장식으로 나눴습니다. `AgentScripts`의 기존 전체 마을 생성기는 이 구조를 감지하면 덮어쓰기를 중단합니다. Blender에서 전체 맵을 다시 생성할 때에는 별도 산출물로 비교한 뒤 변경분을 반영하세요.

## 두 씬 연결

기본 예제에서는 마을의 역 앞 시작 지점 가까이에 **POCKET GARDEN** 문이 있고, 작은 맵 시작 지점 근처에 **VILLAGE** 문이 있습니다. 걷기 상태로 문 가까이에서 **E**를 누르면 이동합니다.

새 연결을 만들려면 다음을 설정합니다.

1. 도착 씬의 `SpawnPoints` 아래에 빈 GameObject를 만들고 `MapSpawnPoint`를 추가합니다. `Spawn Id`를 `from_village`처럼 정합니다. 같은 씬 안에서는 ID가 중복되지 않아야 합니다.
2. 출발 씬의 `Portals` 아래에 빈 GameObject를 만들고 `MapPortal`을 추가합니다. 예제의 포털 오브젝트를 복제하면 표시용 문도 함께 재사용할 수 있습니다.
3. `Target Scene Path`에 `Assets/Scenes/Maps/MyMap.unity`와 같이 전체 에셋 경로를 입력합니다.
4. `Target Spawn Id`를 도착 씬의 `Spawn Id`와 정확히 맞춥니다.
5. `Display Name`에 안내 문구를, `Interaction Radius`에 E 키가 작동할 거리를 지정합니다. 기본값은 2.5m입니다.
6. 도착 씬이 빌드 씬 목록에서 활성화되어 있는지 확인합니다. 돌아오는 포털도 반대 방향으로 별도 설정합니다.

기존 마을의 도착 ID는 `station`, 작은 예제의 도착 ID는 `default`입니다. 포털은 거리와 E 키로 작동하므로 Trigger Collider를 추가할 필요가 없습니다. 포털의 표시용 문에는 충돌체를 두지 않았습니다.

현재 전환은 한 번에 씬 하나를 여는 방식입니다. 도착 씬의 플레이어와 카메라를 사용하고, 걷기 상태와 캐릭터 성별 선택을 전달합니다. 인벤토리·퀘스트·서버 접속 상태 같은 게임 데이터 보존은 이후 게임 시스템과 연결해야 합니다.

## 조작과 협업

- **WASD** 이동, **Shift** 빠르게 이동, **E** 가까운 포털 사용.
- **F** 걷기/맵 전체 보기 전환, **R** 현재 스폰으로 복귀, **1/2** 캐릭터 선택.
- 전체 보기에서 **우클릭 드래그** 회전, **휠** 확대/축소, **휠 버튼 드래그** 이동, **Home** 해당 맵 기본 시점, **T** 탑뷰 전환.

건물·나무 프리팹은 공유하고, 맵별 배치는 각 `.unity` 씬에서 편집합니다. 같은 씬이나 프리팹을 두 사람이 동시에 변경하는 작업은 피하고 모든 `.meta`를 함께 커밋합니다. 바닥을 바꾸거나 스폰을 옮긴 뒤에는 플레이어 발밑의 Collider와 이동 경로를 Play Mode로 확인하세요. 예제 바닥과 경계에는 Collider가 있으며, 건물은 추출한 기존 충돌 구성을 사용합니다. NPC 이동용 NavMesh와 서버 연동은 별도 제작 대상입니다.

정리 도구를 다시 실행해도 이미 존재하는 예제와 템플릿 씬을 덮어쓰지 않습니다. 현재 씬에 저장되지 않은 변경이 있으면 도구가 실행을 중단하므로 먼저 저장해야 합니다.

## 검증

Unity 6000.4.5f1에서 다음 항목을 확인했습니다.

- 기존 마을의 MeshFilter·Renderer 각 5,104개, MeshCollider 1,557개의 메시·재질·충돌 설정과 부품의 월드 좌표 보존.
- 마을 → 정원 → 마을의 Play Mode 왕복, 중복 이동 요청 차단, 잘못된 씬 경로 거절, 캐릭터 선택·걷기 상태·도착 스폰 유지, 맵별 전체 시점 복원.
- 세 씬의 누락 스크립트·메시·재질·참조 없음, 씬마다 활성 플레이어·카메라 하나, 정상 포털 연결, 실제 Scene Template 인스턴스 생성 및 공유 의존성 확인.

**Tools → Company Game → Maps → Validate Maps**로 에셋과 템플릿을 검사할 수 있습니다. **Verify Map Travel (Play Mode)**는 자동으로 Play Mode에서 왕복한 뒤 기존 에디터 씬을 복원합니다. 후자는 잘못된 경로 거절 검사 중 의도적으로 오류 로그 한 건을 남깁니다. 보고서는 각각 `Temp/WorldMapMigration/asset-validation.txt`, `Temp/WorldMapTravelVerification.json`에 기록되며 Git에서 제외됩니다.
