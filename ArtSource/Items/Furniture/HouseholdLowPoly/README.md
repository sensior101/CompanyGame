# 가정용 로우폴리 가구 컬렉션

요청 목록 59개 모델(컴퓨터용 책상 3종 포함)에 싱크대 결합용 타공 하부장 1개를 추가한 60개 모델입니다.
현재 작업 범위는 **모델링 형태 제작**입니다. 기본 재질로 검토하며 여러 색상 적용은 모델링 확정 후 진행합니다.

## 열기

- `Catalog.html`: 로컬 브라우저에서 품목 검색, 공간 분류, 형태 미리보기 및 파일 열기.
- `Models/`: 품목별 편집 가능한 Blender 원본 60개.
- `Exports/<품목>/<품목>_Cream.fbx` 및 `.glb`: 기본 재질의 모델. 검증·전달 대상은 총 120개입니다.
- 작업 방향 변경 전에 만들어진 다른 색상 초안은 최종 모델링 패키지에 포함하지 않습니다.
- `QA/`: 실제 Blender 렌더, 품목별 치수/삼각형/재질 기록, 재수입 검사 결과.
- `RequestedCatalog.json`: 요청 품목 대응표. `Catalog.json`: 실제 제작 측정값.

## 규격

- 미터 단위, Blender Z-up, 전면 -Y. FBX는 Unity용 Y-up / -Z-forward 내보내기 설정.
- 플레이어 기준 높이 1.8m. 싱글침대 매트리스 1m × 2m, 일반 책상 상판 높이 0.74m, 주방 상판 높이 0.90m.
- 일반 모델의 원점은 바닥 높이 z=0이며 가구의 기준 중심입니다. 비대칭 가구는 바운딩 박스 중앙과 다를 수 있습니다.
- 싱크대만 원점이 상판 삽입 평면입니다. 싱크볼은 원점 아래로 내려갑니다.
- 프레임·패브릭·본체·금속·유리 등 재질 역할은 분리되어 있어, 형태 확정 후 색상 작업을 진행할 수 있습니다.
- 전자제품 화면은 별도 이름과 재질을 사용합니다. 컴퓨터 책상 3종에는 컴퓨터가 포함되지 않습니다.
- 피규어 진열장은 빈 내부 선반과 유리문을 갖춥니다. 피규어는 포함하지 않습니다.

## 주방 조립

1. `SinkBase`를 배치합니다. 상판은 높이 0.90m이고 0.58 × 0.41m의 관통 구멍이 실제로 뚫려 있습니다.
2. `Sink`의 원점을 하부장 로컬 위치 `(0, 0, 0.90)`에 놓습니다. 위 테두리는 상판 위에 걸치고, 싱크볼은 구멍 안으로 내려갑니다.
3. `Induction`은 일반 `KitchenBase`의 로컬 위치 `(0, 0, 0.90)`에 놓습니다.
4. 상부장과 후드는 벽 높이에 맞춰 별도 배치합니다. 0.80m 폭의 주방 모듈을 사용합니다.
5. `QA/KitchenAssembly.blend`는 최종 GLB로 조립한 검토용 예시입니다.

## 제작과 검증

`build_household.py`는 가구 공통 형상 도구와 품목별 레시피를 묶은 Blender 전용 아트 제작 스크립트입니다.
기존 SingleBed의 승인된 메시를 싱글/더블 침대에 재사용합니다. 기존 LibraryKiosk/가구 라이브러리의 미터 단위와 독립 에셋 내보내기 규칙을 따릅니다.

- 제작: Blender 5.2 백그라운드 + Cycles/FBX 애드온. 미리보기는 NVIDIA OptiX를 사용합니다.
- 모델링만 재생성: `-- --model-only --only Sink,KitchenBase`처럼 품목 ID를 전달합니다.
- 내보내기만: `-- --skip-render`. 이 옵션은 미리보기를 새로 만들지 않습니다.
- `verify_household.py`: 기본 재질의 모든 FBX/GLB를 새 장면에서 재수입해 원본 대비 치수, 원점, 삼각형, 재질 수와 UV를 검증합니다.
- `assembly_qa.py`: 최종 GLB에서 싱크볼 여유 공간, 상판 구멍 9개 지점 및 조립 렌더를 확인합니다.
- `make_catalog.py`: 형태 검토용 썸네일과 공간별 모음 이미지, 오프라인 카탈로그를 생성합니다.

## 코드 및 게임 영향

기존 파일 수정 없이 이 폴더에 제작 원본, 내보내기 파일, 검증 자료 및 제작용 스크립트를 추가했습니다.
새 스크립트의 책임은 Blender 아트 생성·검증·카탈로그 제작이며, 기존 게임 시스템에 포함시키는 런타임 기능이 아닙니다.
기존 `FurnitureFunction`과 `FurniturePlacement` 값은 모델 속성에 재사용합니다. 기존 클래스/공개 API/저장 데이터/이벤트/UI 호출부는 변경하지 않았습니다.
데이터 흐름은 Blender 형상 + 팔레트 → FBX/GLB → 독립 재수입 검사 → 렌더 카탈로그입니다. 경제·인벤토리·대화·거래 로그·저장 기능의 중복 구현은 없습니다.

Unity 임포트·URP 재질 연결·콜라이더·NavMesh·실제 배치/수면/수납/가전 동작 및 Play-mode는 검증 범위에 포함하지 않았습니다.
Blender 유리/거울과 화면은 Unity에서 적합한 URP 재질 연결이 필요합니다. 특히 FBX는 glTF의 유리 transmission 속성을 그대로 전달하지 않습니다.
씬 배치, 기존 가구 교체, 커밋은 수행하지 않았습니다.

## 형태 수정: 화장대 / 전신거울 / 빨래바구니

- 화장대: 둥근 상판과 일체감 있는 하부 몸체, 매립형 서랍 2개, 가늘어지는 다리, 타원형 거울로 수정.
- 전신거울: 하단을 기준으로 뒤로 10도 기울이고, 후면 지지대 연결 위치와 바닥 지지 폭을 조정.
- 빨래바구니: 위쪽이 넓어지는 타원형 몸통, 막힌 바닥, 두꺼운 입구 테두리, 통풍 구멍 40개와 양쪽 손잡이 구멍으로 수정.
- 수정한 제작 파일: `build_household.py`. 기존 `box`, `mesh`, `cyl`, `rod`, `apply`와 내보내기 파이프라인 재사용. 신규 제작 스크립트 없음.
- 수정 모델: `Models/Vanity.blend`, `Models/FullMirror.blend`, `Models/LaundryBasket.blend` 및 해당 기본 재질 FBX/GLB 6개. 검토 렌더·목록·패키지도 갱신.
- 기본 재질 유지. 데이터 흐름은 기존 Blender 제작 → FBX/GLB 내보내기 → 재수입 검사 그대로이며, 런타임 기능 중복·API/저장 데이터/호출부 변경 없음.
- 검증 기록: `QA/RoundTripReport_Partial.json`, `QA/ThreeCorrections_Scope.json`. Unity 적용 및 플레이 검증은 미수행.

## 형태 수정: 의자 / 소파 / 상단 틈 / 아일랜드

- 컴퓨터용 의자: 연속 곡면의 쿠션 좌판·등받이·팔걸이, 허리 받침 곡면, 뒤로 12도 기울어진 등받이로 수정.
- 바 의자: 좌석에서 등받이·측면으로 이어지는 버킷 형태와 좌판 쿠션 적용.
- 1인용 소파: 좌석 쿠션 폭 0.84m, 팔걸이 사이 공간 약 0.88m로 확대.
- 큰 옷장·벽 부착 수납장·주방 상부장·주방 하부장(싱크대 타공형 포함): 문 높이 조정과 뒤쪽 상단 마감으로 빈틈 차단.
- 아일랜드 식탁: 수납장 제거, 모서리와 단면을 다듬은 상판 및 곡선 받침 2개와 발 받침대로 변경. 가구 분류는 `Surface`로 수정.
- 수정 대상 9개: GamingChair, BarChair, Armchair, Wardrobe, WallCabinet, KitchenBase, SinkBase, KitchenUpper, IslandTable. 나머지 51종은 원본·기본 재질 출력물·미리보기 해시로 보호.
- 기존 제작 스크립트의 `mesh`, `cyl`, `rod`, `box`, `doors`와 내보내기를 재사용하고, 같은 파일에 쿠션과 곡면 단면 제작 함수를 추가. 신규 런타임 스크립트 없음.
- 기존 Blender → FBX/GLB 데이터 흐름 유지. 게임의 공통 기능 중복 구현 및 기존 공개 API/이벤트/저장/호출부 변경 없음.
- 검증: `QA/FiveItems_RoundTripReport.json`, `QA/FiveItemsGeometry.json`, `QA/FiveItems_Scope.json`. Unity 임포트 및 Play-mode 검증은 미수행.

## 1인용 소파 몸체 재설계

- Armchair만 수정. 분리된 팔걸이·등받이 쿠션과 사각 하부 프레임을 제거하고 하부·팔걸이·등받이가 이어지는 하나의 연속된 패브릭 몸체로 재설계.
- 좌석 쿠션 폭 0.84m 유지. 몸체 내부 받침면에 쿠션 하부를 맞추고 짧은 다리는 몸체 안쪽으로 배치.
- 수정 파일: 기존 `build_household.py`의 Armchair 레시피, `Models/Armchair.blend`, 기본 재질 FBX/GLB 및 관련 검토 자료. 기존 `mesh`, `cushion`, `cyl`, `apply`와 내보내기 재사용. 신규 제작/런타임 스크립트 없음.
- 데이터 흐름·게임 API·저장/이벤트/호출부 변경 없음. 중복 게임 기능 구현 없음. 나머지 59개 모델은 파일 해시로 보존 확인.
- 검증 자료: `QA/ArmchairShell_RoundTripReport.json`, `QA/ArmchairShell_Geometry.json`, `QA/ArmchairShell_Scope.json`. Unity Play-mode는 미수행.

## 컴퓨터용 의자 형태 재설계

- GamingChair만 수정. 어깨가 넓고 허리 부분이 좁아지는 높은 버킷형 등받이, 일체형 헤드레스트, 실제 관통된 스트랩 구멍 2개로 컴퓨터용 게이밍 의자 실루엣을 구성.
- 등받이 뒤로 12도 기울기 유지. 좌판 측면 지지부, 얇은 팔걸이 패드와 높이 조절 기둥, 리클라이닝 힌지·레버, 쌍바퀴 5조를 모델링. 조절부는 정적 형상이며 작동 기능은 구현하지 않음.
- 기존 `build_household.py`의 `swivel` 레시피 수정. `mesh`, `cushion`, `rounded_loft`, `box`, `cyl`, `rod`, `apply`와 내보내기 파이프라인 재사용. 등받이 한 메시의 여러 재질 역할을 보존하도록 기존 재질 지정·목록 생성 확장.
- 수정 원본: `Models/GamingChair.blend`, 기본 재질 FBX/GLB 2개 및 관련 카탈로그·검토 이미지. 신규 제작/런타임 스크립트 없음. 신규 파일은 옆·뒤 검토 이미지, 형상·재수입·범위 검증 기록과 `GamingChairRevision.zip` 전달 묶음.
- 기본 재질만 사용. Blender 제작 → FBX/GLB → 재수입 검증 흐름 유지. 게임 코드·API·저장 데이터·이벤트·호출부 변경 및 공통 기능 중복 구현 없음.
- 검증 자료: `QA/GamingChair_RoundTripReport.json`, `QA/GamingChair_Geometry.json`, `QA/GamingChair_Scope.json`. 등받이 단일 연결체·닫힌 메시·관통 구멍 2개 확인. 나머지 59종 원본/기본 출력물/미리보기는 해시로 보존 확인. Unity 임포트·Play-mode는 미수행.

## 네 가지 재질 버전

현재 완성된 60종을 `MaterialVariants/` 아래 네 스타일로 제공합니다. 이전 기본 재질 원본과 출력물은 보존하며, 현재 재질 카탈로그는 `MaterialVariants/Catalog.html`입니다.

- WhiteWood: 화이트와 내추럴 오크, 아이보리·베이지 패브릭.
- Eclectic: 틸·코랄·머스터드·딥퍼플 배색.
- MarbleGold: 대리석 표면, 금속 골드, 아이보리·딥그린 패브릭.
- ChicBlack: 블랙 오크, 차콜 패브릭, 건메탈.
- 기존 `build_household.py`에 `--material-variants`를 추가. 품목별 모델링 레시피를 다시 실행하지 않고 완성된 `.blend`를 읽어 기존 재질 슬롯만 교체. 기존 `rgba`, 내보내기 설정과 `export_asset`을 재사용하며 공통 내보내기를 한 함수로 정리.
- 기존 `verify_household.py`, `make_catalog.py`에 동일 옵션을 추가. 신규 제작/런타임 스크립트 없음. 신규 산출물은 240개 Blender 파일, 480개 FBX/GLB, 미리보기, 공용 텍스처, 검증 기록, 재질 카탈로그 및 전달 ZIP.
- 데이터 흐름: 완성된 Blender 모델 → 재질 슬롯 교체 → 텍스처 포함 내보내기 → 독립 재수입 검사. 메시·UV·치수·원점·조립 부위·게임 데이터 흐름과 API/이벤트/저장/호출부는 변경하지 않음. 중복 게임 기능 구현 없음.
- 검증: `MaterialVariants/SourcePreservation.json`과 `MaterialVariants/RoundTripReport.json`. 저장된 Blender 메시·변환·UV·스무딩·면별 재질 슬롯 번호를 원본과 비교하고, FBX/GLB 정점 위치·삼각형 연결·UV·재질 수·텍스처 로드를 검사. Unity 임포트·URP 연결·Play-mode는 미수행.
