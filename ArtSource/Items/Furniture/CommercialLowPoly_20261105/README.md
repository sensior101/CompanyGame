# 상가용 로우폴리 가구 45종

화이트앤우드 기본 팔레트. 밝은 오크, 따뜻한 흰색 본체와 패브릭, 무광 표면을 사용한다. 식물 잎·흙, 블랙보드, 시계 눈금, 바퀴는 고유 색을 사용한다.

- 기준 실내는 ShopInterior_Empty.blend의 26 × 16.4m, 천장 높이 3.3m이다.
- Models에는 품목별 Blender 파일이 있다. 단위는 미터, 위쪽은 Z, 정면은 -Y이다.
- CommercialFurniture_RoomScale.blend는 실제 상가에 45종을 나열한 크기 확인용 배치본이다.
- Previews에는 분류별 렌더와 상가 배치 렌더가 있다.
- 벽걸이 모델은 뒷면 Y=0, 아래쪽 Z=0을 기준으로 배치한다.
- ImageSurface는 별도 흰색 재질과 0~1 UV를 갖는다. 포스터는 벽 부착용 단면 평면이다.
- 칸막이 책상에는 책상과 칸막이 구성만 포함한다.

제작 및 검증 기록

1. 수정한 기존 파일은 없다.
2. 새 파일은 이 폴더의 모델, 제작·검증 스크립트, 렌더 및 QA 자료다. 제작 스크립트는 상가 전용 형상 조합과 재현을 담당한다.
3. HouseholdLowPoly/build_household.py의 box, cyl, rod, lathe, cushion, desk, carcass, prepare와 기존 의자·소파·신발장 제작 함수를 재사용한다.
4. 기존 형상·상가 치수 → 상가 전용 조합 및 재질 → 개별 Blender 원본 → 파일 재개방 검사 → 크기 배치 렌더 흐름이다.
5. 공통 형상 생성은 기존 함수를 재사용한다. 런타임 공통 기능 추가는 없다.
6. 게임 공개 API, 저장 데이터, 호출부 변경은 없다.
7. 검증은 파일 재개방, 치수, 삼각형 수, 열린 모서리, 퇴화 면, UV·재질, 흰 이미지 면, 책상 구성 및 Blender 렌더다. Unity 반입·플레이 검증은 수행하지 않았다.

| 품목 | 파일 | 너비 × 깊이 × 높이(m) | 삼각형 |
|---|---|---|---|
| 바 스툴 | [원본](Models/BarStool.blend) | 0.51 × 0.48 × 1.003 | 1192 |
| 사무용 회전의자 | [원본](Models/OfficeChair.blend) | 0.683 × 0.666 × 0.962 | 640 |
| 손님용 의자 | [원본](Models/GuestChair.blend) | 0.5 × 0.518 × 0.876 | 320 |
| 대기벤치 | [원본](Models/WaitingBench.blend) | 1.62 × 0.545 × 0.855 | 540 |
| 1인 암체어 | [원본](Models/Armchair.blend) | 1.168 × 0.913 × 0.92 | 1320 |
| ㄱ자 소파 | [원본](Models/CornerSofa.blend) | 2.1 × 1.693 × 0.884 | 792 |
| 1인 사각테이블 | [원본](Models/SquareTable1.blend) | 0.6 × 0.6 × 0.74 | 264 |
| 2인 사각테이블 | [원본](Models/SquareTable2.blend) | 0.8 × 0.8 × 0.74 | 264 |
| 긴 공용 테이블 | [원본](Models/CommunalTable.blend) | 2.4 × 0.9 × 0.74 | 264 |
| 일반 책상 | [원본](Models/PlainDesk.blend) | 1.3 × 0.65 × 0.74 | 264 |
| 칸막이 오피스 책상 | [원본](Models/CubicleDesk.blend) | 1.512 × 0.751 × 1.358 | 528 |
| ㄱ자 책상 | [원본](Models/CornerDesk.blend) | 1.6 × 1.55 × 0.74 | 396 |
| 회의테이블 | [원본](Models/MeetingTable.blend) | 2.4 × 1.1 × 0.74 | 264 |
| 벽걸이 화이트보드 | [원본](Models/Whiteboard.blend) | 1.5 × 0.143 × 0.9 | 308 |
| 벽걸이 블랙보드 | [원본](Models/Blackboard.blend) | 1.5 × 0.143 × 0.9 | 308 |
| 카운터 우드 슬랫 | [원본](Models/Counter01.blend) | 1.88 × 0.739 × 1.02 | 1144 |
| 카운터 화이트 패널 | [원본](Models/Counter02.blend) | 1.88 × 0.738 × 1.02 | 528 |
| 카운터 높은 안내 | [원본](Models/Counter03.blend) | 1.88 × 0.735 × 1.208 | 484 |
| 카운터 ㄱ자 | [원본](Models/Counter04.blend) | 1.88 × 1.61 × 1.02 | 484 |
| 카운터 우드 프레임 | [원본](Models/Counter05.blend) | 2.28 × 0.753 × 1.02 | 572 |
| 카운터 낮은 화이트 | [원본](Models/Counter06.blend) | 1.88 × 0.745 × 0.9 | 528 |
| 유리진열카운터 | [원본](Models/GlassCounter.blend) | 1.5 × 0.65 × 1.051 | 460 |
| 벽을 바라보는 바 테이블 | [원본](Models/WallBarTable.blend) | 2.2 × 0.42 × 1.05 | 264 |
| 낮은 파티션 | [원본](Models/LowPartition.blend) | 1.285 × 0.42 × 1.1 | 264 |
| 높은 파티션 | [원본](Models/HighPartition.blend) | 1.285 × 0.42 × 1.8 | 264 |
| 신발장 | [원본](Models/ShoeCabinet.blend) | 0.8 × 0.432 × 1.12 | 608 |
| 형광등 | [원본](Models/Fluorescent.blend) | 1.22 × 0.25 × 0.096 | 308 |
| 스탠드조명 | [원본](Models/FloorLamp.blend) | 0.494 × 0.494 × 1.55 | 228 |
| 책상램프 | [원본](Models/DeskLamp.blend) | 0.299 × 0.299 × 0.47 | 228 |
| 벽시계 | [원본](Models/WallClock.blend) | 0.4 × 0.069 × 0.4 | 432 |
| 흰 이미지 면 액자 | [원본](Models/PictureFrame.blend) | 0.64 × 0.07 × 0.84 | 222 |
| 흰 이미지 면 포스터 | [원본](Models/Poster.blend) | 0.52 × 0 × 0.82 | 2 |
| 탁상 잎화분 | [원본](Models/Plant01.blend) | 0.854 × 0.863 × 1.093 | 572 |
| 세로 잎화분 | [원본](Models/Plant02.blend) | 0.777 × 0.738 × 1.567 | 588 |
| 선인장 화분 | [원본](Models/Plant03.blend) | 0.4 × 0.38 × 0.68 | 320 |
| 중형 관엽 화분 | [원본](Models/Plant04.blend) | 0.901 × 0.863 × 1.704 | 700 |
| 대형 관엽 화분 | [원본](Models/Plant05.blend) | 0.901 × 0.931 × 1.999 | 940 |
| 낮은 잎화분 | [원본](Models/Plant06.blend) | 0.702 × 0.691 × 0.828 | 572 |
| 테두리 사각 러그 | [원본](Models/Rug01.blend) | 1.8 × 1.2 × 0.015 | 92 |
| 줄무늬 러너 | [원본](Models/Rug02.blend) | 0.7 × 2.2 × 0.014 | 152 |
| 원형 러그 | [원본](Models/Rug03.blend) | 1.7 × 1.7 × 0.027 | 476 |
| 타원형 러그 | [원본](Models/Rug04.blend) | 1.8 × 1.15 × 0.015 | 76 |
| 체크 러그 | [원본](Models/Rug05.blend) | 1.8 × 1.4 × 0.014 | 188 |
| 현관 러그 | [원본](Models/Rug06.blend) | 1.1 × 0.7 × 0.014 | 152 |
| 방석 | [원본](Models/SeatCushion.blend) | 0.43 × 0.43 × 0.07 | 528 |
