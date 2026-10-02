# 작은 맵 제작과 씬 이동

Unity Hub에서 `companyGame/CompanyGame`을 Unity `6000.4.5f1`로 엽니다.

## 시작 씬과 예제

- `Assets/Scenes/daldongnaemap.unity`: 현재 실행·편집할 한국 도시 외곽 달동네입니다. 기존 한국식 건물과 다섯 지형 높이를 유지하고 가로·세로를 각각 1.5배 넓혔습니다.
- `Assets/Scenes/Maps/DaldongnePocketGarden.unity`: 빵집 하나와 나무 세 그루를 배치한 독립적인 작은 맵입니다. 전체 마을 프리팹은 포함하지 않습니다.
- `Assets/Scenes/Templates/SmallMapTemplate.unity`: 바닥, 경계, 플레이어, 카메라, 조명, 기본 스폰 지점이 있는 제작용 씬입니다.
- `Assets/Scenes/Templates/SmallMap.scenetemplate`: 위 씬을 사용하는 Unity Scene Template입니다. 원본 메시·재질·캐릭터는 복제하지 않고 참조합니다.

마을과 정원 예제는 빌드 씬 목록에 등록되어 있으며 마을이 첫 번째입니다. 이전 초안 `DaldongneMap.unity`와 `SampleScene`은 실행 대상에서 제외합니다. 정원 예제와 제작용 `SmallMapTemplate.unity`는 유지하며 제작용 템플릿은 빌드 대상에서 제외합니다.

현재 마을의 지형 높이는 1.2 / 3.6 / 7.2 / 10.8 / 14.4m입니다. 기존 건물 13동과 고시원 1동을 유지하고, 추가 고시원 복제본을 제거했습니다. 별도 원룸 10동을 포함해 전체 건물 23동입니다. 계단 10개와 오르막길 3개로 연결합니다. 역 건물은 계단·지형과 같은 가로 배율을 사용해야 하므로 일반 주택에 적용한 축소 배율을 사용하지 않습니다.

## 추가한 지구 5개

아래 씬은 모두 `Assets/Scenes/Maps/`에 있으며 144 × 144m 크기입니다. 각 씬을 열고 Play를 누르면 해당 지구에서 시작합니다. 각 맵의 역이나 버스정류장 가까이에서 SPACE로 목적지를 선택합니다. 모든 지구 씬이 빌드 목록에 등록되어 있습니다.

| 씬 | 구성 | 회색 타일 빈 부지 |
|---|---|---:|
| `NightlifeDistrict.unity` | 상점 24동, 게임장·옷가게·헤어숍·술집, LED 간판, 교차로·골목 | 4 |
| `CivicDistrict.unity` | 도서관·경찰서·병원·구청·문화센터·소방서 | 6 |
| `BusinessDistrict.unity` | 한대건설, 중견기업 1·중소기업 2, 공장 6, 중앙 강과 도로 교량 2 | 10 |
| `CoastalResidentialDistrict.unity` | 아파트 단지 3동, 상가 4, 해변 주택 4, 모래사장·산 | 5 |
| `LuxuryResidentialDistrict.unity` | 고층 아파트 6, 고급 주택 6, 쇼핑 건물 2, 큰 공원 | 5 |

건물은 외관 모델입니다. 실내와 업종별 게임 로직은 포함하지 않습니다. 15개 여유 스폰은 공간 배치이며 동시 접속 네트워크 기능은 기존 게임 시스템의 영역입니다. 검증 기록과 화면은 `ArtSource/WorldDistricts/QA/`를 참고합니다.

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
│  ├─ Transit
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

새 씬에는 `PlayerMovement`가 붙은 활성 플레이어를 하나만 두고, 그 씬의 카메라를 `View Camera`와 `PlayerCameraController`에 연결합니다. 템플릿에는 이 연결이 되어 있습니다. 스폰을 이동할 때는 `Spawn_Default`와 플레이어의 위치, `PlayerMovement`의 `Spawn` 값을 함께 변경해야 해당 씬을 바로 실행할 때와 다른 씬에서 진입할 때 같은 곳에서 시작합니다.

## 건물과 나무 재사용

`Assets/Art/Daldongne/WarmVillage/Prefabs/Buildings`의 건물 프리팹을 Hierarchy에 드래그하면 건물 하나를 하나의 묶음으로 이동할 수 있습니다. 창문·벽·지붕은 필요한 경우에만 프리팹 내부를 펼쳐 수정합니다. 여러 부품이 있다는 것 자체는 정상이며, 부품마다 별도의 원본 모델 파일이 생긴다는 뜻은 아닙니다.

나무는 같은 위치의 줄기·가지·잎을 묶고 공통 프리팹을 참조합니다. `Prefabs/Trees/Tree_Common.prefab` 또는 `Tree_Extra.prefab`을 여러 번 배치해서 재사용하세요. 한 번만 다르게 바꾸려면 씬 인스턴스에서 오버라이드를 사용하고, 모든 배치에 반영할 변경은 Prefab Mode에서 원본을 수정합니다.

원본 마을 프리팹은 건물 13동과 나무 인스턴스 48개(일반 34개, 추가형 14개)로 정리했습니다. 현재 씬에서는 기존 건물을 보존하고 `One Room Neighborhood`에 원룸 10동을 추가했습니다. 원룸 부지와 겹치는 나무만 씬에서 제거했습니다. 기존 에셋의 메시와 재질은 공유하며, 새 원룸·지구 건물의 세부 장식은 건물별·재질별 메시로 합칩니다.

마을 프리팹 내부의 최상위 분류는 `00_Terrain`, `10_Buildings`, `20_Nature`, `30_Props`, `40_Colliders`입니다. 건물 내부는 구조·지붕·문과 창문·간판·세부 장식으로 나눴습니다. `AgentScripts`의 기존 전체 마을 생성기는 이 구조를 감지하면 덮어쓰기를 중단합니다. Blender에서 전체 맵을 다시 생성할 때에는 별도 산출물로 비교한 뒤 변경분을 반영하세요.

## 지하철·버스로 씬 이동

달동네와 새 지구 5개의 이동은 실제 역·버스정류장에서 시작합니다. 가까이 가면 **지하철 타기 / 버스 타기**와 **SPACE** 키캡이 나타납니다. SPACE로 창을 열고 다른 지구 5개 중 목적지를 선택합니다. ESC 또는 닫기로 취소합니다.

1. 맵마다 `TransitStop`을 지하철·버스 하나씩 배치하고 `boardingPoint`를 막히지 않은 보도에 둡니다.
2. `SpawnPoints` 아래에 `MapSpawnPoint` ID `subway`와 `bus`를 하나씩 둡니다. 같은 수단의 도착 스폰을 사용합니다.
3. 두 시설 사이의 실제 도보 시간이 최소 10초가 되게 배치합니다. 상업지구는 강 양쪽에 배치합니다.
4. 씬의 플레이어에 `PlayerInteraction`을 두고 한글 폰트와 6개 지구 목적지를 연결합니다. 목적지 씬은 빌드 목록에서 활성화해야 합니다.

기존 포탈 문과 임시 지구 이동 표지판은 실제 지구에서 제거했습니다. `DaldongnePocketGarden`은 과거 제작 예제이며 지구 이동 목록에는 포함하지 않습니다. 전환은 한 번에 씬 하나를 열고, 도착 씬의 플레이어·카메라를 사용합니다. 인벤토리는 세션 동안 맵 이동 후에도 유지합니다. 디스크 저장과 서버 동기화는 별도 게임 시스템입니다.

## 조작과 협업

- **WASD** 이동, **Shift** 빠르게 이동, **SPACE** 역·정류장에서 목적지 선택.
- **E** 인벤토리 열기/닫기, **1~8** 퀵슬롯 선택, **ESC** 창 닫기.
- **F** 걷기/맵 전체 보기 전환, **R** 현재 스폰으로 복귀, **Alt+1/Alt+2** 캐릭터 선택.
- 전체 보기에서 **우클릭 드래그** 회전, **휠** 확대/축소, **휠 버튼 드래그** 이동, **Home** 해당 맵 기본 시점, **T** 탑뷰 전환.

건물·나무 프리팹은 공유하고, 맵별 배치는 각 `.unity` 씬에서 편집합니다. 같은 씬이나 프리팹을 두 사람이 동시에 변경하는 작업은 피하고 모든 `.meta`를 함께 커밋합니다. 바닥을 바꾸거나 스폰을 옮긴 뒤에는 플레이어 발밑의 Collider와 이동 경로를 Play Mode로 확인하세요. 예제 바닥과 경계에는 Collider가 있으며, 건물은 추출한 기존 충돌 구성을 사용합니다. NPC 이동용 NavMesh와 서버 연동은 별도 제작 대상입니다.

정리 도구를 다시 실행해도 이미 존재하는 예제와 템플릿 씬을 덮어쓰지 않습니다. 현재 씬에 저장되지 않은 변경이 있으면 도구가 실행을 중단하므로 먼저 저장해야 합니다.

## 검증

작은 맵과 템플릿을 처음 분리할 때 Unity 6000.4.5f1에서 다음 항목을 확인했습니다. 아래 부품 수와 좌표 보존 기록은 당시 기준입니다.

- 기존 마을의 MeshFilter·Renderer 각 5,104개, MeshCollider 1,557개의 메시·재질·충돌 설정과 부품의 월드 좌표 보존.
- 마을 → 정원 → 마을의 Play Mode 왕복, 중복 이동 요청 차단, 잘못된 씬 경로 거절, 캐릭터 선택·걷기 상태·도착 스폰 유지, 맵별 전체 시점 복원.
- 세 씬의 누락 스크립트·메시·재질·참조 없음, 씬마다 활성 플레이어·카메라 하나, 정상 포털 연결, 실제 Scene Template 인스턴스 생성 및 공유 의존성 확인.

**Tools → Company Game → Maps → Validate Maps**로 에셋과 템플릿을 검사할 수 있습니다. **Verify Map Travel (Play Mode)**는 자동으로 Play Mode에서 왕복한 뒤 기존 에디터 씬을 복원합니다. 후자는 잘못된 경로 거절 검사 중 의도적으로 오류 로그 한 건을 남깁니다. 보고서는 각각 `Temp/WorldMapMigration/asset-validation.txt`, `Temp/WorldMapTravelVerification.json`에 기록되며 Git에서 제외됩니다.

현재 달동네 확장의 에셋·이동 검사 결과는 `ArtSource/Daldongne/TerracedExpansion/assets.json`과 `navigation.json`을 확인하세요. 경로 수와 성공 여부는 최신 보고서를 기준으로 합니다. 15개 스폰과 CharacterController 이동 검사는 공간·충돌 확인이며, 15명 네트워크 동시 접속 시험은 아닙니다.
