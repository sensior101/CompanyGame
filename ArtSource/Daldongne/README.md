# 달동네 로우폴리 게임 맵

사용자가 제공한 사선·탑다운 이미지 두 장의 주요 시설과 언덕 마을 구성을 참고해 Higgsfield 3D Jutsu에서 제작했습니다. 이미지 텍스처를 붙인 평면이 아니라 편집 가능한 3D 메시입니다.

- Higgsfield 프로젝트: https://higgsfield.ai/3d-jutsu/4238da52-0a82-49af-9f25-15514b37bdc2
- Blender 원본: `DaldongneTown.blend`
- 게임용 GLB: `../../CompanyGame/Assets/Art/Daldongne/DaldongneTown.glb`
- Unity 진입점과 조작 방법: Unity 에셋 폴더에 함께 제공한 README 참고
- Unity 배포 패키지: `Daldongne_Unity.unitypackage`
- 사선 및 탑다운 프리뷰: `Daldongne_Isometric.png`, `Daldongne_TopDown.png`

## 구성

햄버거 가게, 편의점, 분식집, 베이커리, 카페, 빨래방, 고시원, 시청, 쉼터, 주택 4채, 달동네역, 버스정류장과 버스, 차량, 가로등, 벤치, 가로수 및 화단. 한글 간판은 외부 폰트가 없어도 표시되는 실제 메시로 구성했습니다.

지형은 해안의 낮은 구역에서 상점가와 세 단계의 언덕으로 이어집니다. 계단 통로의 지형과 옹벽을 파내고, 동쪽 계단을 주택 옆으로 옮겼습니다. 지하철 입구에는 아래로 내려가는 계단이 있으며 도로 출구에는 지지 구조가 있습니다.

Blender는 Z-up, 미터 단위입니다. GLB는 표준 Y-up 좌표를 사용합니다. 렌더 프레임에 포함된 바다 받침까지 전체 너비는 약 80m입니다. 반복 부품은 공유 메시를 사용하고, Blender 컬렉션은 지형·이동 경로·시설·식생·차량과 인물·소품·카메라와 조명으로 구분했습니다.

## 원본 다시 만들기

`python assemble_source.py`는 작업 단계들을 `DaldongneTown_Source.py` 하나로 합칩니다. Blender 5.2의 **빈 새 파일**에서 이 파일을 실행하면 같은 맵을 다시 생성할 수 있습니다. 원본 `.blend`에서는 별도 실행 없이 각 부품을 편집하면 됩니다.

`build_map.py`는 지형과 배치, `props.py`는 건물·소품·한글, `refine_map.py`와 `finish_map.py`는 통로·접속부 보정 및 컬렉션 구성을 담당합니다. 원본을 수정한 뒤에는 다시 조립하고 모델을 내보내야 Unity에 반영됩니다.

## 검증 범위

Blender Eevee 카메라 렌더, GLB 바이너리/인덱스/유한 좌표 검사, 주요 계단 경로의 하향 레이 검사로 구조를 확인했습니다. Unity에 네이티브 메시 166개·재질 29개·충돌체 218개를 가져왔고, 별도 `DaldongneMap.unity` 씬에서 사선·탑다운 카메라 렌더와 플레이 모드, 계단 Collider 레이 검사를 통과했습니다. Unity 컴파일 및 콘솔 오류는 0개였습니다. Unity의 이동 캐릭터, 퀘스트, 교통 AI, 실내 공간 및 NavMesh 경로 탐색은 이 모델의 범위에 포함되지 않습니다.
