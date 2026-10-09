# Civic Library / 행정지구 도서관 외관

첨부 레퍼런스를 기준으로 만든 Unity용 2층 도서관 외관입니다. 실제 Blender 메시이며 카메라와 조명은 FBX에 포함하지 않습니다.

## 파일

- `CivicLibrary.blend`: 편집 가능한 원본, 모델과 프리뷰 전용 컬렉션 분리.
- `CivicLibrary.fbx`: Unity 임포트용 모델. 35,544 triangles, 42 mesh objects.
- `Textures/`: 반복 가능한 벽돌 컬러/노멀, 우드 컬러 텍스처.
- `material_manifest.json`: URP 재질 이름과 텍스처 매핑, 실측 치수, 접근 경로 정보.
- `build_library.py`: 전체 모델 재생성 스크립트.

## 치수와 동선

- 단위: 1 Blender unit = 1 m. Blender 정면은 -Y, Unity 정면은 -Z.
- 본체: 폭 30 m × 깊이 18 m, 지상층 바닥 높이 0.75 m, 지붕 상단 9.68 m.
- 전체 조경/처마 포함 경계: X -19..19 m, Y -20..10.13 m, Z -0.282..9.68 m.
- 전면 계단: **5개**, 각 높이 0.15 m, 디딤 깊이 0.42 m, 폭 6.4 m.
- 오른쪽 ㄷ자 경사로: 각 경사 구간 7.5 m, 폭 1.8 m, 경사 1:20; 회전참 2 × 4 m.
- 경사로 정상의 오른쪽 난간은 출구 폭 1.8 m를 비워 두었습니다.
- 왼쪽 전면 벤치: **1개**.
- 책장과 독서 가구는 유리창 너머로 보이는 장식용 내부입니다.

Blender 좌표에서 경사로 진입 (7,-16.1,0.018) → (14.5,-16.1,0.393) → 오른쪽 회전참 → (14.5,-13.9,0.393) → (7,-13.9,0.768) → (6.1,-11,0.768) → 중앙 현관 방향입니다.

## 계층과 Unity 재질

`CivicLibrary` 아래 `Structure`, `Glass`, `Frames`, `Wood`, `Roof`, `Steps`, `Ramp`, `Railings`, `Bench`, `Landscape`, `InteriorHint`로 정리했습니다. 메시를 카테고리와 재질별로 합쳐 드로우콜을 줄였고, 계단 및 경사로는 개별 이름을 유지했습니다.

재질은 모두 `CL_` 접두어입니다. `CL_Glass`는 URP Lit / Transparent / alpha 0.18 / smoothness 0.92 / 양면으로 매핑합니다. 잎 재질 두 종류도 양면이 필요합니다. FBX 재질을 그대로 쓰지 말고 manifest를 기준으로 URP에 연결합니다. Blender 유리는 실제 transmission 셰이더를 사용합니다.

충돌체는 구조물, 계단, 경사로, 난간, 벤치, 유리와 프레임에 연결하고 잎/관목 등 조경과 장식용 내부에는 연결하지 않습니다. 전면 포장면 `Site_38x29`의 상단은 기존 지면보다 0.018 m 높습니다. Unity 통합은 별도 `UnityLibraryIntegration.cs`에서 관리합니다.

## 재생성

PowerShell:

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --python './ArtSource/WorldDistricts/CivicLibrary/build_library.py'
```

모델/FBX만 다시 만들 때는 뒤에 `-- --no-render`를 붙입니다. 난수 seed가 고정되어 같은 형상으로 재생성됩니다. `_Presentation_ONLY` 컬렉션은 렌더 배경, 카메라, 조명이며 FBX에서 제외됩니다.

검토용 렌더와 QA 결과 파일은 저장소에서 제외합니다. 재생성이 필요하면 위 도구를 사용하세요.
