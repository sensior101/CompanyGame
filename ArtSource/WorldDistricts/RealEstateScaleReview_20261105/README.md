# 부동산 단층 설계 수정 — 2026-11-05

이전 균일 확대안은 문 높이 8.65m, 건물 높이 15.33m였다. 부지 크기에 맞추면서 사람 크기와 층수를 확인하지 못한 결과였다. 이번에는 전체 높이를 압축하지 않고 실제 1.8m 캐릭터와 문 1.8×2.5m를 기준으로 공간을 다시 구성했다.

- 연결된 L자 평면 27×20m, 중앙 최고점 5m / 측면 최고점 4.4m.
- 48×48m 부지에 전면 8m, 좌우 각각 10.5m, 후면 20m 여유.
- 중앙 출입구와 매물 카드, 녹색 연속 간판 유지. 중앙 간판 상단에 집 지붕 윤곽 추가.
- 비교: 기존 도서관 높이 11.565m, 경찰서 11.628m. 부동산은 이보다 낮은 단층 건물.

## 1. 수정한 기존 파일

- `CompanyGame/Assets/Art/WorldDistricts/RealEstateOffice/Models/RE_01.fbx`
- 같은 폴더 상위 `SourceCatalog.json`
- `CompanyGame/Assets/Scenes/Maps/CivicDistrict.unity`
- `CompanyGame/Assets/Scenes/Maps/CivicDistrict/NavMesh-CitizenNavigation.asset`
- `ArtSource/WorldDistricts/RealEstateOffice_20261104/CURRENT.md`

## 2. 신규 파일과 이유

`RealEstateOffice_20261104/SingleStoreyRevision_20261105/`에 Blender 원본, FBX/GLB, 카탈로그, 생성 스크립트, 5개 시점 미리보기와 모델 검증 기록을 보관한다. 이전 디자인을 덮어쓰지 않고 실패한 버전도 구분해서 남긴다.

이 보고서 폴더의 `ApplySingleStorey.cs`, `VerifyAppliedOffice.cs`, `Review/verify_single_storey.py`는 적용·검증용 일회성 도구다. Assets 밖에 있으며 게임 런타임에 포함되지 않는다. 기존 검사 및 에디터 생성 함수를 호출한다. `Before/`에는 이번 변경 전 파일을 보관한다.

## 3. 재사용한 기존 클래스·메서드

`MapParcel`, `WorldMapTools.FitBuildingWithinTier`, `MapColliderRebuilder.Scan/Create`, `CitizenNavBuilder.Bake/WriteStarts`, 기존 RE_01 프리팹 및 재질 연결, 기존 Blender export/검사 헬퍼를 사용했다.

## 4. 변경된 데이터 흐름

Blender 원본 → 기존 내보내기 → 기존 FBX 경로에 재수입 → 기존 프리팹과 CivicDistrict 인스턴스 갱신 → 기존 충돌 생성기 및 시민 NavMesh 재빌드. 부지 ID와 주소·크기는 유지한다.

## 5. 중복 구현 여부

신규 공통 관리자나 게임플레이 시스템은 만들지 않았다. 경제·인벤토리·저장·NPC 대화 흐름은 변경하지 않았다.

## 6. 기존 기능 및 호출부 영향

모델·프리팹 GUID, 출입구 앵커명, 기존 10개 재질 연결을 유지한다. 부동산 배치와 해당 건물 충돌 형상만 변경한다. 이전 과대 모델 때문에 가려졌던 기존 나무·가로등의 메시 5개와 충돌 5개를 복구했다. 새 장식을 건물 모델에 넣은 것은 아니다. 다른 건물의 배치·크기와 상가 24종은 이번 단층 수정에서 변경하지 않았다.

## 7. 검증과 한계

검증 근거: `Review/ModelValidation.json`, `Review/UnityValidation.json`, `Review/UnityApplication.json`. Blender·FBX·GLB의 크기, 문, 재질, 연결부, 지붕 높이와 열린 마당을 검사한다. Unity에서는 치수, 기존 부지·다른 건물 보존, 충돌 메시, 전후좌우 접근 경로 및 임시 비교 캐릭터 제거를 검사한다.

최종 집 모양 간판 포함 결과: 3개 형식 99개 검사 통과, Unity 23개 검사 통과, 보행 경로 8개 모두 완전 연결. 충돌 메시 109개는 현재 모델을 참조한다. 간판 변경 전후 물리 형상 지문이 동일하여 앞서 생성한 NavMesh 3,817개 삼각형과 시민 시작점 301개를 유지했다. 원본 FBX와 Unity 적용 파일의 SHA256 일치. 임시 비교 캐릭터 및 베이크 메시가 저장 씬에 없는 것을 확인했다.

`git diff --check`는 Unity가 직렬화한 빈 `m_Name:` / `value:` 항목의 후행 공백을 보고했다. 에디터가 생성한 씬을 텍스트로 재작성하지 않았으며 해당 검사 자체는 통과로 보고하지 않는다. 커밋·푸시는 하지 않았다.

실제 게임 캐릭터를 사용한 에디터 스크린샷은 `Review/Unity_Door_Character.png`, 전체 모습은 `Review/Unity_Office.png`이다. 비교용 캐릭터는 저장되는 씬에서 제거한다. Play 모드에서 캐릭터를 직접 조작한 통행 테스트는 수행하지 않았다. 새 출입문 열기 기능을 구현한 작업은 아니다.
