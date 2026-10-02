# 기존 한국 달동네 확장

`CompanyGame` Unity 프로젝트에서 `Assets/Scenes/daldongnaemap.unity`를 열고 Play를 누릅니다. 이번 작업은 이 씬에서 이어서 편집하며 새 마을 씬을 만들지 않습니다. 이전 초안 `DaldongneMap.unity`와 `SampleScene`은 실행 대상에서 제외하고, `DaldongnePocketGarden.unity`와 제작용 `SmallMapTemplate.unity`는 유지합니다.

## 확장 범위와 기존 에셋

- 기존 한국 도시 외곽 달동네의 건물 양식과 1.2 / 3.6 / 7.2 / 10.8 / 14.4m 지형 높이를 유지합니다.
- 기존 지형의 가로·세로를 각각 1.5배 넓히고 뒤쪽에 주거 골목을 연결합니다. 건물 크기는 유지합니다.
- 기존 건물 13동과 원래 고시원 1동을 유지합니다. 확장 때 추가했던 고시원 복제본은 제거하고, 원룸 10동을 새로 배치해 전체 23동입니다.
- 계단 10개와 오르막길 3개, 상단 주거 골목과 역 앞 곡선 산책로를 구성합니다.
- 원래 고시원 1동의 왼쪽·뒤쪽 창문은 원본 측면 창문 메시를 공유합니다. 새 원룸은 `Assets/Art/WorldDistricts/`의 재질별 합친 메시를 사용합니다.

## 편집 도구

도구는 `CompanyGame/Assets/Editor/WorldMaps/`에 있습니다. Play Mode를 종료한 상태에서 실행합니다.

- `TerracedVillageExpansion.Apply()` / **Tools → Company Game → Maps → Expand Existing Terraced Village**: 확장 전 원본 씬에 최초 한 번만 적용합니다. 이미 확장된 현재 씬에서 다시 실행하지 않습니다.
- `TerracedVillageExpansion.RefineConnections()`: 확장된 씬의 연결부·터널 위치·역 앞 산책로와 소품을 보정합니다. 생성 그룹을 교체하므로 중복 없이 재실행할 수 있습니다.
- `TerracedBuildingDetails.Apply(root)`: `Map_daldongnaemap` GameObject를 전달해 기존 고시원 창문을 재사용합니다. 생성한 창문 그룹만 교체하며 원본 프리팹은 수정하지 않습니다. 호출 후 씬을 저장합니다.
- `VillageSurfaceRepair.Station()`: 역·계단·지형의 배율을 일치시키고 역 앞 곡선 길을 계단 밖으로 이동합니다.
- `VillageSurfaceRepair.Connections()`: 계단·경사로 상단의 전환면, 역 뒤 연결 바닥과 지하 측벽을 보완합니다.
- `VillageSurfaceRepair.SurfaceAudit()`: 보이는 메시의 경로 폭과 역 주변을 검사합니다. 보고서는 `ArtSource/WorldDistricts/QA/village-visible-surfaces.json`입니다.

현재 배치는 씬 인스턴스에서 수정합니다. 초기 Blender/GLB나 이전 전체 맵 생성기로 현재 씬을 덮어쓰지 않습니다.

## 검증 파일

| 파일 | 내용 |
|---|---|
| `assets.json` | 건물 수·스케일, 계단과 경사면, 누락 메시·재질·스크립트, 컴파일 상태 |
| `navigation.json` | CharacterController 경로 이동, 스폰 바닥·여유 공간·간격, 실패 위치와 주변 충돌체 |
| `additional-routes.json` | 확장 골목·진입로·산책로의 검사 경로 좌표 |
| `Overview.png` 등 PNG | 전체 맵, 주거 골목, 한국식 건물, 기존 상점·계단, 오르막길 확인 화면 |

`TerracedVillageQA.Audit()`, `Navigation()`, `Capture()`가 각각 보고서와 화면을 기록합니다. 경로 수·검사 횟수·성공 여부와 실행 모드는 최신 보고서에서 확인합니다. 15개 스폰 지점과 이동 검사는 공간·충돌을 확인하는 절차이며, 네트워크로 플레이어 15명이 동시에 접속한 시험은 아닙니다.
