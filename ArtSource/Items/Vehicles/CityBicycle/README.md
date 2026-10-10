# 시티 자전거

제공된 레퍼런스를 바탕으로 Blender 5.2에서 만든 5,400 triangle 자전거입니다.
민트, 크림, 브라운, 블루, 코랄, 차콜 6종이 같은 메시를 공유합니다.
바구니, 낮은 곡선 프레임, 흙받이, 뒤 짐받이, 갈색 안장과 손잡이를 포함합니다.

## 사용

- 플레이 시작 시 기존 InventoryManager가 색상별 1대씩 지급합니다. 현재 인벤토리 구조에 따라 세션 동안 유지됩니다.
- 기본 퀵슬롯 1~6에서 선택하면 손에 작은 자전거 아이템이 보입니다. 왼쪽 클릭으로 앞쪽의 평평한 바닥에 실제 크기로 설치합니다.
- 손에 든 상태에서 Space로 소유권 등록, 5초 길게 눌러 소유권 포기. 등록 시 기본 잠금이며, 집문서와 같은 PropertyRegistry에서 다른 소유자의 중복 등록을 거부합니다.
- 설치된 자전거 가까이에서 Space로 탑승합니다. WASD 이동, Shift 가속, Space 하차. 탑승 중 왼쪽 아래에 조작 안내가 계속 표시됩니다. 실내에서는 설치할 수 없습니다.
- 커서를 설치된 자전거의 외곽 안에 올리면 흰색 테두리, 좌클릭하면 커서 옆 권한 팝업이 표시됩니다. 소유자만 잠금/잠금 해제를 변경할 수 있습니다. 잠금 시 소유자만, 해제 시 누구나 탑승할 수 있습니다.
- 설치된 자전거의 외곽 안을 우클릭하면 드롭 아이템이 되고, F로 줍습니다. 좌클릭 연타는 해체하지 않습니다. 등록된 자전거의 해체는 소유자만 할 수 있습니다. 팝업은 Escape 또는 바깥 클릭으로 닫습니다.
- 화면에 투영된 실제 자전거 실루엣에서 외부 배경과 연결된 공간은 제외하고, 바퀴·프레임으로 둘러싸인 구멍만 채워 hover와 클릭을 판정합니다. 안장과 핸들 사이 위쪽처럼 바깥으로 열린 오목한 부분은 클릭되지 않습니다. 카메라·모델이 바뀔 때만 마스크를 갱신하며 앞을 가린 벽도 검사합니다.
- 손에 쥔 모형은 프레임이 손 위치에 오도록 회전·축소해 배치합니다.
- 속도는 기본 8 m/s, 가속 10.4 m/s입니다. 가속과 감속, 바퀴 회전, 간단한 탑승 자세를 적용했습니다.
- 자전거마다 개별 ID가 있어 같은 색상도 소유권이 분리됩니다. 설치·해체·줍기·보관함 이동·거래 취소 후에도 ID와 소유권, 잠금 상태를 유지합니다. 탑승 중에는 해체할 수 없습니다.
- 맵 전환 시 하차하고 마지막 위치를 기존 바닥 아이템 기록에 남깁니다. 같은 플레이 세션에서 해당 맵으로 돌아오면 복원됩니다.
- 탑승 중에는 1인칭 헤드밥을 적용하지 않습니다. 탑승/하차와 시점 변경 시 흔들림 값을 초기화하고, 하차 후 도보 헤드밥이 다시 작동합니다.

## 파일

- `CityBicycle.blend`: 편집 가능한 Blender 원본
- `build_city_bicycle.py`: 메시 제작, FBX 내보내기, 6색 아이콘 렌더
- `integrate_city_bicycle.cs`: Unity CLI `eval_file`로 실행하는 에셋 연결 스크립트 (Edit Mode)
- `Preview.png`: 제작 명령으로 다시 생성하는 Blender 렌더. 버전 관리하지 않는다.
- `verify_bicycle_flow.cs`, `verify_bicycle_input.cs`, `verify_vehicle_ownership.cs`, `verify_vehicle_scene.cs`: 기존 플레이어를 사용하는 Play Mode 검증 코드. Unity CLI `eval_file`로 실행합니다.
- `verify_vehicle_silhouette.cs`: 외곽 내부 선택과 열린 배경 제외, 앞쪽 장애물 차단 검증.
- `QA/`: 검증 시 생성하는 보고서·화면·판정 마스크의 로컬 출력 경로. 생성 자료는 정리했으며 버전 관리하지 않는다.
- Unity 모델/재질/프리팹/아이콘: `CompanyGame/Assets/Art/Items/Vehicles/CityBicycle`
- 아이템 데이터: `CompanyGame/Assets/Resources/Inventory/Vehicles/CityBicycle`

기존 Player, PlayerMovement, InventoryManager를 사용합니다. 이동용 플레이어나 별도 매니저를 생성하지 않습니다.
추가된 PlayerVehicle은 기존 플레이어에 부착되어 탑승 모델과 상태만 관리합니다.

## 검증 (2026-10-10)

- Unity 6000.4.5f1에서 6색 에셋 연결, 중복 지급 방지, 손 표시, 설치·탑승·해체·줍기와 수량 보존을 확인했습니다.
- 등록·소유권 포기·잠금 입력, 외곽 판정, 장애물 차단, 씬 이동 후 ID·소유권 복원을 검증했습니다.
- 탑승 중 시점 전환·가속 시 헤드밥이 0이며 하차 후 도보 헤드밥이 복구됨을 확인했습니다. 위 검증 도구는 임시 입력 설정을 검사 후 복원합니다.
- 소유권 검증은 집문서와 동일한 로컬 플레이어 이름 판정으로 두 사용자의 허용/거부를 확인합니다. 실제 네트워크 세션 동기화는 현재 프로젝트의 별도 멀티플레이 연동 범위입니다.

## 다시 생성

저장소 루트에서 Blender를 실행합니다.

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --factory-startup --disable-autoexec --python 'ArtSource/Items/Vehicles/CityBicycle/build_city_bicycle.py'
```

Unity가 임포트를 마친 뒤 Edit Mode에서 `integrate_city_bicycle.cs`를 Unity CLI의 `eval_file`로 실행합니다.
