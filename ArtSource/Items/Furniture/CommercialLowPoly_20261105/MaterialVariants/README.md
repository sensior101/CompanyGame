# 상가 가구 4종 재질 스타일

45종 × 4스타일, 개별 Blender 파일 180개와 스타일별 전체 배치본 4개.

| 스타일 | 표면 구성 | 전체 배치본 |
|---|---|---|
| 화이트앤우드 | 따뜻한 화이트, 오크 결, 아이보리 패브릭 | [Blender](WhiteWood/CommercialFurniture_WhiteWood.blend) |
| 화이트앤블랙 | 흰 본체·상판, 검은 프레임과 포인트 | [Blender](WhiteBlack/CommercialFurniture_WhiteBlack.blend) |
| 유광 블랙 메탈 | 검은 금속 외장, 반사 코팅, 검은 가죽 쿠션 | [Blender](GlossBlack/CommercialFurniture_GlossBlack.blend) |
| 대리석앤골드 | 대리석 상판·외장, 골드 프레임·손잡이, 아이보리 패브릭 | [Blender](MarbleGold/CommercialFurniture_MarbleGold.blend) |

액자·포스터의 이미지 면은 흰색이다. 잎·흙, 유리, 보드 필기면, 조명 확산판 등은 용도에 맞는 재질을 적용했다. 러그와 방석은 스타일별 패브릭으로 구성했다. 텍스처는 각 Blender 파일 안에 패킹했다.

[좌석 비교](Previews/Compare_Seating.png) · [책상·카운터 비교](Previews/Compare_Work.png) · [소품 비교](Previews/Compare_Decor.png)

제작 및 검증

1. 기존 파일 수정은 없다.
2. 새 제작 파일은 상위 폴더 build_styles.py이다. 상가 모델의 부위별 재질 배정과 4스타일 제작을 담당한다. 이 폴더에는 스타일별 모델·배치본, 텍스처, 렌더와 QA 자료를 추가했다.
3. HouseholdLowPoly의 make_style_textures, style_material, style_surface, material_geometry_signature와 build_commercial의 camera_scene을 재사용했다.
4. 기본 Blender 모델 → 부위 역할에 따른 재질 배정 → 텍스처 패킹 → 스타일별 저장 → 파일 재개방 및 형상 비교 흐름이다.
5. 공통 재질 생성 함수 재사용. 게임 런타임 공통 기능 추가는 없다.
6. 원본 모델, 게임 코드, 저장 데이터, 호출부 변경은 없다.
7. 180개 저장 파일의 형상·UV·변환 서명, 텍스처 패킹, 흰 이미지 면, 블랙 금속 셰이더 검사를 통과했다. 원본 46개 파일의 해시도 동일하다. 결과는 QA/validation.json에 기록했다. Unity 반입 및 게임 내 검증은 수행하지 않았다.

| 품목 | 화이트앤우드 | 화이트앤블랙 | 유광 블랙 | 대리석앤골드 |
|---|---|---|---|---|
| 바 스툴 | [원본](WhiteWood/Models/BarStool_WhiteWood.blend) | [원본](WhiteBlack/Models/BarStool_WhiteBlack.blend) | [원본](GlossBlack/Models/BarStool_GlossBlack.blend) | [원본](MarbleGold/Models/BarStool_MarbleGold.blend) |
| 사무용 회전의자 | [원본](WhiteWood/Models/OfficeChair_WhiteWood.blend) | [원본](WhiteBlack/Models/OfficeChair_WhiteBlack.blend) | [원본](GlossBlack/Models/OfficeChair_GlossBlack.blend) | [원본](MarbleGold/Models/OfficeChair_MarbleGold.blend) |
| 손님용 의자 | [원본](WhiteWood/Models/GuestChair_WhiteWood.blend) | [원본](WhiteBlack/Models/GuestChair_WhiteBlack.blend) | [원본](GlossBlack/Models/GuestChair_GlossBlack.blend) | [원본](MarbleGold/Models/GuestChair_MarbleGold.blend) |
| 대기벤치 | [원본](WhiteWood/Models/WaitingBench_WhiteWood.blend) | [원본](WhiteBlack/Models/WaitingBench_WhiteBlack.blend) | [원본](GlossBlack/Models/WaitingBench_GlossBlack.blend) | [원본](MarbleGold/Models/WaitingBench_MarbleGold.blend) |
| 1인 암체어 | [원본](WhiteWood/Models/Armchair_WhiteWood.blend) | [원본](WhiteBlack/Models/Armchair_WhiteBlack.blend) | [원본](GlossBlack/Models/Armchair_GlossBlack.blend) | [원본](MarbleGold/Models/Armchair_MarbleGold.blend) |
| ㄱ자 소파 | [원본](WhiteWood/Models/CornerSofa_WhiteWood.blend) | [원본](WhiteBlack/Models/CornerSofa_WhiteBlack.blend) | [원본](GlossBlack/Models/CornerSofa_GlossBlack.blend) | [원본](MarbleGold/Models/CornerSofa_MarbleGold.blend) |
| 1인 사각테이블 | [원본](WhiteWood/Models/SquareTable1_WhiteWood.blend) | [원본](WhiteBlack/Models/SquareTable1_WhiteBlack.blend) | [원본](GlossBlack/Models/SquareTable1_GlossBlack.blend) | [원본](MarbleGold/Models/SquareTable1_MarbleGold.blend) |
| 2인 사각테이블 | [원본](WhiteWood/Models/SquareTable2_WhiteWood.blend) | [원본](WhiteBlack/Models/SquareTable2_WhiteBlack.blend) | [원본](GlossBlack/Models/SquareTable2_GlossBlack.blend) | [원본](MarbleGold/Models/SquareTable2_MarbleGold.blend) |
| 긴 공용 테이블 | [원본](WhiteWood/Models/CommunalTable_WhiteWood.blend) | [원본](WhiteBlack/Models/CommunalTable_WhiteBlack.blend) | [원본](GlossBlack/Models/CommunalTable_GlossBlack.blend) | [원본](MarbleGold/Models/CommunalTable_MarbleGold.blend) |
| 일반 책상 | [원본](WhiteWood/Models/PlainDesk_WhiteWood.blend) | [원본](WhiteBlack/Models/PlainDesk_WhiteBlack.blend) | [원본](GlossBlack/Models/PlainDesk_GlossBlack.blend) | [원본](MarbleGold/Models/PlainDesk_MarbleGold.blend) |
| 칸막이 오피스 책상 | [원본](WhiteWood/Models/CubicleDesk_WhiteWood.blend) | [원본](WhiteBlack/Models/CubicleDesk_WhiteBlack.blend) | [원본](GlossBlack/Models/CubicleDesk_GlossBlack.blend) | [원본](MarbleGold/Models/CubicleDesk_MarbleGold.blend) |
| ㄱ자 책상 | [원본](WhiteWood/Models/CornerDesk_WhiteWood.blend) | [원본](WhiteBlack/Models/CornerDesk_WhiteBlack.blend) | [원본](GlossBlack/Models/CornerDesk_GlossBlack.blend) | [원본](MarbleGold/Models/CornerDesk_MarbleGold.blend) |
| 회의테이블 | [원본](WhiteWood/Models/MeetingTable_WhiteWood.blend) | [원본](WhiteBlack/Models/MeetingTable_WhiteBlack.blend) | [원본](GlossBlack/Models/MeetingTable_GlossBlack.blend) | [원본](MarbleGold/Models/MeetingTable_MarbleGold.blend) |
| 벽걸이 화이트보드 | [원본](WhiteWood/Models/Whiteboard_WhiteWood.blend) | [원본](WhiteBlack/Models/Whiteboard_WhiteBlack.blend) | [원본](GlossBlack/Models/Whiteboard_GlossBlack.blend) | [원본](MarbleGold/Models/Whiteboard_MarbleGold.blend) |
| 벽걸이 블랙보드 | [원본](WhiteWood/Models/Blackboard_WhiteWood.blend) | [원본](WhiteBlack/Models/Blackboard_WhiteBlack.blend) | [원본](GlossBlack/Models/Blackboard_GlossBlack.blend) | [원본](MarbleGold/Models/Blackboard_MarbleGold.blend) |
| 카운터 우드 슬랫 | [원본](WhiteWood/Models/Counter01_WhiteWood.blend) | [원본](WhiteBlack/Models/Counter01_WhiteBlack.blend) | [원본](GlossBlack/Models/Counter01_GlossBlack.blend) | [원본](MarbleGold/Models/Counter01_MarbleGold.blend) |
| 카운터 화이트 패널 | [원본](WhiteWood/Models/Counter02_WhiteWood.blend) | [원본](WhiteBlack/Models/Counter02_WhiteBlack.blend) | [원본](GlossBlack/Models/Counter02_GlossBlack.blend) | [원본](MarbleGold/Models/Counter02_MarbleGold.blend) |
| 카운터 높은 안내 | [원본](WhiteWood/Models/Counter03_WhiteWood.blend) | [원본](WhiteBlack/Models/Counter03_WhiteBlack.blend) | [원본](GlossBlack/Models/Counter03_GlossBlack.blend) | [원본](MarbleGold/Models/Counter03_MarbleGold.blend) |
| 카운터 ㄱ자 | [원본](WhiteWood/Models/Counter04_WhiteWood.blend) | [원본](WhiteBlack/Models/Counter04_WhiteBlack.blend) | [원본](GlossBlack/Models/Counter04_GlossBlack.blend) | [원본](MarbleGold/Models/Counter04_MarbleGold.blend) |
| 카운터 우드 프레임 | [원본](WhiteWood/Models/Counter05_WhiteWood.blend) | [원본](WhiteBlack/Models/Counter05_WhiteBlack.blend) | [원본](GlossBlack/Models/Counter05_GlossBlack.blend) | [원본](MarbleGold/Models/Counter05_MarbleGold.blend) |
| 카운터 낮은 화이트 | [원본](WhiteWood/Models/Counter06_WhiteWood.blend) | [원본](WhiteBlack/Models/Counter06_WhiteBlack.blend) | [원본](GlossBlack/Models/Counter06_GlossBlack.blend) | [원본](MarbleGold/Models/Counter06_MarbleGold.blend) |
| 유리진열카운터 | [원본](WhiteWood/Models/GlassCounter_WhiteWood.blend) | [원본](WhiteBlack/Models/GlassCounter_WhiteBlack.blend) | [원본](GlossBlack/Models/GlassCounter_GlossBlack.blend) | [원본](MarbleGold/Models/GlassCounter_MarbleGold.blend) |
| 벽을 바라보는 바 테이블 | [원본](WhiteWood/Models/WallBarTable_WhiteWood.blend) | [원본](WhiteBlack/Models/WallBarTable_WhiteBlack.blend) | [원본](GlossBlack/Models/WallBarTable_GlossBlack.blend) | [원본](MarbleGold/Models/WallBarTable_MarbleGold.blend) |
| 낮은 파티션 | [원본](WhiteWood/Models/LowPartition_WhiteWood.blend) | [원본](WhiteBlack/Models/LowPartition_WhiteBlack.blend) | [원본](GlossBlack/Models/LowPartition_GlossBlack.blend) | [원본](MarbleGold/Models/LowPartition_MarbleGold.blend) |
| 높은 파티션 | [원본](WhiteWood/Models/HighPartition_WhiteWood.blend) | [원본](WhiteBlack/Models/HighPartition_WhiteBlack.blend) | [원본](GlossBlack/Models/HighPartition_GlossBlack.blend) | [원본](MarbleGold/Models/HighPartition_MarbleGold.blend) |
| 신발장 | [원본](WhiteWood/Models/ShoeCabinet_WhiteWood.blend) | [원본](WhiteBlack/Models/ShoeCabinet_WhiteBlack.blend) | [원본](GlossBlack/Models/ShoeCabinet_GlossBlack.blend) | [원본](MarbleGold/Models/ShoeCabinet_MarbleGold.blend) |
| 형광등 | [원본](WhiteWood/Models/Fluorescent_WhiteWood.blend) | [원본](WhiteBlack/Models/Fluorescent_WhiteBlack.blend) | [원본](GlossBlack/Models/Fluorescent_GlossBlack.blend) | [원본](MarbleGold/Models/Fluorescent_MarbleGold.blend) |
| 스탠드조명 | [원본](WhiteWood/Models/FloorLamp_WhiteWood.blend) | [원본](WhiteBlack/Models/FloorLamp_WhiteBlack.blend) | [원본](GlossBlack/Models/FloorLamp_GlossBlack.blend) | [원본](MarbleGold/Models/FloorLamp_MarbleGold.blend) |
| 책상램프 | [원본](WhiteWood/Models/DeskLamp_WhiteWood.blend) | [원본](WhiteBlack/Models/DeskLamp_WhiteBlack.blend) | [원본](GlossBlack/Models/DeskLamp_GlossBlack.blend) | [원본](MarbleGold/Models/DeskLamp_MarbleGold.blend) |
| 벽시계 | [원본](WhiteWood/Models/WallClock_WhiteWood.blend) | [원본](WhiteBlack/Models/WallClock_WhiteBlack.blend) | [원본](GlossBlack/Models/WallClock_GlossBlack.blend) | [원본](MarbleGold/Models/WallClock_MarbleGold.blend) |
| 흰 이미지 면 액자 | [원본](WhiteWood/Models/PictureFrame_WhiteWood.blend) | [원본](WhiteBlack/Models/PictureFrame_WhiteBlack.blend) | [원본](GlossBlack/Models/PictureFrame_GlossBlack.blend) | [원본](MarbleGold/Models/PictureFrame_MarbleGold.blend) |
| 흰 이미지 면 포스터 | [원본](WhiteWood/Models/Poster_WhiteWood.blend) | [원본](WhiteBlack/Models/Poster_WhiteBlack.blend) | [원본](GlossBlack/Models/Poster_GlossBlack.blend) | [원본](MarbleGold/Models/Poster_MarbleGold.blend) |
| 탁상 잎화분 | [원본](WhiteWood/Models/Plant01_WhiteWood.blend) | [원본](WhiteBlack/Models/Plant01_WhiteBlack.blend) | [원본](GlossBlack/Models/Plant01_GlossBlack.blend) | [원본](MarbleGold/Models/Plant01_MarbleGold.blend) |
| 세로 잎화분 | [원본](WhiteWood/Models/Plant02_WhiteWood.blend) | [원본](WhiteBlack/Models/Plant02_WhiteBlack.blend) | [원본](GlossBlack/Models/Plant02_GlossBlack.blend) | [원본](MarbleGold/Models/Plant02_MarbleGold.blend) |
| 선인장 화분 | [원본](WhiteWood/Models/Plant03_WhiteWood.blend) | [원본](WhiteBlack/Models/Plant03_WhiteBlack.blend) | [원본](GlossBlack/Models/Plant03_GlossBlack.blend) | [원본](MarbleGold/Models/Plant03_MarbleGold.blend) |
| 중형 관엽 화분 | [원본](WhiteWood/Models/Plant04_WhiteWood.blend) | [원본](WhiteBlack/Models/Plant04_WhiteBlack.blend) | [원본](GlossBlack/Models/Plant04_GlossBlack.blend) | [원본](MarbleGold/Models/Plant04_MarbleGold.blend) |
| 대형 관엽 화분 | [원본](WhiteWood/Models/Plant05_WhiteWood.blend) | [원본](WhiteBlack/Models/Plant05_WhiteBlack.blend) | [원본](GlossBlack/Models/Plant05_GlossBlack.blend) | [원본](MarbleGold/Models/Plant05_MarbleGold.blend) |
| 낮은 잎화분 | [원본](WhiteWood/Models/Plant06_WhiteWood.blend) | [원본](WhiteBlack/Models/Plant06_WhiteBlack.blend) | [원본](GlossBlack/Models/Plant06_GlossBlack.blend) | [원본](MarbleGold/Models/Plant06_MarbleGold.blend) |
| 테두리 사각 러그 | [원본](WhiteWood/Models/Rug01_WhiteWood.blend) | [원본](WhiteBlack/Models/Rug01_WhiteBlack.blend) | [원본](GlossBlack/Models/Rug01_GlossBlack.blend) | [원본](MarbleGold/Models/Rug01_MarbleGold.blend) |
| 줄무늬 러너 | [원본](WhiteWood/Models/Rug02_WhiteWood.blend) | [원본](WhiteBlack/Models/Rug02_WhiteBlack.blend) | [원본](GlossBlack/Models/Rug02_GlossBlack.blend) | [원본](MarbleGold/Models/Rug02_MarbleGold.blend) |
| 원형 러그 | [원본](WhiteWood/Models/Rug03_WhiteWood.blend) | [원본](WhiteBlack/Models/Rug03_WhiteBlack.blend) | [원본](GlossBlack/Models/Rug03_GlossBlack.blend) | [원본](MarbleGold/Models/Rug03_MarbleGold.blend) |
| 타원형 러그 | [원본](WhiteWood/Models/Rug04_WhiteWood.blend) | [원본](WhiteBlack/Models/Rug04_WhiteBlack.blend) | [원본](GlossBlack/Models/Rug04_GlossBlack.blend) | [원본](MarbleGold/Models/Rug04_MarbleGold.blend) |
| 체크 러그 | [원본](WhiteWood/Models/Rug05_WhiteWood.blend) | [원본](WhiteBlack/Models/Rug05_WhiteBlack.blend) | [원본](GlossBlack/Models/Rug05_GlossBlack.blend) | [원본](MarbleGold/Models/Rug05_MarbleGold.blend) |
| 현관 러그 | [원본](WhiteWood/Models/Rug06_WhiteWood.blend) | [원본](WhiteBlack/Models/Rug06_WhiteBlack.blend) | [원본](GlossBlack/Models/Rug06_GlossBlack.blend) | [원본](MarbleGold/Models/Rug06_MarbleGold.blend) |
| 방석 | [원본](WhiteWood/Models/SeatCushion_WhiteWood.blend) | [원본](WhiteBlack/Models/SeatCushion_WhiteBlack.blend) | [원본](GlossBlack/Models/SeatCushion_GlossBlack.blend) | [원본](MarbleGold/Models/SeatCushion_MarbleGold.blend) |

