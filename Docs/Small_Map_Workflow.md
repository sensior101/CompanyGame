# 작은 맵 제작과 씬 이동

Unity Hub에서 `companyGame/CompanyGame`을 Unity `6000.4.5f1`로 엽니다.

## 시작 씬과 예제

- `Assets/Scenes/daldongnaemap.unity`: 현재 실행·편집할 한국 도시 외곽 달동네입니다. 부지 규격을 맞출 때 맵 루트 배율이나 지형을 늘리지 않습니다. 아래 규격 작업은 시작 시점의 맵 외곽과 지형을 기준으로 합니다.
- `Assets/Scenes/Maps/DaldongnePocketGarden.unity`: 빵집 하나와 나무 세 그루를 배치한 독립적인 작은 맵입니다. 전체 마을 프리팹은 포함하지 않습니다.
- `Assets/Scenes/Templates/SmallMapTemplate.unity`: 바닥, 경계, 플레이어, 카메라, 조명, 기본 스폰 지점이 있는 제작용 씬입니다.
- `Assets/Scenes/Templates/SmallMap.scenetemplate`: 위 씬을 사용하는 Unity Scene Template입니다. 원본 메시·재질·캐릭터는 복제하지 않고 참조합니다.

마을과 정원 예제는 빌드 씬 목록에 등록되어 있으며 마을이 첫 번째입니다. 이전 초안 `DaldongneMap.unity`와 `SampleScene`은 실행 대상에서 제외합니다. 정원 예제와 제작용 `SmallMapTemplate.unity`는 유지하며 제작용 템플릿은 빌드 대상에서 제외합니다.

현재 마을은 원래 지형과 맵 외곽을 유지한 상태에서 내부 건물·기단·접근 계단을 재배치했습니다. 임시 건물과 별도 원룸 10동을 포함한 소형 22동은 각각 16 × 16m 부지에 배치했고 건물의 긴 변을 12m로 맞췄습니다. 역 1동은 기존 크기와 위치를 유지합니다. 맵 루트나 지형의 전체 배율은 바꾸지 않았습니다.

## 추가한 지구 5개

아래 씬은 모두 `Assets/Scenes/Maps/`에 있습니다. 번화가·해변주거지·고급주거지는 144 × 144m, 행정지구는 201.6 × 201.6m이며 상업지구는 별도의 원형 맵입니다. 부지 규격 작업에서 이 크기를 변경하지 않습니다. 각 씬을 열고 Play를 누르면 해당 지구에서 시작합니다. 각 맵의 역이나 버스정류장 가까이에서 SPACE로 목적지를 선택합니다. 모든 지구 씬이 빌드 목록에 등록되어 있습니다.

| 씬 | 구성 | 회색 타일 빈 부지 |
|---|---|---:|
| `NightlifeDistrict.unity` | 상점 24동, 게임장·옷가게·헤어숍·술집, LED 간판, 교차로·골목 | 4 |
| `CivicDistrict.unity` | 도서관·경찰서·병원·구청·문화센터·소방서 | 6 |
| `BusinessDistrict.unity` | 기존 상업지구 전체 배치 보존, 빈 부지 88 × 88m 유지 | 8 |
| `CoastalResidentialDistrict.unity` | 아파트 단지 3동, 상가 4, 해변 주택 4, 모래사장·산 | 5 |
| `LuxuryResidentialDistrict.unity` | 고층 아파트 6, 고급 주택 6, 쇼핑 건물 2, 큰 공원 | 5 |

건물은 외관 모델입니다. 실내와 업종별 게임 로직은 포함하지 않습니다. 15개 여유 스폰은 공간 배치이며 동시 접속 네트워크 기능은 기존 게임 시스템의 영역입니다. 검증 기록과 화면은 `ArtSource/WorldDistricts/QA/`를 참고합니다.

## 2026-10-30~31 부지·건물 규격

단위는 월드 미터입니다. 구매용 빈 부지 하나에 독립 건물 하나가 들어가며, 주변 같은 등급의 건물도 같은 기준을 사용합니다. **건물의 긴 변을 해당 등급의 12/18/26/38m에 맞춰 확대·축소**합니다. X·Y·Z에 같은 비율을 적용해 형태를 보존하므로 짧은 변은 모델마다 다릅니다. 최대 크기 이하인지만 검사하고 작은 건물을 그대로 두는 이전 처리는 수정했습니다. 빈 부지는 지정된 정사각형 크기를 유지합니다. **격자 바닥은 빈 부지에만 표시**합니다.

| 용도 | 부지 | 건물 최대 |
|---|---:|---:|
| 소형 상가·주택 | 16 × 16 | 12 × 12 |
| 중형 주택·빌라 | 24 × 24 | 18 × 18 |
| 아파트·중형 상업시설 | 32 × 32 | 26 × 26 |
| 경찰서·병원·구청·시청·도서관·소방서·문화센터 | 48 × 48 | 38 × 38 |
| 기존 상업지구 | 88 × 88 빈 부지 유지 | 기존 건물 유지 |

| 맵 | 건물이 있는 부지 | 빈 부지 |
|---|---|---|
| 달동네 | 소형 16 × 16m, 22곳 저장 완료; 역은 기존 크기·위치 유지 | 없음 |
| 번화가 | 소형 24곳 | 16 × 16, 4곳 |
| 행정지구 | 공공 6곳 | 32 × 32, 6곳 |
| 해변주거지 | 아파트 3곳·중형 주택 4곳·소형 상가 4곳 | 24 × 24, 5곳 |
| 고급주거지 | 아파트·상업 8곳·빌라 6곳 | 24 × 24, 5곳 |
| 상업지구 | 기존 건물 7곳 유지 | 88 × 88, 8곳 유지 |
| 작은 정원 | 소형 빵집 1곳 | 없음 |

달동네 역은 사용자가 지정한 예외입니다. 역용 32m 부지를 만들기 위한 해안 확장이나 받침 추가는 없습니다. 기존 시청 씬 인스턴스는 비활성화해 보존하고, `Home_A.prefab`을 재사용한 `TemporaryBuilding`을 소형 규격으로 배치했습니다. 최종 외곽은 약 12.00 × 11.37m입니다. 원본 시청 프리팹은 보존하며 행정지구에 새 시청을 추가하는 작업은 포함하지 않습니다.

행정·해변·고급주거지는 승인된 내부 도로 구간과 공원·교통시설 위치를 조정했습니다. 도로 폭은 행정지구 11.2m, 해변·고급주거지 8m를 유지합니다. 기존 5개 맵의 건물 56개와 달동네 22개를 합쳐 **78개 건물**을 기준 크기로 맞췄습니다. 차량, 원본 건물 메시, 맵 외곽, 지형, 해안선은 확대하지 않았습니다. 모든 맵의 건물 86개·빈부지 28개 전후 실측은 [건물·부지 대조표](Parcel_Size_Comparison.md)와 [CSV](Parcel_Size_Comparison.csv)에 있습니다. 상업지구 건물 7개와 달동네 역 1개는 크기 통일 대상에서 제외해 기존 상태를 유지합니다.

### 달동네 최종 저장 상태

| 항목 | 저장 상태 |
|---|---|
| 소형 건물·부지 | 건물 22개, 각 부지 16 × 16m, 건물 긴 변 12m |
| 점유 부지 바닥 | 격자 표시 0개. 자연 지형과 개별 건물 기단을 사용하며, `Parcel Sites`의 부지 경계 메시는 Renderer를 끈 검증용 데이터 |
| 건물 기단·출입구 | 기단 22개, 출입구 발판 22개 |
| `Home_C` 접근 보완 | 기존 16 × 16m 부지와 건물 크기를 유지하고 건물·기단·입구를 부지 안에서 북쪽으로 1.5m 이동. `Entry_Home_C`만 다시 설계해 기존 담장을 넘는 접근 경로 확보 |
| `navigationExclusions` | `NavMeshModifierVolume` 1개로 `Home_C` 담장을 가로지르는 서쪽 지름길만 NavMesh에서 제외. 이 보정은 건물·메시·Collider를 바꾸지 않음 |
| 접근 경로 | 보행·계단 경로 35개. 기존 지형까지 닿는 받침과 충돌체 포함 |
| 나무 | 기존 45개 중 17개 위치 조정. 줄기·가지·수관의 크기와 회전 유지, 실제 원지형 높이에 맞춰 배치 |
| 대체한 기존 접근 구조 | 지정 경로 14개만 비활성화: 계단 9개, 난간·기둥 4개, `Gosiwon_Access` 컨테이너 1개. 원본 메시·Transform은 보존 |
| 별도 비활성 원본 | 기존 `CityHall`, `Cafe_SourcePreserved`. 위 접근 구조 14개와 별도이며 원본 프리팹은 수정하지 않음 |
| 카페 소품 | 자전거 54조각, 의자 8조각, 탁자·컵 7조각의 크기와 조립 상태 유지 |
| 신규 Art 메시 에셋 | 이번 달동네 적용에서 0개. 기존 메시와 Unity 기본 Cube를 씬에서 재사용 |

NavMesh 제외 영역은 `Dald Parcel Infrastructure/Route_Entry_Home_C/Nav_WallShortcut`의 기존 Unity `NavMeshModifierVolume` 컴포넌트입니다. 월드 중심은 `(30.325, 7.4, 9)`, 크기는 `(5.55, 1, 1.34)m`, Area는 `1`(Not Walkable)입니다. 기존 담장과 충돌체를 유지한 채 새 입구 계단으로 경로를 유도하며, 전역 에이전트 설정이나 다른 맵의 경로 규칙은 변경하지 않았습니다.

### 기존 코드와 에셋 연결

- `Assets/Scripts/Economy/Property/LandPlot.cs`: `SizeTier`, `GetParcelSide`, `TryGetBuildingSide`가 규격을 제공합니다. 기존 가격·소유권·저장 필드와 공개 API는 유지합니다.
- `Assets/Scripts/Editor/WorldMaps/WorldMapTools.cs`: `ApplyParcelSize`로 빈 부지를, `NormalizeBuildingToTier`로 주변 건물의 기준 크기를 적용합니다. 맵 부모·지형·차량 등 잘못된 대상은 거절합니다. 기존 축소 전용 `FitBuildingWithinTier`는 이전 호출부 호환성을 위해 유지하며 이번 건물 크기 통일에는 사용하지 않습니다.
- `Assets/Scripts/Editor/Citizens/CitizenNavBuilder.cs`: 기존 NavMesh 생성기를 재사용합니다. 정점이 없는 텍스트 메시를 NavMesh 입력에서 제외합니다.
- `MapColliderRebuilder.Scan`과 기존 충돌 생성기를 재사용해 변경된 배치의 Collider를 갱신합니다. `CombatCollisionBuilder.BuildMap`으로 기존 서버 충돌 프리팹과 manifest 항목을 갱신합니다.
- 씬의 `Parcel Sites`는 건물이 있는 부지의 크기·소유 건물 대응을 검증하는 기준입니다. 달동네에서는 Renderer를 끄고 Collider를 두지 않아 자연 지형을 덮거나 이동을 막지 않습니다. `Assets/Art/WorldDistricts/ParcelLayouts/`의 메시들은 다른 지구의 재배치된 도로·공원·장식용 복사본입니다. 원본 모델을 직접 변형하지 않기 위해 분리했으며 새로운 런타임 관리자나 경제·네트워크 시스템을 추가하지 않습니다.

데이터 흐름은 **기존 규격 API → 씬별 건물·부지 배치 → 기존 Collider 생성 → 기존 NavMesh 생성 → 기존 서버 충돌 캐시 갱신**입니다. 지하철·버스 목적지 ID, 상점 연결, 건물의 기존 동작 컴포넌트는 재사용합니다. 위치가 바뀐 교통시설은 해당 승차 지점과 도착 스폰도 함께 옮깁니다.

변경 전 백업과 초기 기록은 `_temp/parcel-tiers-20261030/`, 최종 크기 보정·달동네 검사 기록은 `_temp/parcel-size-correction-20261030/`에 있습니다. `_temp`는 로컬 작업·검증 자료이며 게임은 이 폴더에 의존하지 않습니다.

### 저장한 파일 범위와 검증

- 기존 씬 6개: `Assets/Scenes/daldongnaemap.unity`, `Assets/Scenes/Maps/`의 `NightlifeDistrict.unity`, `CivicDistrict.unity`, `CoastalResidentialDistrict.unity`, `LuxuryResidentialDistrict.unity`, `DaldongnePocketGarden.unity`. 달동네 내부 재배치까지 저장했습니다.
- 각 씬의 기존 `NavMesh-CitizenNavigation.asset` 6개, `Assets/Resources/CombatCollision/`의 해당 맵 프리팹 6개와 `manifest.json`을 갱신했습니다.
- 기존 코드 3개는 위 `LandPlot.cs`, `WorldMapTools.cs`, `CitizenNavBuilder.cs`입니다. 기존 설명 문서는 `Docs/Small_Map_Workflow.md`를 갱신합니다.
- 새 도로·공원·장식 메시 67개와 Unity 메타 파일: `ParcelLayouts/CivicDistrict/` 23개, `CoastalResidentialDistrict/` 20개, `LuxuryResidentialDistrict/` 24개. 런타임 스크립트 신규 생성은 없습니다.
- 달동네를 포함한 건물이 있는 부지 78곳과 빈 부지 20곳을 규격에 맞춰 저장했습니다. 상업지구의 88m 빈 부지 8곳은 유지합니다. 기존 상업지구 건물의 점유 부지는 새로 88m로 지정하지 않았습니다.
- 앞선 단계의 지구/교통수단 진입 11건과 시청 교체·고급주거지 보도 마무리 후 추가 5건은 `_temp/parcel-tiers-20261030/play-validation.json`, 같은 폴더의 `final-play-validation.json`에 기록돼 있습니다. 아래 최종 저장본 검사와 구분합니다.
- 최신 `normalized-audit.json`: 저장본 7맵, 건물 **78/78개**, 실패 **0건**, 미완료 **0건**입니다. 원본 맵·지형 Transform, 원본 지형 메시와 범위, 차량 형태를 보존했고 승인된 기존 접근 구조는 비활성화만 허용했습니다. 상업지구 씬·NavMesh·서버 충돌 관련 7파일과 manifest 항목은 작업 전과 동일합니다. 원본 Art 메시 **1,716개**, 에셋 의존 해시 **1,342개**도 동일합니다.
- 최신 `dald-access-qa.json`: 저장된 달동네의 지하철 출발점에서 건물 접근점 **22/22개**로 완전한 NavMesh 경로를 확인했습니다. 실패 **0건**입니다. `Home_C`의 부지 내부 위치·접근 경로 보완 후에도 저장 및 보존·경로 검사를 통과했습니다(`homec-access-applied.json`).
- `homec-navigation-applied.json`: 담장 지름길을 제외하는 국소 NavMesh 영역 1개를 저장하고 NavMesh를 재생성했습니다. 이 보정의 건물·메시·Collider 변경은 **0건**입니다. 최종 씬 해시에 맞춰 서버 충돌 캐시도 갱신했습니다.

<!-- DALD_CHARACTER_WALK_STATUS_BEGIN -->
**Play Mode CharacterController 자동 보행: 22/22개 성공, 실패 0건.** 기존 `MapColliderRules`의 반지름·높이·계단·경사·스킨 설정을 적용한 검증용 CharacterController를 `Move`로 이동시켜 지하철 출발점에서 각 건물 접근점까지의 실제 충돌 통과를 확인했습니다. 기록: `_temp/parcel-size-correction-20261030/dald-character-walk.json`.
<!-- DALD_CHARACTER_WALK_STATUS_END -->

**최종 저장본 진입 검사: 5/5건 성공.** 행정지구 기본 스폰, 달동네 지하철·버스, 고급주거지 지하철·버스 진입을 다시 검사했습니다. 모든 건에서 실제 프레임 진행, 동일 플레이어 유지, 접지 상태, 도착 평면 오차 **0m**, 실행 오류 **0건**을 확인했습니다. 기록: `_temp/parcel-size-correction-20261030/final-play-validation.json`. 앞선 단계의 11건·5건과 별도인 최종 5건입니다.

아직 완료로 주장하지 않는 범위는 모든 골목·계단의 수동 보행, 전체 NPC 목적지 간 경로, 차량 주행, 별도 서버/클라이언트 동시 접속입니다.

### 아키텍처 규칙에 따른 변경 정리

| 항목 | 내용 |
|---|---|
| 1. 수정한 기존 파일 | 기존 코드 `LandPlot.cs`, `WorldMapTools.cs`, `CitizenNavBuilder.cs`; 위 씬 6개와 대응 NavMesh·서버 충돌 자료; `Docs/Small_Map_Workflow.md` |
| 2. 신규 파일과 이유 | 세 지구의 `ParcelLayouts` 메시 67개와 메타: 원본 모델을 보존하며 도로·공원·외부 장식만 재배치하기 위한 복사본. `Docs/Parcel_Size_Comparison.md`·`.csv`: 114개 실측 전후 비교. `_temp`의 적용·QA 보조 스크립트는 로컬 작업 전용이며 런타임에 포함되지 않음. 이번 달동네용 신규 Art 메시 에셋은 없음 |
| 3. 재사용한 클래스·메서드 | `LandPlot` 규격 API, `WorldMapTools.ApplyParcelSize`·`NormalizeBuildingToTier`·`MeasureFootprint`, `MapColliderRebuilder.Scan`·기존 충돌 생성기, `CitizenNavBuilder`, Unity `NavMeshModifierVolume`, `CombatCollisionBuilder.BuildMap`, 기존 `TransitStop`·`MapSpawnPoint` 연결 |
| 4. 데이터 흐름 | 기존 규격 API → 씬 건물·부지 배치 → Collider 갱신 → NavMesh 재생성 → 서버 충돌 캐시 갱신 → 저장 씬 실측·접근 경로 검사 |
| 5. 중복 구현 여부 | 신규 런타임 관리자·경제·인벤토리·대화·거래·로그·저장·네트워크 시스템 없음. 기존 공통 기능 재사용 |
| 6. 기존 기능·호출부 영향 | 공개 API와 기존 `FitBuildingWithinTier` 호출 호환성 유지. 소유권·가격·저장 데이터와 목적지 ID를 변경하지 않음. 배치 변경에 맞춰 기존 상호작용·스폰 연결을 갱신하고 카페 씬 복사본의 참조를 명시적으로 연결 |
| 7. 검증과 한계 | 저장본 7맵 78/78개 규격·원본 보존, 달동네 NavMesh 접근 22/22개, Play CharacterController 자동 보행 22/22개, 최종 진입 5/5건 통과. 전체 수동 보행·차량·동시 멀티플레이 검증은 완료로 주장하지 않음 |

이 규격 작업은 기존 씬의 건물과 부지를 정렬한 것입니다. 빈부지 구매 후 실제 건물을 생성하는 콘텐츠의 완성 여부를 뜻하지는 않습니다. 향후 건설 콘텐츠는 기존 부지 등급을 읽고 같은 건물 크기 기준을 사용해야 합니다.

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

원본 마을 프리팹은 건물 13동과 나무 인스턴스 48개(일반 34개, 추가형 14개)로 정리했습니다. 현재 씬에는 `One Room Neighborhood`의 원룸 10동이 추가돼 있습니다. 이번 규격 작업에서는 작업 시작 시점의 나무 45개를 보존하고 그중 17개의 위치만 조정했습니다. 기존 에셋의 메시와 재질은 공유하며, 원룸·지구 건물의 세부 장식은 건물별·재질별 메시로 합칩니다.

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

건물·나무 프리팹은 공유하고, 맵별 배치는 각 `.unity` 씬에서 편집합니다. 동시 편집·`.meta` 규칙은 [Git_Collaboration.md](Git_Collaboration.md#맵과-에셋-공동-작업)를 따릅니다. 바닥을 바꾸거나 스폰을 옮긴 뒤에는 플레이어 발밑의 Collider와 이동 경로를 Play Mode로 확인하세요. NPC 이동용 NavMesh와 서버 충돌 캐시는 위의 기존 생성기로 갱신합니다. 캐시 생성과 서버·클라이언트 동시 실행 검증은 서로 다른 단계입니다.

정리 도구를 다시 실행해도 이미 존재하는 예제와 템플릿 씬을 덮어쓰지 않습니다. 현재 씬에 저장되지 않은 변경이 있으면 도구가 실행을 중단하므로 먼저 저장해야 합니다.

## 검증

작은 맵과 템플릿을 처음 분리할 때 Unity 6000.4.5f1에서 다음 항목을 확인했습니다. 아래 부품 수와 좌표 보존 기록은 당시 기준입니다.

- 기존 마을의 MeshFilter·Renderer 각 5,104개, MeshCollider 1,557개의 메시·재질·충돌 설정과 부품의 월드 좌표 보존.
- 마을 → 정원 → 마을의 Play Mode 왕복, 중복 이동 요청 차단, 잘못된 씬 경로 거절, 캐릭터 선택·걷기 상태·도착 스폰 유지, 맵별 전체 시점 복원.
- 세 씬의 누락 스크립트·메시·재질·참조 없음, 씬마다 활성 플레이어·카메라 하나, 정상 포털 연결, 실제 Scene Template 인스턴스 생성 및 공유 의존성 확인.

**Tools → Company Game → Maps → Validate Maps**로 에셋과 템플릿을 검사할 수 있습니다. **Verify Map Travel (Play Mode)**는 자동으로 Play Mode에서 왕복한 뒤 기존 에디터 씬을 복원합니다. 후자는 잘못된 경로 거절 검사 중 의도적으로 오류 로그 한 건을 남깁니다. 보고서는 각각 `Temp/WorldMapMigration/asset-validation.txt`, `Temp/WorldMapTravelVerification.json`에 기록되며 Git에서 제외됩니다.

현재 달동네 확장의 에셋·이동 검사 결과는 `ArtSource/Daldongne/TerracedExpansion/assets.json`과 `navigation.json`을 확인하세요. 경로 수와 성공 여부는 최신 보고서를 기준으로 합니다. 15개 스폰과 CharacterController 이동 검사는 공간·충돌 확인이며, 15명 네트워크 동시 접속 시험은 아닙니다.
