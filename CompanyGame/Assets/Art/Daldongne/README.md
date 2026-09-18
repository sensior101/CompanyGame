# 달동네 맵 사용 방법

`Assets/Scenes/DaldongneMap.unity`를 열고 Play를 누르세요. 기존 SampleScene은 유지되어 있습니다.

| 입력 | 동작 |
|---|---|
| 마우스 휠 | 확대·축소 |
| 오른쪽 버튼 드래그 | 회전 |
| 가운데 버튼 드래그 | 평행 이동 |
| T | 탑다운 / 사선 뷰 전환 |
| Home | 초기 구도 복원 |

## 파일

- `DaldongneTown.glb`: 다른 3D 도구에서도 열 수 있는 원본 게임용 모델
- `DaldongneTown.prefab`: Unity 네이티브 메시·재질·충돌체가 연결된 전체 맵
- `DaldongneMeshes.asset`: 166개 공유 메시와 29개 재질을 담은 에셋
- `Daldongne_RenderPipeline.asset`: 기존 PC 렌더 파이프라인을 복제한 맵용 설정. 그림자 거리 160m, MSAA 4x
- `../../Scripts/World/DaldongneMapCamera.cs`: 맵 관찰 카메라

블렌더 원본과 사선·탑다운 프리뷰, 모델 생성 소스는 저장소 루트 `ArtSource/Daldongne`에 있습니다. `Daldongne_Unity.unitypackage`는 이 맵을 다른 Unity 프로젝트에 가져오기 위한 패키지입니다. Unity 6 URP 프로젝트에서 가져온 뒤 필요하면 Quality 설정의 Render Pipeline Asset에 맵용 렌더 파이프라인을 지정하세요.

## 제작·검증

사용자가 준 두 참고 이미지의 주요 시설과 단차 지형을 바탕으로 제작했습니다. 약 7만 4천 삼각형이며, 한글 간판도 메시입니다. 기본 지형·계단·건물 충돌체 218개와 플레이어 배치용 시작 지점이 포함되어 있습니다. 현재 품질 프리셋은 맵용 렌더 파이프라인을 사용합니다. 원래 `PC_RPAsset.asset`은 수정하지 않았습니다.

Blender Eevee 사선·탑다운 렌더와 GLB 구조 검사, Unity 가져오기·플레이 모드·동쪽 계단 충돌 레이 검사를 통과했습니다. 검증 시 Unity 컴파일 및 콘솔 오류는 없었습니다.

캐릭터 이동, 실내 공간, 상점 기능, NPC AI 및 NavMesh 탐색은 별도 게임 로직으로 연결하면 됩니다. 기본 Collider는 지형과 주요 건물에만 적용되어 있습니다.

프로젝트 `AgentScripts/DaldongneImport.cs`는 이 파일의 색상 기반 비압축 GLB 형식에 맞춘 제작용 가져오기 코드입니다. 범용 glTF 가져오기 도구는 아닙니다. 모델을 다시 내보낸 경우 기존 Unity 에셋이 자동 갱신되지는 않습니다.
