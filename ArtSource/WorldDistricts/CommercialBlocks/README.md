# Commercial Blocks — Blender 상가 블록

사용자가 새로 제공한 도안 8개를 기준으로 `CB_01`–`CB_08`을 각각 독립적으로 모델링하는 상가 외관 팩입니다. 확정된 공통 규격은 **본체 12 × 8 m, 본체 3층**이며 옥상 구성은 각 도안을 따릅니다. 각 도안의 외장·창 배치를 따르고 간판은 교체할 수 있습니다. 모델은 한 개씩 제작되며 최종 생성 여부와 개별 치수는 `Catalog/catalog.json`에서 확인합니다.

## 도안 순서

첨부 도안의 윗줄 왼쪽부터 01–04, 아랫줄 왼쪽부터 05–08입니다. 각각 별도의 제작 파일을 사용합니다.

| 모델 | 디자인 | 개별 제작 원본 |
| --- | --- | --- |
| CB_01 | 콘크리트 리본과 수직 금속 코어 | `Designs/01_concrete_core.py` |
| CB_02 | 아이보리 프레임과 세로 오크 | `Designs/02_ivory_oak.py` |
| CB_03 | 적벽돌 로프트와 루프 퍼골라 | `Designs/03_brick_loft.py` |
| CB_04 | 둥근 코너와 연속 화이트 리본 | `Designs/04_rounded_ribbon.py` |
| CB_05 | 차콜 캐노피와 깊은 테라스 | `Designs/05_charcoal_terrace.py` |
| CB_06 | 베이지 조적과 옥상 정원 | `Designs/06_sandstone_garden.py` |
| CB_07 | 엇갈린 아이보리 오픈 테라스 | `Designs/07_ivory_portals.py` |
| CB_08 | 그레이 조적과 코너 로지아 | `Designs/08_stepped_masonry.py` |

`Catalog/index.html`을 열면 8종 렌더를 확대 비교하고 BLEND·FBX를 열 수 있습니다. 한 모델만 다시 제작하려면 팩 폴더에서 다음처럼 번호를 지정합니다.

```powershell
& "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" --background --python "build_blocks.py" -- 4
```

## 파일 구성

| 경로 | 용도 |
| --- | --- |
| `Models/CB_XX.blend` | Blender 편집 원본. 모델과 촬영용 `Presentation_ONLY` 컬렉션이 분리되어 있습니다. |
| `Exports/CB_XX.fbx` | 모델 계층과 재질 이름을 보존한 Unity 전달용 FBX. 촬영용 바닥·카메라·조명은 제외됩니다. |
| `Exports/Textures` | 외장·포장 재질용 PNG. 텍스처이므로 삭제하면 안 됩니다. |
| `Catalog/catalog.json` | 모델별 치수·층 구성·디자인·재질·간판 정보. 제작 중에는 완성된 항목만 있을 수 있습니다. |
| `Catalog/Previews` | 모델별 Blender 렌더 미리보기. Unity 실행 화면이 아닙니다. |
| `build_blocks.py` | Blender 생성 원본 스크립트. 다시 실행하면 생성 파일을 덮어쓰므로 수작업 수정본은 별도로 보관하세요. |
| `Tools/ImportCommercialBlocks.cs` | 필요할 때 설치하는 Unity Editor 임포트 메뉴. 현재 Unity 프로젝트에 자동 설치하지 않습니다. |
| `Tools/validate_exports.py` | Blender에서 FBX를 다시 불러와 원본 메타데이터와 비교하는 검증 스크립트. 원본 모델을 저장하거나 수정하지 않습니다. |
| `QA/PreviousBatch` | 이전 제작본을 보존하는 위치. 현재 8개 도안의 최종 에셋에 포함하지 않습니다. |

## 치수와 배치

- Blender 단위는 m입니다. 본체 공통 평면은 12 × 8 m이고 본체는 3층입니다. 건물마다 `width`, `depth`, `floorHeights`, `roofSlabHeight` 값을 확인합니다.
- 각 층의 실제 높이는 `floorHeights` 항목을 따릅니다. 옥탑·테라스·난간벽·설비 등 옥상 구성과 전체 높이는 해당 도안을 따릅니다.
- 난간벽·옥상 설비·기단·처마·간판·배수관은 본체에서 돌출될 수 있습니다. `exportBounds`는 이 부분을 포함한 전체 메시 경계이므로 배치 간격과 검증에 사용합니다.
- Blender에서 앞쪽은 `-Y`, 위쪽은 `+Z`입니다. FBX는 Unity용 축 설정으로 출력합니다. 임포트 루트의 변환을 임의로 초기화하지 말고 모델 전체를 회전하여 배치하세요.
- `EntryAnchor`, `InteriorSpawnAnchor`는 출입 위치 연결용 표시입니다. 포털·텔레포트 스크립트가 연결되어 있지는 않습니다.
- 유리 뒤 공간과 가구 실루엣은 외관 표현용입니다. 이동 가능한 실내맵·상층 계단·상점 시스템은 포함하지 않습니다.

## 간판과 문

| 이름 | 역할 |
| --- | --- |
| `Sign_Main_01` | 첫 번째 전면 간판 |
| `Sign_Main_02` | 두 번째 전면 간판 |
| `Sign_Corner` | 모서리 쪽 간판 연결 위치 |
| `Sign_Side` | 측면 간판 연결 위치 |
| `Door_Leaf_01`, `Door_Leaf_02` | 경첩 기준으로 분리된 양쪽 유리문 그룹 |

간판의 실제 크기와 위치는 모델별 카탈로그 `signs` 항목을 확인합니다. 그룹 아래의 `_Board` 메시에는 0–1 이미지 UV가 있습니다. 상점별 새 재질을 만들어 `_BaseMap`에 간판 이미지를 넣고 해당 보드 렌더러에 지정하거나, 보드를 숨긴 뒤 간판 프리팹을 붙일 수 있습니다. 간판 그룹의 위치와 회전을 유지하세요. 한 건물의 여러 보드는 기본 재질을 공유하므로 서로 다른 간판을 넣을 때는 보드별 재질을 복제합니다.

문은 프레임·유리·손잡이가 각 문 그룹에 묶여 있습니다. 개폐 애니메이션이나 자동문 로직은 없으며 추후 해당 그룹의 로컬 회전을 제어하면 됩니다. 뒤쪽 관리문은 고정된 외관 장식입니다.

## Unity URP 선택 임포트

현재 프로젝트의 `Packages/manifest.json`에서 URP **17.4.0**, 설치된 패키지에서 **`Universal Render Pipeline/Lit`** 셰이더를 확인했습니다. 아래 도구는 선택한 새 에셋 폴더에만 출력하고 기존 씬·빌드 설정·게임 컴포넌트를 변경하지 않습니다.

1. 최종 `Catalog/catalog.json`에 revision 2 이상의 개별 디자인 8개가 모두 생성되었는지 확인합니다.
2. `Tools/ImportCommercialBlocks.cs`를 사용할 Unity 프로젝트의 `Assets/Editor` 폴더로 직접 복사합니다.
3. Unity 컴파일 후 **Tools → Company Game → Import Commercial Blocks (URP)** 메뉴를 선택합니다.
4. 이 팩의 `Catalog/catalog.json`을 선택하고 Unity `Assets` 안의 출력 위치를 선택합니다.
5. 확인하면 새 `CommercialBlocks` 폴더가 생성됩니다. 같은 이름이 이미 있으면 번호가 붙은 새 폴더를 사용하며 기존 결과를 덮어쓰지 않습니다.
6. 생성된 `Prefabs/CB_XX.prefab`을 씬에 배치하여 확인합니다.

출력은 `Models`, `Textures`, `Materials/CB_XX`, `Prefabs`, `SourceCatalog.json`으로 구성됩니다. `SourceCatalog.json`은 출처 기록용 사본이며 원본 팩 기준 상대 경로를 유지합니다. 다시 임포트할 때는 원본 팩의 `Catalog/catalog.json`을 선택하세요.

도구가 수행하는 작업:

- FBX와 필요한 텍스처 복사, 모델별 URP Lit 재질 생성 및 이름으로 재연결
- 유리의 알파 투명도·렌더 큐·깊이 쓰기 설정, 외장 텍스처 연결
- 원래 문·간판·출입 표시 계층 보존 및 누락 검사
- UV2 생성 요청, 별도 프리뷰 씬에서 프리팹 저장

충돌체, LOD, NavMesh, 포털, 문 조작, 간판 교체 UI, 상점 로직은 추가하지 않습니다. 실제 배치 시 건물의 역할에 맞춰 기존 프로젝트 컴포넌트에 연결합니다. 오류가 나면 새 출력 폴더에 일부 결과가 남을 수 있으며 자동 삭제하지 않습니다.

## FBX 재불러오기 검증

다음 명령은 도구 사용 예시이며 모델 생성이 완료된 뒤 실행합니다.

```powershell
& "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" --background --factory-startup --python "Tools/validate_exports.py" -- --catalog "Catalog/catalog.json" --report "QA/export_validation.json"
```

팩 폴더를 작업 위치로 사용합니다. 기본 모드는 `CB_01`–`CB_08` 전체를 요구합니다. 하나씩 만드는 중에는 `--allow-partial`을 붙여 현재 카탈로그에 있는 모델만 검사할 수 있습니다. 이 경우 결과의 `packComplete`는 false이며 8개 완성 검증으로 취급하지 않습니다. 없는 FBX는 불러오기를 시도하지 않고 누락으로 보고합니다.

검증은 각 FBX를 새 Blender 데이터 상태에서 불러와 유한 좌표·변환, 단위 스케일, 실제 전체 경계, 삼각형 수, 첫 UV 채널, 유리 재질, 간판·문·출입 표시, 촬영용 오브젝트 제외, 재질 색상 및 외부 텍스처 참조를 확인합니다. 치수는 카탈로그 `exportBounds.min/max`의 Blender 좌표와 비교하며 고정된 건물 크기를 가정하지 않습니다. `exportBounds`가 없으면 크기 검사는 통과 처리하지 않습니다. Unity 전용 투명도는 카탈로그의 표면 종류와 알파 명세를 확인합니다. 발광 표현이나 Unity 렌더 결과가 Blender와 같다고 판단하지 않습니다.

2026-10-09 Blender **5.2.1 LTS**에서 `CB_01`–`CB_08` 전체 FBX를 각각 다시 불러와 검증했습니다. 카탈로그 3개 항목과 모델 1,178개 항목, **총 1,181개 항목이 모두 통과**했습니다. 최종 결과는 `QA/export_validation.json`에 있으며 `scope=complete`, `pass=true`, `packComplete=true`입니다. 이전 `QA/export_validation_partial.json`은 첫 모델만 검사한 중간 기록입니다.

## 확인 범위와 한계

- 임포트 도구는 소스와 로컬 URP 셰이더 속성에 대한 **정적 검사만 수행**했습니다. Unity Editor를 실행하지 않고 동봉된 C# 컴파일러와 Unity 참조 DLL로 소스 컴파일 검사를 통과했습니다. Unity Editor 안에서 메뉴 실행·에셋 임포트·플레이 테스트는 수행하지 않았습니다.
- Blender Cycles의 굴절 유리는 Unity URP Lit의 알파 투명도로 근사합니다. Blender 렌더와 같은 반사·굴절 결과를 보장하지 않습니다. 씬의 반사 프로브와 조명에 따라 조정해야 합니다.
- Blender에서 노드로 만든 미세 범프는 베이크된 노멀맵이 없으므로 자동 전달되지 않습니다. 색상 텍스처와 금속도·거칠기 수치는 연결됩니다.
- 발광색·세기가 카탈로그에 포함된 경우에만 발광 재질을 연결합니다. 재질 발광은 실시간 조명 오브젝트를 생성하지 않습니다.
- 유리는 건물별 렌더러에 함께 포함될 수 있으므로 여러 투명 창이 겹치는 시점에서 정렬을 확인하세요. 필요한 경우 유리 메시 분리나 셰이더 조정이 별도로 필요합니다.
- Unity에서는 모델 크기, 간판 이미지 방향, 문 피벗, 투명 창, 충돌체와 이동 경로를 확인한 뒤 게임에 배치하세요.
