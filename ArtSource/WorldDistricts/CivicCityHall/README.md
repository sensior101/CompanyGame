# 시청 — 1차 외관 모델

사용자 시청 참고 이미지의 낮고 넓은 3층 석재 공공청사를 Blender에서 제작했다.
실제 CivicDistrict에서 사용하는 도서관 외관의 부지를 측정해 크기를 정했다.

## 크기와 구성

| 항목 | 도서관 비교 기준 | 시청 |
|---|---:|---:|
| 포장 부지 | 38 × 29 m | 42 × 34 m |
| 부지 면적 | 1,102 m² | 1,428 m² (+29.58%) |
| 본체 기준 평면 | 30 × 18 m | 34 × 20 m |
| 최고점 | 지면 위 9.68 m | 15.80 m |

도서관 부지 기준은 현재 외관 FBX의 `Site_38x29` 메시다. 도서관 지붕을 포함한
전체 외곽 깊이는 30.13 m이며, 별도 V4 실내 바닥 크기와 구분했다.
시청은 1 Blender unit = 1 m, Z up, 정면 -Y, 부지 바닥 Z=0,
광장 상면 Z=0.14, 정문 바닥 Z=0.92다.

- 중앙 석재 포털, 2개의 원형 기둥과 3층 유리 입면
- 얇고 넓은 현관 캐노피, 계단과 연속된 반환 경사로
- 전면 광장, 낮은 식재대, 나무, 벤치
- 태극기와 시기, `시청` 입체 간판 및 전면 석재 표지
- 좌·우·후면 창호, 후문, 낮은 옥상 설비와 채광부

## 결과 파일

- `CivicCityHall_Blockout.blend`: 실제 Blender 모델, 재질, 4개 검토용 카메라
- `UnityExport/CivicCityHall_Blockout.fbx`, `.glb`: 프레젠테이션 요소를 제외한 모델
- `BlockoutRenders/`: 제작 명령으로 다시 생성되는 앞·뒤 투시도, 정면도, 부지 상부도. 생성 이미지는 버전 관리하지 않는다.
- `model_manifest.json`: 치수, 메시/삼각형 수, 세부 배치 정보
- `Reference_CityHall.png`: 제공받은 참조 이미지 보존본

## 기존 파일 수정

이번 시청 작업에서는 기존 제작 도구와 Unity 파일을 수정하지 않았다.
모든 신규 산출물은 이 `CivicCityHall` 폴더에 있다. 이전 작업의 미커밋 변경은 유지했다.

## 신규 제작 파일과 책임

- `build_cityhall.py`: 이 시청의 외형 배치, 기존 도구 연결, 저장/내보내기/렌더 실행.
- `site_design.py`: 시청 부지에 맞는 계단·경사로·조경 배치값만 담당.
- `signage_design.py`: 시청 간판과 깃발의 자산별 메시 배치만 담당.
- `Validation/inspect_library_dimensions.py`: 기존 도서관 FBX 치수를 읽는 검토용 코드.
- 검증 JSON은 검증 명령으로 생성하는 로컬 출력이며 버전 관리하지 않는다.

위 Python 파일은 Blender 제작 레시피이며 게임 실행 스크립트가 아니다.
신규 시청의 외관·부지·상징물이라는 독립적인 아트 데이터를 보관하기 위해 생성했다.

## 재사용한 기존 도구

- `HandaeHQ/v02/build_handae.py`: `MeshBatch`, `box`, `loft`
- `CompanyBuildings/01_LimestoneTower/build_office01.py`: `material`, `camera`
- `CompanyBuildings/OfficeSet_ABCD/build_office_set.py`: `finish`, `panel_grid`, `merge_batches`
- `CivicLibrary/build_library.py`: `assign`, `uv_box`, `box`, `beam`, `ico`, `ramp`, `ramp_rail`, `shrub`
- `CompanyBuildings/01_LimestoneTower/Validation/verify_roundtrip.py`: 공통 FBX/GLB 검증
- 기존 `NotoSansKR-Regular.ttf`: 한글 간판을 실제 메시로 변환

기존 스크립트에서 필요한 정의만 AST로 읽어 기존 건물의 장면 생성 코드는 실행하지 않는다.

## 데이터 흐름·중복·영향

도서관 실측 → 시청 배치 레시피 → 기존 메시/재질 함수 → 소유 컬렉션별 병합 →
Blender 원본 및 FBX/GLB → 기존 공통 검증기로 재수입 확인.

공통 메시 생성·계단 경사로 원시함수·창호 그리드·검증기를 새로 복제하지 않았다.
새 게임 관리자, 거래/인벤토리/금전/로그 시스템, Unity API 변경은 없다.
기존 런타임 호출부·씬·저장 데이터에 영향이 없다.

## 검증과 후속 범위

- 도서관 원본 FBX와 현재 Unity 외관 참조 및 부모 스케일을 확인했다.
- Blender에서 실제 4방향 렌더를 만들고 앞·뒤·정면·위의 외형을 확인했다.
- 광장, 6단 정문 계단, 2회 경사로와 연결부 높이를 좌표로 점검했다.
- 한글 폰트를 메시로 변환해 주간판 글자 높이 0.85 m, 표지석 0.42 m를 측정했다.
- FBX·GLB를 각각 새 Blender 장면으로 재수입해 모두 통과했다. 각 형식은 메시 30개,
  삼각형 31,198개이며 실제 크기 42 × 34 × 15.8 m와 일치한다.
  UV0, 유효 좌표, 재질 슬롯, 퇴화 삼각형 없음, 단일 루트, 프레젠테이션 제외도 확인했다.
  아래 검증 명령을 실행하면 `Validation/export_roundtrip.json`을 다시 생성한다.
- 경사로 끝의 포털 앞 회전 깊이를 1.43 m로 확보하고, 후문은 계단과 화단 개방부로 연결했다.
- 아직 1차 외관 비례 검토 단계다. 실내, 실제 작동하는 문, Unity 배치, 충돌,
  LOD, 라이트맵, NavMesh, 게임 내 보행 검증은 후속 범위다.

## 다시 생성

저장소 루트에서 Blender 5.2로 실행:

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --factory-startup --python-exit-code 1 --python 'ArtSource/WorldDistricts/CivicCityHall/build_cityhall.py'
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --factory-startup --python-exit-code 1 --python 'ArtSource/WorldDistricts/CompanyBuildings/01_LimestoneTower/Validation/verify_roundtrip.py' -- --model-dir 'ArtSource/WorldDistricts/CivicCityHall'
```

렌더 없이 모델/내보내기만 다시 만들려면 첫 명령 뒤에 `-- --skip-render`를 추가한다.
