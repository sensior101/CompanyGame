# 회사 건물 02–05 / A·B·C·D

제공된 4종 기획 이미지를 바탕으로 만든 Blender 외관 모델이다.
앞선 01번 건물과 같은 **비례 및 주요 외관 검토 단계**이며 기본 재질을 포함한다.
완성된 상세 텍스처나 Unity 장면 배치 결과는 아니다.

## 유형 및 실제 치수

단위는 m, 순서는 가로 × 깊이 × 높이이다. 가로·깊이는 낮은 기단을 포함한다.

| 유형 | 에셋 | 치수 | 외관 특징 |
| --- | --- | --- | --- |
| A | Office02_SlimGlass | 72 × 62.4 × 148 | 푸른 유리, 은색 세로 핀, 작은 단차가 있는 크라운 |
| B | Office03_UrbanTerrace | 81.6 × 64.8 × 144 | 석재·유리 격자, 상부 3단 테라스 |
| C | Office04_MinimalDark | 74.4 × 62.4 × 146 | 짙은 금속 세로 핀, 평평한 지붕, 따뜻한 색의 로비 |
| D | Office05_ClassicStone | 76.8 × 64.8 × 148 | 크림색 석재 격자, 기둥과 상부 코니스 |

01번 기준은 88 × 64 × 148m이다. 새 모델들은 높이가 97.3–100% 범위이고,
깊이도 비슷하다. 사진의 타워 형태에 맞춰 넓은 다층 포디움 대신 간결한 저층부를 구성했다.
각 `design.py`의 기준 평면은 공통 제작 도구에서 X/Y 1.2배를 정점에 적용하여 위 치수로 내보낸다.
루트 스케일은 1이며, 1 Blender unit = 1m를 유지한다.

## 결과 위치

| 유형 | 모델 폴더 |
| --- | --- |
| A | `../02_SlimGlass/` |
| B | `../03_UrbanTerrace/` |
| C | `../04_MinimalDark/` |
| D | `../05_ClassicStone/` |

각 폴더에는 다음 자료가 있다.

- `Office0N_..._Blockout.blend`: 독립적으로 편집 가능한 Blender 원본
- `UnityExport/`: 동일 모델의 FBX·GLB
- `BlockoutRenders/`: 제작 명령으로 다시 생성하는 전면 사선·후면 사선·정면도. 생성 이미지는 버전 관리하지 않는다.
- `design.py`: 유형별 형상 제작 레시피
- `model_manifest.json`: 실제 치수·메시·삼각형·재질·제작 범위
- `Validation/export_roundtrip.json`: 검증 명령으로 생성하는 로컬 검사 결과. 보고서는 버전 관리하지 않는다.

`render_comparison.py`를 실행하면 4종을 동일 축척·각도로 배치한 `Comparison.blend`와
`Comparison_ABCD.png`가 생성된다. 비교 장면·이미지·결과 JSON은 버전 관리하지 않는다.
배치용 건물 FBX에는 비교용 배경·문자·카메라·조명이 포함되지 않는다.
`Reference_ABCD.png`는 제공된 이미지의 보관 사본이다.

## 기존 도구 재사용 및 파일 변경

1. **수정한 기존 파일:** `../01_LimestoneTower/Validation/verify_roundtrip.py`.
   `validate_model(model_dir, report_path=None)`로 확장하여 다른 모델의 manifest도 검사한다.
   인자 없이 실행하면 기존 01번 모델을 검사하는 동작을 유지한다.
2. **새 파일:** 유형별 Blender 모델·내보내기·렌더·형상 레시피, 공통 제작 레시피,
   비교 렌더 스크립트, 검증 호출 스크립트와 문서. 새 건물의 독립적인 형상과 전달 자료를 보관하기 위한 것이다.
3. **재사용한 코드:** 한대건설의 `MeshBatch`, `box`, `loft`; 01번 모델의 `material`, `camera`, `light`;
   기존 내보내기 검사 기능. 원본 제작 도구의 장면 생성 부분은 실행하지 않고 필요한 정의만 사용한다.
4. **처리 흐름:** 유형별 형상 레시피 → 공통 창호·메시 제작 → 치수 적용 → Blender 원본 저장 →
   FBX·GLB 내보내기 → 동일 검증 함수로 새 장면에 재임포트 → 비교 렌더.
5. **중복 구현:** 유형별 레시피에는 형상만 정의한다. 재질·카메라·내보내기·검증을 유형마다 따로 만들지 않는다.
6. **기존 기능 영향:** 게임 런타임 스크립트, Unity 씬·프리팹·저장 데이터·공개 API 변경 없음.
   01번 건물의 메시와 재질 원본도 변경하지 않았다.
7. **검증:** 소스의 유리 가림·문과 기둥 간섭·겹친 지붕 면을 확인하고 수정했다.
   아래 검증 명령으로 각 모델과 이 폴더의 `Validation/export_roundtrip.json`을 다시 생성한다.
   기존 01번 호환성은 `../01_LimestoneTower/Validation/verify_roundtrip.py`를 인자 없이 실행해 확인한다.

### 최종 내보내기 검사

Blender 5.2.1의 빈 장면에 FBX와 GLB를 각각 불러와 **8/8 통과**했다.

| 유형 | 메시 | 삼각형 | FBX | GLB |
| --- | ---: | ---: | --- | --- |
| A | 13 | 29,388 | 통과 | 통과 |
| B | 11 | 20,820 | 통과 | 통과 |
| C | 11 | 74,760 | 통과 | 통과 |
| D | 8 | 23,244 | 통과 | 통과 |

치수·루트·메시/삼각형 수를 manifest와 대조하고 UV, 유효 좌표, 면적 0인 면,
재질 누락, 카메라·조명·배경 혼입, 검사 중 원본 불변을 확인했다.
공통 검사 도구를 바꾼 뒤 기존 01번 모델의 두 포맷도 동일한 검사를 통과했다.

## 재생성

저장소 루트에서 PowerShell로 실행한다. 제작 명령은 선택한 모델의 생성 결과를 다시 저장한다.

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --factory-startup --python-exit-code 1 --python 'ArtSource/WorldDistricts/CompanyBuildings/OfficeSet_ABCD/build_office_set.py'
```

특정 유형만 만들려면 끝에 `-- --types B,D`, 렌더를 생략하려면 `-- --skip-render`를 붙인다.

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --factory-startup --python-exit-code 1 --python 'ArtSource/WorldDistricts/CompanyBuildings/OfficeSet_ABCD/verify_office_set.py'
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --factory-startup --python-exit-code 1 --python 'ArtSource/WorldDistricts/CompanyBuildings/OfficeSet_ABCD/render_comparison.py'
```

## 남은 범위

상세 텍스처·석재 줄눈·회사 사인·실내는 후속 작업이다.
Unity 배치, 게임용 재질 연결, 충돌체, LOD, 라이트맵, 실제 게임 화면은 아직 검증하지 않았다.
이미지에 적힌 층수·면적 수치를 실사 설계도처럼 복제하기보다, 사용자 요청에 따라
첫 번째 게임 모델의 규모에 맞춰 창호 반복과 층 구성을 조정했다.
