# 달동네: 서울 외곽의 따뜻한 마을

## 2026-09-19 슈퍼·베이커리 업데이트

빨래방 자리를 초록 줄무늬 차양의 동네 슈퍼로 교체하고, 빵집을 노랑 차양의 목조 2층 건물로 수정했습니다.
현재 Unity 편집 원본·미리보기·검증 결과는 `ShopRemodel/README.md`에 정리했습니다.

## 2026-09-19 건물 및 고시원 접근로 업데이트

고시원·편의점·Home_A~D를 사진 기반 Unity 네이티브 메시로 다시 제작했습니다.
폭 1.8m 고시원 접근로와 정문 앞 테라스, 담장 진입 틈을 반영했습니다.
남녀 캐릭터 걷기·달리기 왕복을 포함한 Play Mode 검사 324개 통과.
최신 편집 원본·미리보기·검증 결과는 `ReferenceBuildings/README.md`를 참고하세요.
아래 Blender/GLB/Unitypackage 파일은 초기 맵 스냅샷이며 이번 리모델링을 포함하지 않습니다.

Unity 프로젝트의 `Assets/Scenes/daldongnaemap.unity`를 열어 사용합니다.
기존 `DaldongneMap.unity`와 SampleScene은 이전 버전으로 남겨두었습니다.

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

걷기 모드는 이 맵을 확인하기 위한 별도 `DaldongneVillageWalker`입니다. 기존 게임의 PlayerMovement를 교체하지 않습니다.

## 이동과 충돌

계단 13개와 연결 보행로 24개를 구성했습니다. 보이는 계단과 보이지 않는 이동용 경사면 `Collider_Ramp_*`를 분리했습니다. 지형, 큰 건물, 옹벽, 발판에는 실제 충돌이 있으며 작은 장식 소품에는 충돌을 넣지 않았습니다.

검사는 반지름 0.35 m, 높이 1.8 m인 플레이어를 기준으로 합니다. 다른 크기의 캐릭터나 NavMesh AI를 사용하면 해당 설정으로 추가 확인해야 합니다. 현재 AI NavMesh는 별도로 굽지 않았습니다.

- `warm_navigation_validation.json`: 중심·양옆을 0.25 m 이하 간격으로 검사한 바닥과 캡슐 여유 공간.
- `warm_controller_validation.json`: 실제 CharacterController로 각 경로를 양방향 이동한 결과.
- `unity_warm_validation.json`: Unity 가져오기 결과.
- `warm_glb_validation.json`: GLB 구조·유효한 좌표·삼각형 인덱스 검사.

최종 리비전 7 검증: 37개 경로의 2,499개 지점 검사 통과, 실제 CharacterController 왕복 74회 통과. Unity 컴파일 오류 없음. Play 모드에서 걷기/전체 시점 전환과 역 앞으로 복귀도 확인했습니다.

## 원본 및 재생성

- `DaldongneWarmTown.blend`: Higgsfield에서 내려받은 편집 원본.
- `DaldongneWarm_FullSource.py`: Blender 5.2에서 전체 맵을 재생성하는 스크립트. **현재 열린 Blender 장면을 비우고 생성**하므로 별도 파일에서 실행합니다.
- `warm_landmarks.py`, `warm_environment.py`, `warm_routes.py`, `warm_finish.py`, `warm_edge_clearance.py`: 건물, 재질·소품, 동선, 후속 보정 모듈.
- Unity의 `Assets/Art/Daldongne/WarmVillage/`: GLB, 네이티브 메시·재질, 프리팹, 후처리 프로필.
- `DaldongneWarm_Unity.unitypackage`: Unity용 배포 묶음. URP와 Input System이 있는 프로젝트용입니다.

Higgsfield 프로젝트: https://higgsfield.ai/3d-jutsu/4238da52-0a82-49af-9f25-15514b37bdc2
