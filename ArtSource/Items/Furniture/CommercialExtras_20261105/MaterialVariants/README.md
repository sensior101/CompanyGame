# 상가 추가 가구 19종 — 4개 테마

19종 × 4테마 = 개별 Blender 76개 + 테마별 상가 배치본 4개. 원본의 형상·UV·크기·원점은 유지하고 재질만 변경했다.

| 테마 | 상가 배치본 | 개별 모델 폴더 |
|---|---|---|
| 화이트앤우드 | [Blender](WhiteWood/CommercialExtras_WhiteWood.blend) | [WhiteWood/Models](WhiteWood/Models/) |
| 화이트앤블랙 | [Blender](WhiteBlack/CommercialExtras_WhiteBlack.blend) | [WhiteBlack/Models](WhiteBlack/Models/) |
| 광택 블랙 금속 | [Blender](GlossBlack/CommercialExtras_GlossBlack.blend) | [GlossBlack/Models](GlossBlack/Models/) |
| 대리석앤골드 | [Blender](MarbleGold/CommercialExtras_MarbleGold.blend) | [MarbleGold/Models](MarbleGold/Models/) |

## 비교 미리보기

- [우산꽂이, 벽걸이 메뉴판, 입간판, 정수기, 페달 휴지통](Previews/ThemeComparison_01.jpg)
- [원형 단체 테이블, 좌식 테이블, 천장형 테이블 번호판, 업소용 싱크대, 업소용 냉장고](Previews/ThemeComparison_02.jpg)
- [업소용 상부개방 냉동고, 서빙 카트, 빨간 플라스틱 의자, 테라스 테이블, 테라스 파라솔](Previews/ThemeComparison_03.jpg)
- [업소용 오븐, 탁상 전화기, 빈백, 미용실 의자](Previews/ThemeComparison_04.jpg)

## 재질 규칙

- 기존 상가 가구의 surface/material/style_material/make_style_textures를 재사용. 목재와 대리석 텍스처는 Blender 파일 안에 포함했다.
- 화이트앤우드는 밝은 본체와 오크, 화이트앤블랙은 밝은 본체와 검은 프레임, 광택 블랙은 금속성 0.94의 유광 표면, 대리석앤골드는 대리석 판재와 금색 프레임이다.
- 빈백·미용실 쿠션·파라솔 천은 비금속 재질이다. 블랙 좌석은 가죽 느낌으로 처리했다. 유리·화면·고무·표시등은 기능상 식별을 유지한다.
- 빨간 플라스틱 의자는 화이트앤우드에서 빨강을 유지하고 다른 세 테마에서는 테마 색으로 바뀐다.
- 메뉴판·입간판의 ImageUV와 순백색 교체 면을 유지했다. 천장 번호판은 01 기본 표시다.

## 완료 보고

1. 기존 파일 수정 없음. 기본 19종·배치본 및 앞서 제작한 45종의 테마 파일을 해시로 비교해 보존했다.
2. build_styles.py는 추가 품목의 재질 예외와 기존 테마 도구 연결을, package_styles.py는 비교 이미지 라벨과 문서 생성을 담당한다. 새 산출물은 MaterialVariants 아래에 저장한다.
3. CommercialLowPoly/build_styles.py의 apply_style/material/save/configure_render/reflection_lights, HouseholdLowPoly의 style_surface/style_material/make_style_textures/material_geometry_signature 재사용.
4. 기본 모델 읽기 → 기존 테마와 추가 품목별 재질 규칙 적용 → 텍스처 포함 저장 → 4개 상가 배치본·비교 렌더 → 재개방 검증.
5. 공통 메시·재질 생성 기능의 중복 구현 없음. 게임 공통 기능 추가 없음.
6. Unity API·저장 데이터·이벤트·호출부 변경 없음. 게임 반입과 기능 연결은 수행하지 않았다.
7. 개별 76개와 배치본 4개 검증. 형상·UV·변환 유지, 텍스처 포함, 흰 이미지 면, 블랙 금속 및 천의 비금속 속성, 상가 범위 확인. 원본 204개 해시 일치. Unity 외형·플레이는 미검증.

## 파일 규칙

개별 파일은 `테마/Models/품목ID_테마.blend` 형식이다. 카메라·조명·스튜디오 바닥과 분리된 `품목ID_ASSET` 컬렉션이 실제 모델이다.
