# 달동네 보수와 독립 지구 5개

Unity 프로젝트는 `CompanyGame/`이며, 새 씬은 `CompanyGame/Assets/Scenes/Maps/`에 있습니다. 각 씬의 Play 시작 고정을 해제해 현재 연 씬에서 실행합니다. 역이나 버스정류장 가까이에서 SPACE를 누르고 목적지를 선택합니다. E는 인벤토리입니다.

## 저장된 씬

| 씬 | 건물 외관 | 빈 부지 |
|---|---|---:|
| NightlifeDistrict | 상점 24동: 게임장, 옷가게, 헤어숍, 포차·호프·바 등. 교차로 9개와 보행 골목, LED 간판 | 4 |
| CivicDistrict | 도서관, 경찰서, 종합병원, 구청, 문화센터, 소방서 | 6 |
| BusinessDistrict | 한대건설 최고층 사옥, 중견 1·중소 2, 공장 6. 중앙 강과 도로 교량 2개 | 10 |
| CoastalResidentialDistrict | 아파트 3동의 단지, 중심 상가 4동, 해변 주택 4동, 해변·산 | 5 |
| LuxuryResidentialDistrict | 고층 아파트 6동, 고급 주택 6동, 쇼핑 건물 2동, 큰 공원 | 5 |

모든 새 씬의 전체 바닥 크기는 144 × 144m입니다. 보행 구역은 평지이며 상업지구 강과 해변 바다는 난간으로 경계를 구분합니다. 빈 부지는 회색 직사각 타일입니다. 실내와 업종별 게임 로직은 이번 외관·맵 작업에 포함하지 않습니다.

## 달동네 수정

- 원래 건물 13동과 다섯 지형 높이 유지. 기존 고시원 1동 유지, 복제 고시원 제거.
- 벽돌, 창틀, 실외기, 우수관, 계량기, 옥상 물탱크와 설비가 있는 원룸 10동 배치.
- 역 전체 가로 배율을 지형·계단과 동일하게 맞춤. 점자블록, 유리 외벽, 계단 구멍 경계를 일치시킴.
- 역 앞 곡선 길이 계단을 가로지르던 부분 제거. 지하 측벽을 뒤쪽 터널 벽까지 연결하고 하단 바닥 교체.
- 중앙 상단 계단, 서쪽 중간 계단, 동쪽 경사로의 위쪽 연결면 보완. 통로 가장자리와 겹친 원룸 위치 조정.

## 제작·검사 코드

`CompanyGame/Assets/Editor/WorldMaps/`:

- `DistrictGeometry.cs`: 건물·창문·옥상·나무·벤치 등의 공통 모델. 재질별로 메시를 합치고 충돌체는 단순한 별도 볼륨으로 구성.
- `CityDistrictBuilder.cs`: 지구 배치, 도로·교량·부지·스폰, 플레이어·카메라 참조, 빌드 씬 등록.
- `TransitMapBuilder.cs`: 달동네 모델을 참고한 역·정류장, 승차·도착 위치, 지하철 계단과 일치하는 지면 개구부. 달동네 시설 위치는 유지.
- `TransitMapQA.cs`: 교통시설 수·간격, 승차 접근, 계단의 보이는 메시와 충돌면, 실제 목적지 선택창·씬 이동 검사.
- `DistrictPresentation.cs`: 깊이 검사를 하는 한글 간판, 외벽·옥상 디테일, 보도 경계, 유리 반사와 야간 후처리.
- `VillageSurfaceRepair.cs`: 역과 지형 연결 보수, 보이는 바닥 검사, 화면 저장.
- `TerracedVillageQA.cs`: 달동네의 양방향 경로 이동과 자산 검사.
- `DistrictMapQA.cs`: 지구 전체 바닥·건물 겹침·스폰 검사 및 실제 Play 씬 순회.

`Build(index)`는 기존 씬을 덮어쓰지 않습니다. `Rebuild(index)`는 수작업 배치를 교체하므로 명시적으로 다시 생성할 때만 사용합니다. 재생성 전 원본 씬을 `_temp/DistrictBackups/`에 복사하고 Unity API로 저장해 GUID를 유지합니다. 생성 씬은 이후 Hierarchy에서 직접 편집할 수 있습니다.

## 조명

번화가의 `30_Presentation/Neon Night Volume`은 전역 Volume입니다. 프로필은 `Assets/Art/WorldDistricts/Profiles/NeonNight.asset`입니다. Bloom threshold 0.8, intensity 1.1, scatter 0.62; Neutral tonemapping; exposure +0.35와 saturation +8을 사용합니다. Map Camera의 post-processing을 켜고 Volume layer 0을 포함합니다. HDR과 Renderer의 PostProcessData 연결을 확인했습니다.

## 검증

최신 결과는 `QA/`의 JSON과 PNG입니다.

- 달동네 49개 경로 × 양방향 × 중앙·양 가장자리: 294회 이동 검사.
- 보이는 바닥 3,920개 표본: 숨겨진 충돌체만 검사하는 방식에서 범위를 넓힘.
- 지구별 7,835~8,836개 바닥 표본, 건물·부지 겹침, 기본 스폰과 여유 스폰 15개 검사.
- `play-tour.json`: 달동네 → 번화가 → 행정 → 상업 → 해변 → 도심 → 달동네. 활성 플레이어 1개, 해당 씬 카메라 연결, 바닥 착지 확인.
- `transit-*-audit.json`, `transit-*-station.json`: 시설 간 거리와 보행 접근, 계단 하강·상승 및 시각 메시 검사.
- `transit-play-tour.json`: 목적지 선택창에서 지하철 6회·버스 6회 씬 이동과 취소 후 조작 복구. 각 파일의 `passed` 또는 `success`와 수정 시각을 확인합니다.

## 교통시설

여섯 지구마다 지하철역 하나와 버스정류장 하나가 있습니다. 새 맵에서는 두 시설을 서로 떨어진 위치에 배치하며 상업지구에서는 강의 반대편에 둡니다. 임시 포탈·이동 표지판은 제거했습니다. 시설의 일반 간판과 노선 안내판은 모델의 일부입니다.

정류장 지붕의 메시에는 카메라 가림 감지용 MeshCollider를 추가했습니다. 원래 모델과 위치를 유지하면서 플레이어를 가리는 지붕에 기존 투명화가 적용됩니다. 실제 달동네 승차 위치의 수정 화면은 `QA/transit-daldongnaemap-Bus-prompt-final.png`입니다. 적용 후 여섯 맵의 교통시설 접근·계단 검사를 다시 통과했습니다.

`TransitStop`의 `boardingPoint`는 승차 안내가 뜨는 지점입니다. 목적지 플레이어는 교통수단에 맞는 `MapSpawnPoint`의 `subway` 또는 `bus`로 도착합니다. `PlayerInteraction`이 SPACE 입력과 목적지 창을 담당합니다.

15개 여유 스폰은 공간·충돌 검증이며, 네트워크 동시 접속 부하 시험은 아닙니다. 씬의 빈 슬롯을 포함한 Unity 기본 YAML 출력에는 공백 경고가 생길 수 있으며, 이를 없애기 위해 씬 YAML을 직접 편집하지 않았습니다.
