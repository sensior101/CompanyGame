# 회사 건물 01 — Limestone Tower

제공된 두 장의 사진을 기준으로 만든 **비례 검토용 1차 외관 모델**이다.
회사 건물 30종 중 첫 번째 모델이며, 회사명은 아직 지정하지 않았다.

## 크기와 형태

| 항목 | 이 모델 | 현재 한대건설 모델 |
| --- | ---: | ---: |
| 가로 | 88m | 89.584m |
| 깊이 | 64m | 63.959m |
| 최고 높이 | 148m | 160.012m |

높이는 한대건설보다 약 7.5% 낮다. 1 Blender unit = 1m이며, 바닥 기준 Z=0이다.
타워의 기본 평면은 48×40m, 저층부의 높이는 22m이다.

- 일정한 폭의 직사각형 타워, 돌출된 크림색 석재 기둥, 안쪽으로 들어간 창
- 일부 기둥을 연결하는 큰 가로 띠와 작은 높이 차이를 가진 상부 끝단
- 검은 프레임의 유리 저층부, 옆면의 테라코타색 수직 루버
- 뒤로 들어간 1층 출입구와 후면 하역 셔터

## 결과 파일

- `Office01_LimestoneTower_Blockout.blend`: 편집 가능한 Blender 원본. 컬렉션, 재질, 렌더 카메라 포함
- `UnityExport/Office01_LimestoneTower_Blockout.fbx`: 모델과 배치 기준점만 내보낸 FBX
- `UnityExport/Office01_LimestoneTower_Blockout.glb`: 같은 모델의 GLB
- `BlockoutRenders/`: 제작 명령으로 다시 생성하는 Blender 검토 렌더. 생성 이미지는 버전 관리하지 않는다.
- `References/`: 사용자가 제공한 원본 사진의 보관용 사본
- `model_manifest.json`: 모델 치수, 메시·재질 정보, 제작 범위
- `Validation/verify_roundtrip.py`: FBX·GLB 재임포트 공통 검증 도구. 결과 JSON과 파일 해시는 실행할 때 생성한다.

## 제작 및 재사용

기존 `ArtSource/WorldDistricts/HandaeHQ/v02/build_handae.py`의 `MeshBatch`, `box`, `loft`를
AST로 읽어 재사용한다. 한대건설의 장면 생성 코드는 실행하지 않는다.
`build_office01.py`는 이 건물의 배치·형태·재질·출력만 정의하는 Blender 제작 레시피이다.
기존 메시 생성 기능을 복제하거나 게임 공통 기능을 추가하지 않았다.

제작 흐름: 참고 사진 및 한대건설 치수 → 기존 메시 제작 도구 → Blender 모델 → FBX·GLB·렌더 → 새 장면 재임포트 검사.

재생성 명령(PowerShell, 저장소 루트):

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --factory-startup --python 'ArtSource/WorldDistricts/CompanyBuildings/01_LimestoneTower/build_office01.py'
```

이 명령은 이 폴더의 생성 결과를 다시 저장한다. 렌더를 생략하려면 뒤에 `-- --skip-render`를 붙인다.

검증 명령:

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --factory-startup --python 'ArtSource/WorldDistricts/CompanyBuildings/01_LimestoneTower/Validation/verify_roundtrip.py'
```

## 검증 및 영향

- 이 작업에서 기존 파일 수정 없음. 생성 파일은 이 건물의 원본·출력·검증 자료이다.
- 신규 런타임 스크립트 없음. 기존 API, 데이터, 호출부와 Unity 장면에 변경 없음.
- 모델: 16개 메시, 55,044개 삼각형, 8개 모델 재질.
- FBX·GLB를 각각 빈 Blender 장면에 다시 불러와 88×64×148m 치수, UV, 유효한 좌표, 재질을 확인했다.
- 면적 0인 삼각형 0개. 렌더용 카메라·조명·배경이 내보낸 모델에 포함되지 않은 것을 확인했다.
- 전면·후면 렌더를 확인하고 창 가림, 겹친 지붕 면, 셔터와 기둥 간섭을 수정했다.

현재는 전체 비례와 주요 외관 구성을 검토하는 단계이다. 석재 줄눈·최종 텍스처,
입구 사인·회사명, 지붕 설비 마감은 후속 상세 작업이다.
전체 실내, Unity 배치·재질 연결·충돌체·LOD·라이트맵 및 실제 게임 화면 검증은 아직 수행하지 않았다.
