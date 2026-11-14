# 상가 추가 가구 19종 — 화이트앤우드

기존 가구와 같은 미터 단위, Z 위쪽, -Y 정면. 상가 내부 26 × 16.4m / 천장 3.3m 기준이다. 빨간 플라스틱 의자는 요청한 빨간색이며, 업소용 기기는 위생 작업면과 손잡이에 금속을 사용한다.

- `Models/`: 19개 개별 Blender 원본. 각 파일의 `ID_ASSET` 컬렉션에 모델만 들어 있다. 카메라·조명·바닥은 별도 프레젠테이션 요소다.
- `CommercialExtras_WhiteWood_RoomScale.blend`: 창문 없는 기존 상가에서 크기를 확인하는 배치본. 전화기는 기존 74cm 책상 위에 놓았다. 야외 가구도 크기 비교를 위해 이 배치본에 포함했다.
- `Previews/`: 품목별 렌더, 4장 미리보기, 상가 배치 렌더. 품목별 미리보기는 화면에 맞춘 배율이며, 실제 상대 크기는 상가 배치본에서 확인한다.
- 바닥·탁상 모델은 아래쪽 Z=0, 벽 메뉴판은 뒤쪽 Y=0 / 아래쪽 Z=0, 천장 번호판은 천장 접점 Z=0에서 아래로 내려온다.
- 메뉴판·입간판의 흰 면은 별도 UV와 재질을 갖춘 이미지 교체용 면이다. 번호판은 01 기본 표시. 기능 연결은 포함하지 않는다.

## 제작 및 검증

1. 기존 파일 수정 없음. 기존 제작 라이브러리·상가·가구 파일의 SHA-256을 비교해 보존 확인.
2. 이 폴더에 모델, 미리보기, QA, recipes.py, build_extras.py, package_review.py를 새로 생성했다. recipes는 추가 품목 형상, build는 기존 제작·카메라 도구 연결 및 검증, package는 검토용 이미지와 문서를 담당한다.
3. HouseholdLowPoly의 box/cyl/rod/lathe/rounded_loft/cushion/basin/faucet/handle/init/prepare, CommercialLowPoly의 bounds/camera_scene 재사용.
4. 기존 치수·형상 도구 → 추가 품목 조합·재질 → 개별 Blender 저장 → 파일 재개방 검사 → 기존 상가 크기 배치와 렌더.
5. 공통 형상 생성·게임 공통 로직의 중복 구현 없음.
6. Unity 스크립트·공개 API·저장 데이터·이벤트·호출부 변경 없음. Blender 제작 산출물만 추가.
7. 19개 파일 재개방, 치수, 원점, 삼각형 수, UV·재질, 비정상 모서리·퇴화 면, 상가 범위 검사를 통과했다. Unity 반입·상호작용·플레이 검증은 수행하지 않았다.

## 품목

| 품목 | 원본 | 너비 × 깊이 × 높이(cm) | 삼각형 | 설치 |
|---|---|---|---:|---|
| 우산꽂이 | [Blender](Models/UmbrellaStand.blend) | 32.0 × 26.0 × 51.0 | 1028 | Floor |
| 벽걸이 메뉴판 | [Blender](Models/WallMenuBoard.blend) | 75.0 × 7.7 × 93.0 | 310 | Wall |
| 입간판 | [Blender](Models/AFrameSign.blend) | 63.2 × 51.7 × 101.4 | 386 | Floor |
| 정수기 | [Blender](Models/WaterDispenser.blend) | 34.0 × 36.0 × 151.0 | 1236 | Floor |
| 페달 휴지통 | [Blender](Models/PedalTrashBin.blend) | 35.8 × 42.0 × 50.4 | 608 | Floor |
| 원형 단체 테이블 | [Blender](Models/RoundGroupTable.blend) | 140.0 × 140.0 × 74.0 | 516 | Floor |
| 좌식 테이블 | [Blender](Models/LowFloorTable.blend) | 110.0 × 70.0 × 32.0 | 296 | Floor |
| 천장형 테이블 번호판 | [Blender](Models/HangingTableNumber.blend) | 32.0 × 5.6 × 62.6 | 568 | Ceiling |
| 업소용 싱크대 | [Blender](Models/CommercialSink.blend) | 120.0 × 68.0 × 115.2 | 940 | Floor |
| 업소용 냉장고 | [Blender](Models/CommercialFridge.blend) | 85.0 × 86.0 × 190.0 | 1048 | Floor |
| 업소용 상부개방 냉동고 | [Blender](Models/CommercialFreezer.blend) | 123.0 × 79.2 × 89.0 | 824 | Floor |
| 서빙 카트 | [Blender](Models/ServingCart.blend) | 69.5 × 51.0 × 94.9 | 1352 | Floor |
| 빨간 플라스틱 의자 | [Blender](Models/RedPlasticChair.blend) | 55.2 × 52.8 × 87.0 | 888 | Floor |
| 테라스 테이블 | [Blender](Models/TerraceTable.blend) | 78.2 × 80.0 × 73.7 | 712 | Floor |
| 테라스 파라솔 | [Blender](Models/PatioParasol.blend) | 240.0 × 240.0 × 263.0 | 700 | Floor |
| 업소용 오븐 | [Blender](Models/CommercialOven.blend) | 85.0 × 89.2 × 99.5 | 1232 | Floor |
| 탁상 전화기 | [Blender](Models/DeskTelephone.blend) | 23.3 × 19.5 × 12.0 | 3356 | Surface |
| 빈백 | [Blender](Models/Beanbag.blend) | 92.0 × 82.8 × 67.8 | 732 | Floor |
| 미용실 의자 | [Blender](Models/SalonChair.blend) | 66.5 × 88.5 × 93.3 | 1916 | Floor |
