# 원래 디자인 비율을 유지한 부지 크기 맞춤

2026-11-05 최종 기준: **가로·깊이·높이에 같은 배율을 적용하고, 긴 수평변만 해당 부지의 기존 건축 규격에 맞춘다.**

| 대상 | 실제 부지 | 적용 기준 | 결과 |
|---|---|---|---|
| 부동산 RE_01 | 48×48m | 최초 디자인 전체 ×3.460837887 | 38×25.668934×15.331512m, Unity 교체 완료 |
| 상가 24종 | 16×16m | 각 원본의 긴 수평변을 12m로 맞추는 동일 XYZ 배율 | 24종 모두 기존 결과가 일치해 유지 |

치수 순서는 폭×깊이×높이. 부동산 문·간판·매물 카드도 건물과 같은 배율로 확대했다. 38×38m로 평면을 다시 설계한 거부된 버전은 사용하지 않았다.

## 현재 사용할 파일

- 부동산 Blender: `../RealEstateOffice_20261104/ProportionalFit_20261105/Models/RE_01.blend`
- 부동산 FBX/GLB/카탈로그: 위 폴더의 `Exports/`, `Catalog/catalog.json`
- Unity 부동산: `CompanyGame/Assets/Art/WorldDistricts/RealEstateOffice/Prefabs/RE_01.prefab`
- 상가 24종: `../CommercialBlocks/ParcelFit24_20261104/`의 Models/Exports/Catalog. 3·4·5층 각각 8종.
- 실제 Unity 모습: `Review/Unity_Office.png`, 주변 건물과 비교: `Review/Unity_District.png`.
- 상가 24종 실제 치수: `Review/CommercialDimensions.md`.

## 작업 내역

1. **수정한 기존 파일**: Unity의 `RealEstateOffice/Models/RE_01.fbx`, `SourceCatalog.json`, `CivicDistrict.unity`, `CivicDistrict/NavMesh-CitizenNavigation.asset`. FBX와 프리팹 GUID는 그대로 유지했다. 상가 모델 파일은 재검사 결과 불일치가 없어 수정하지 않았다.
2. **신규 생성 파일 및 이유**: 부동산 `ProportionalFit_20261105`는 최초 원본을 보존한 비례 확대 모델·내보내기·미리보기·메타데이터다. Unity `SceneMeshes/RE_TreeTrim_*`, `RE_LampTrim_*` 5개는 확대 영역에 겹친 나무 한 그루와 가로등 한 개만 정리한 공용 메시 복사본이다. 이 폴더의 ReplaceOffice.cs와 Review 검증 스크립트는 일회성 에디터 작업/검증 코드이며 런타임 컴포넌트가 아니다.
3. **재사용**: 기존 `fit_parcels.py`의 snapshot/verify·균일 변환, 상가 내보내기/렌더/검증 함수, `WorldMapTools.FitBuildingWithinTier`, `MapParcel`, `MapColliderRebuilder.Scan/Create`, `CityHallRecovery.TrimParcelDetails`, `CitizenNavBuilder.Bake/WriteStarts`. 앞선 정정 과정에서 추가한 메시 정리 함수의 범위 인자를 재사용했다.
4. **데이터 흐름**: 최초 RE_01 → 전체 동일 XYZ 배율과 바닥 중심 피벗 → Blender/FBX/GLB → 기존 Unity FBX 경로에 재가져오기 → 기존 프리팹/씬에 반영 → 구조 충돌체와 내비게이션 재생성. 부지 ID·주소·크기 및 다른 건물의 변환은 유지된다.
5. **중복 구현**: 새 경제·인벤토리·저장·상호작용 관리자 없음. 기존 에디터 도구를 연결해 사용했다.
6. **기존 기능 및 호출부 영향**: `공공 지구 401번지`, `CivicDistrict:CivicDistrict_Community`, 기존 모델/프리팹 GUID 유지. 다른 건물의 위치·회전·크기 동일. 확대된 문틀·기둥까지 실제 형상대로 막도록 구조 MeshCollider 40개를 생성했다. 부지 안에서 건물과 겹친 기존 수목·가로등의 332삼각형만 제거했고, 그 밖의 공용 메시 삼각형과 정점은 동일하다. 부동산 매매·실내 이동 기능은 추가하지 않았다.
7. **검증과 한계**: 부동산 3형식 재불러오기 90개 검사 통과. Blender 원본의 모든 정점·토폴로지·UV·재질·앵커가 하나의 균일 변환과 일치한다. 상가 건물 72형식+독립 로고 3형식 총 75형식/2,442검사 재통과. Unity 검증 34/34 통과, 주변 8방향 NavMesh 경로 정상. 최종 NavMesh 3,812삼각형, 시민 시작점 299개. 실제 씬 캡처를 시각 확인했고 저장을 확인했다. Play Mode 직접 조작과 멀티플레이 검증은 실행하지 않았다.

Unity FBX SHA-256은 새 비례 확대 원본 FBX와 일치한다. 기존 모델·규격·Nightlife 씬 보존 해시와 별도 백업을 유지했으며 커밋/푸시는 하지 않았다.
