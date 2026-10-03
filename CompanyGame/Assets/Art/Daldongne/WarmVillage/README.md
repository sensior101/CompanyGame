# 달동네: 서울 외곽의 따뜻한 마을

## 2026-09-27 기존 달동네 확장

현재 실행·편집 대상은 `Assets/Scenes/daldongnaemap.unity`입니다. 원래 한국식 건물 디자인과 1.2 / 3.6 / 7.2 / 10.8 / 14.4m의 다섯 지형 높이를 유지하고, 기존 지형의 가로·세로를 각각 1.5배 넓혔습니다. 건물 크기는 유지합니다.

기존 건물 13동을 보존하고 추가 고시원 복제본을 제거했습니다. 원래 고시원 1동과 기존 한국식 주택을 유지하며, `One Room Neighborhood`에 벽돌·창틀·실외기·배관·옥상 설비를 갖춘 원룸 10동을 배치했습니다. 전체 건물은 23동입니다. 계단 10개와 오르막길 3개, 상단 골목과 역 앞 곡선 산책로를 사용합니다. 역은 일반 주택과 달리 지형과 동일한 가로 배율로 연결합니다.

`Assets/Scenes/Maps/DaldongnePocketGarden.unity`와 `Assets/Scenes/Templates/SmallMapTemplate.unity`는 유지합니다. 이번 확장은 현재 마을 씬에서 편집하며 새 마을 씬을 만들지 않습니다. 도구 사용법과 최신 검증 보고서는 저장소의 `ArtSource/Daldongne/TerracedExpansion/README.md`를 참고하세요.

## 2026-09-19 슈퍼·베이커리 업데이트

빨래방을 같은 위치의 `Supermarket.prefab`으로 교체하고, 베이커리를 목조 2층 빵집으로 다시 만들었습니다.
초록 줄무늬 차양·냉동고·음료 상자가 있는 동네 슈퍼와 노랑 줄무늬 차양·따뜻한 진열창이 있는 빵집입니다.
원본 생성기·미리보기·98개 이동 검사 결과는 `ArtSource/Daldongne/ShopRemodel/README.md`를 참고하세요.

## 2026-09-19 건물 및 고시원 접근로 업데이트

고시원·편의점·Home_A~D를 제공된 사진에 맞춘 새 네이티브 메시로 교체했습니다.
고시원에는 상단 길에서 정문까지 폭 1.8m 보행로와 테라스를 연결하고 담장의 진입 틈을 만들었습니다.
남녀 캐릭터 걷기·달리기 왕복을 포함한 Play Mode 검사 324개가 통과했습니다.
수정 원본·화면·검증 결과는 저장소의 `ArtSource/Daldongne/ReferenceBuildings/`에 있습니다.
생성기는 `CompanyGame/AgentScripts/ReferenceBuildingRemodel.cs`, 접근로 수정기는 `GosiwonAccessRepair.cs`입니다.
기존 전체 맵 Blender/GLB/Unitypackage는 아래 초기 제작 시점의 스냅샷이며 이 변경은 포함하지 않습니다.

Unity 프로젝트의 `Assets/Scenes/daldongnaemap.unity`를 열어 사용합니다.
이전 초안 `DaldongneMap.unity`와 `SampleScene`은 현재 실행 대상에서 제외합니다.

## 반영한 외형

- 지하철역: 청록색 유리 캐노피, 가는 금속 프레임, 노란 점자블록과 2호선 표지.
- 버스 정류장: 철제·유리 쉘터, 벤치, 노선판. 주변에 자전거와 자판기.
- 시청: 붉은 벽돌, 밝은 기둥과 몰딩, 시계가 있는 박공 지붕.
- 베이커리: 바랜 청록 벽, 낮은 꺾인 지붕, 크림·올리브 차양과 빵 진열대.
- 카페: 밝은 회벽과 큰 창, 목재 테라스, 작은 테이블, 메뉴판, 덩굴.
- 나머지 건물: 벽돌과 덧칠한 회벽, 배수관, 실외기, 물탱크, 빨랫줄과 장독.

외형은 편집 가능한 로우폴리 메시와 재질로 제작했습니다. 벽돌과 지붕 디테일은 메시로 묶어 표현하며 외부 텍스처 다운로드가 필요하지 않습니다. 유리에는 투명 URP 재질을 사용합니다.

## 실행과 조작

1. 위 씬을 열고 Unity의 **Play**를 누릅니다.
2. 마우스 휠: 확대·축소 / 오른쪽 드래그: 회전 / 가운데 드래그: 이동.
3. **T**: 위에서 보기 / **Home**: 전체 시점 복귀.
4. **F**: 걷기 모드와 전체 시점 전환. **WASD**: 이동 / **Shift**: 달리기 / **R**: 역 앞으로 복귀.

이 맵은 기존 게임의 `PlayerMovement`와 `PlayerCameraController`를 그대로 사용합니다. 별도 이동 스크립트를 함께 활성화하지 말고, 플레이어는 씬에 하나만 두세요.

## 이동과 충돌

초기 계단 13개 중 `ShopWest`, `WestLower`, `East`를 눈에 보이는 오르막길로 바꾸어 현재는 계단 10개와 오르막길 3개입니다. 남은 계단의 이동용 경사면 `Collider_Ramp_*`는 보이는 계단과 분리되어 있습니다. 기존 연결 보행로에 상단 주거 골목과 역 앞 곡선 산책로를 추가했습니다. 지형, 큰 건물, 옹벽, 발판에는 실제 충돌이 있으며 작은 장식 소품에는 충돌을 넣지 않았습니다.

검사는 반지름 0.35 m, 높이 1.8 m인 플레이어를 기준으로 합니다. 다른 크기의 캐릭터나 NavMesh AI를 사용하면 해당 설정으로 추가 확인해야 합니다. 현재 AI NavMesh는 별도로 굽지 않았습니다.

- `TerracedExpansion/navigation.json`: 확장된 현재 씬의 CharacterController 경로 이동과 15개 스폰 지점 검사. 검사 모드·경로 수·실패 여부는 보고서에서 확인합니다.
- `TerracedExpansion/assets.json`: 현재 건물 수·스케일·계단과 경사면·누락 에셋·컴파일 상태 검사.
- `warm_navigation_validation.json`: 초기 맵의 중심·양옆을 0.25 m 이하 간격으로 검사한 바닥과 캡슐 여유 공간.
- `warm_controller_validation.json`: 초기 맵에서 실제 CharacterController로 각 경로를 양방향 이동한 결과.
- `unity_warm_validation.json`: 초기 Unity 가져오기 결과.
- `warm_glb_validation.json`: 초기 GLB 구조·유효한 좌표·삼각형 인덱스 검사.

위 보고서는 저장소의 `ArtSource/Daldongne/` 아래에 있습니다. 스폰·이동 검사는 공간과 충돌을 확인하며, 네트워크로 플레이어 15명이 동시에 접속한 시험은 아닙니다.

초기 리비전 7 검증 기록: 37개 경로의 2,499개 지점 검사 통과, 실제 CharacterController 왕복 74회 통과. 당시 Unity 컴파일 오류가 없었으며 Play 모드에서 걷기/전체 시점 전환과 역 앞으로 복귀를 확인했습니다. 현재 확장 씬의 결과는 `TerracedExpansion` 보고서를 기준으로 합니다.

## 원본 및 재생성

- `DaldongneWarmTown.blend`: Higgsfield에서 내려받은 편집 원본.
- `DaldongneWarm_FullSource.py`: Blender 5.2에서 전체 맵을 재생성하는 스크립트. **현재 열린 Blender 장면을 비우고 생성**하므로 별도 파일에서 실행합니다.
- `warm_landmarks.py`, `warm_environment.py`, `warm_routes.py`, `warm_finish.py`, `warm_edge_clearance.py`: 건물, 재질·소품, 동선, 후속 보정 모듈.
- Unity의 `Assets/Art/Daldongne/WarmVillage/`: GLB, 네이티브 메시·재질, 프리팹, 후처리 프로필.
- `DaldongneWarm_Unity.unitypackage`: Unity용 배포 묶음. URP와 Input System이 있는 프로젝트용입니다.

Higgsfield 프로젝트: https://higgsfield.ai/3d-jutsu/4238da52-0a82-49af-9f25-15514b37bdc2
