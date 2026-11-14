# 부동산 사무실 내부 — 2026-11-07

`ShopShell_NoWindows.blend`(실내 26 × 16.4m, 천장 3.3m)를 부동산 RE_01의 내부로 연결하고 기존 화이트앤우드 가구 130점으로 사무실을 꾸몄다.

- 씬: `CompanyGame/Assets/Scenes/Interiors/RealEstateOfficeInterior.unity`
- 구역: 입구, 안내 카운터, 대기 라운지, 매물 벽과 매물 안내판, 상담 데스크 3세트, 서류장, 직원 책상 3개, 대표 책상, 파티션 회의실
- 결과 화면: [위에서 본 내부](QA/02_Cutaway.png) · [입장 직후](QA/11_Play_Inside.png) · [라운지](QA/04_Lounge_Eye.png) · [상담 데스크](QA/05_Consult_Eye.png) · [회의실](QA/06_Meeting_Eye.png) · [밖으로 나온 뒤](QA/13_Play_BackOutside.png)

## 다시 만들기

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' -b --factory-startup --python 'ArtSource/WorldDistricts/RealEstateInterior_20261107/export_interior.py'
```

그다음 Unity를 닫고 `-batchmode -executeMethod RealEstateInteriorBuilder.Build -quit`, `RealEstateInteriorQA.RunLayout -quit`, `RealEstateInteriorQA.RunPlay`(`-quit` 없음, 스스로 종료) 순서로 실행한다. 다시 실행해도 내부 씬과 포털은 중복 없이 새로 만들어진다. 가구 위치는 `RealEstateInteriorBuilder.BuildScene`에서 고친다.

## 작업 보고

1. 수정한 기존 파일: `Assets/Scenes/Maps/CivicDistrict.unity`(문 앞 포털과 나오는 지점 추가), `ProjectSettings/EditorBuildSettings.asset`(내부 씬 등록), `Assets/Resources/CombatCollision/manifest.json`과 CivicDistrict 서버 충돌 프리팹.
2. 새 파일: `Assets/Art/Interiors/RealEstateOffice/`(FBX, 재질, 프리팹, 매물 카드 텍스처), 내부 씬과 그 서버 충돌 프리팹, `Assets/Scripts/Editor/WorldMaps/RealEstateInteriorBuilder.cs`(생성), `RealEstateInteriorQA.cs`(검증), 이 폴더의 `export_interior.py`와 QA 기록.
3. 재사용: 시청과 같은 `StoreInteractionPoint`·`MapSpawnPoint`·`SceneLoadManager`, `PlayerInteraction.TryUseDoor`, `Seat`·`WorldObject`, `CombatCollisionBuilder.BuildMap`, 기존 WhiteWood 가구 프리팹과 `WW_Oak` 재질.
4. 흐름: 문 앞에서 상호작용 → `realestate_entry`로 입장 → 안쪽 나가기 지점에서 상호작용 → CivicDistrict의 `realestate_exit`로 복귀.
5. 중복 구현 없음. 런타임 코드를 추가하지 않았다. 생성·검증 도구는 Editor 전용이다.
6. 공개 API·저장 데이터·호출부 변경 없음. 배치한 가구는 고정 소품(`StaticProp`)이라 플레이어가 가져갈 수 없다. 의자와 소파의 앉기 기능은 유지했다.
7. 검증: `QA/layout.txt`(벽 관통, 가구 겹침, 플레이어 크기 기준 이동 가능 영역, 좌석 접근, 포털 연결), `QA/play.txt`(Play 모드에서 실제 플레이어가 문으로 입장, 11구간 이동, 복귀).

## 2026-11-07 수정

- 컴퓨터(`DesktopSet`)는 `HouseholdLowPoly/build_household.py` 레시피에서 키보드·본체 간격을 줄였다.
- 가구 124개 파일을 게임 크기로 키웠다. 치수 표는 `Items/Furniture/furniture_sizes_20261107.json`, 적용은 `Items/Furniture/apply_sizes.py`(각 .blend의 메시 정점을 직접 변경)다. Unity 배율은 1 그대로다. 예: 화이트보드 2.6×1.4m, 일반 책상 1.8×0.9×0.78m, 안내 카운터 3.2×1.05×1.12m.
- 사무실 1인칭 FOV를 기본 85에서 75로 낮췄다(편의점·고시원과 동일).
- 사무실 수납장 20점에 고정 `storageId`를 주어 서버 목록(`StorageLocations.json`)에 등록했다.
- 보관함은 별도 목록 창 대신 `InventoryUI.Container.cs`로 같은 인벤토리 창에 슬롯을 붙여 연다.
- 이 수정은 위 작업 보고 5·6번과 달리 런타임 코드를 바꾼다: `FridgeInteraction`, `FridgeStorage`, `InventoryUI`, `InventorySlotPointer`, `PlayerInventory`, `SeatInteraction`, `FurnitureLightInteraction`, `StorageType`.

## 2026-11-11 매매 1단계

- 직원 6명(`15_Staff`): 안내 직원(카운터), 1번 부지 상담, 2번 상가 상담, 3번 주택 상담(동쪽 상담 데스크), 사무 직원 2명(뒤쪽 책상, 대화만). 모두 의자에 앉아 있다(`SeatedStaff`). 그 의자에는 플레이어가 앉을 수 없다.
- 매물 64곳(`Assets/Resources/RealEstate/Listings.json`): 부지 8(업무 지구 빈 부지), 상가 32(상업 지구·해변·업무 지구), 주택 24(달동네 원룸·단독주택, 비치하우스, 고급 빌라). 맵의 `MapParcel`에서 `RealEstateListingBaker.Bake`로 만들고, 가격표는 그 파일 위쪽 표에 있다.
- 상담: 매물 보기(싼 순서로 5개씩, 다음 목록) → 상세(주소·크기·가격·계좌 잔액) → 구매하면 계좌에서 결제. 내 매물 팔기는 구매가의 80%로 중개소가 다시 산다. 사업자 등록을 한 곳은 팔 수 없다. 안내 직원은 내 부동산 현황과 창구 안내를 한다.
- 소유는 기존 `LandPlotManager`(서버 `land.buy`)를 쓴다. 판매는 새 요청 `land.sell`(서버 `ServerLedger.SellLand`가 계좌에 지급)이다. 서버 가격은 `LandPlot.PriceOf`가 매물표에서 읽는다.
- 상담 직원 바로 뒤 수납장에서는 수납장이 우선이다(더 가까운 쪽이 스페이스바를 가진다. `DialogueManager.YieldTo`).
- 검증: `QA/layout.txt`(직원 6명 좌석, 매물 수, 서버 가격), `QA/play.txt`(주택 창구에서 목록·잔액 부족 거절·구매·되팔기·사업자 등록지 판매 거절, 안내 현황, 부지·상가 창구 목록), `LedgerQA.RunPlay`(서버 `land.sell` 지급과 남의 땅 판매 거절). 화면: `QA/20_Play_HouseListings.png`, `21_Play_HouseDetail.png`, `22_Play_Reception.png`, `23_Play_Desks.png`.

## 한계

- 키 입력 대신 `TryUseDoor`를 직접 호출했고, 이동은 실제 `CharacterController`로 시켰다. 사람이 키보드로 조작한 시험은 아니다.
- 앉기 동작 자체와 멀티플레이 접속 상태의 출입·보관함은 시험하지 않았다.
- 플레이어가 직접 설치한 가구의 `furniture:<id>:<n>` 보관함은 서버 목록에 없어서, 서버 접속 중에는 서버가 거절한다(`ServerLedger.Storage`).
- Blender 카탈로그 미리보기 렌더(`Preview.png`, `QA/*/Cream.png`, `Previews/`)와 `CommercialFurniture_RoomScale.blend`는 다시 만들지 않아 예전 크기다.
- 문짝은 닫아 둔 고정 상태다. 문을 통과해 걸어 나가지 않고, 문 앞에서 상호작용해 드나든다.
- 직원은 지금 프로젝트의 기본 아바타(`MaleVisual`·`FemaleVisual`)라 옷이 없다. 플레이어·시민·고시원 주인도 같은 상태다.
- 주택 매물에는 들어갈 내부가 없다(고시원 제외). 사면 지도와 현황에 내 소유로 나올 뿐이다. 고시원 방은 시작 집문서 제도 그대로이며 매물에 넣지 않았다.
- 월세·전세·상가 임대(2단계)와 플레이어끼리 거래는 없다.
- `QA/01_TopDown.png`의 수직 카메라에서만 일부 가구 윗면 색이 어둡거나 얼룩져 보인다. 눈높이와 내려다본 시점(`14`, `15`)에서는 정상이며 원인은 확인하지 않았다.
