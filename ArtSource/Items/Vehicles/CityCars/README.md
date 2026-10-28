# 도시 자동차 세트 (CityCars)

실차 18종을 이름만 바꾼 패러디 차량 세트(로고·엠블럼·실명 표기 없음)를 Blender 5.2 스크립트 하나로 만듭니다.
레퍼런스: `Reference/Car_References_18.png` (차종별 앞 3/4·옆·뒤 사진).

| 코드 | 실차 | 게임 이름 | 상태 |
|---|---|---|---|
| C01 | 모닝 | 나이트 | 게임에 이미 있음 |
| C02 | 레이 | 레이스 | 미착수 |
| C03 | 아반떼 | 아방데 | 게임에 이미 있음 |
| **C04** | **쏘나타(DN8)** | **소나티네** | **완성·검수 대기** |
| C05 | 그랜저 | 그랜드 | 미착수 |
| C06 | 제네시스 G80급 | 제네시아 | 미착수 |
| C07 | 셀토스 | 셀토즈 | 미착수 |
| C08 | 스포티지 | 스포티즈 | 미착수 |
| C09 | 카니발 | 카니발레 | 미착수 |
| C10 | BMW 3시리즈 | 바이에른 B3 | 미착수 |
| C11 | BMW 5시리즈 | 바이에른 B5 | 게임에 이미 있음 |
| C12 | BMW 7시리즈 | 바이에른 B7 | 미착수 |
| C13 | BMW M3급 | 바이에른 M | 미착수 |
| C14 | 아우디 A4·A6·A8 | 아우디온 | 미착수 |
| C15 | 벤츠 C·E·S | 벤처스 | 미착수 |
| C16 | 벤틀리 플라잉스퍼급 | 밴틀러 컨티넨털 | 게임에 이미 있음 |
| C17 | 롤스로이스 | 롤스로얄 | 미착수 |
| C18a / C18b | 포르쉐 911 / 람보르기니 우라칸 | 포르셰 9 / 람보르노 | 미착수 |

## 규격 (기존 `Assets/Art/Items/Vehicles/Cars` 관례와 동일)

- 단위 m, 실차 치수. Unity 기준 +X 오른쪽, +Y 위, +Z 전방. FBX는 `-Z forward / Y up`.
- 계층: `Car_<code>` (빈 오브젝트) > `Car_<code>_Body`, `Car_<code>_Wheel_FL/FR/RL/RR`
  - Body 피벗 = 지면 중앙(차체 중심 x=0; 기존 차들은 x≈+0.37 치우침이 있었음).
  - 바퀴 4개는 메시 1개 공유, 피벗 허브 중심, 회전축 X. 왼쪽 바퀴는 Y축 180° 회전 배치.
- 재질 슬롯 `Car_*` 공유: Trim, Paint_<code>, Glass, LampGlass, LightFront, LightRear, Plate, Chrome, DarkChrome, Interior, Seat, Headliner / 바퀴: Rubber, Alloy, Chrome, Steel, Trim.
  색상 변형은 `Car_Paint_<code>` 색만 바꿉니다(메시 공유). 변형 목록은 `manifest.json`.

## 파일

- `build_city_cars.py`: 모델·재질·FBX·렌더·비교 시트를 처음부터 만드는 스크립트 (`SPECS` 에 차종 정의)
- `CityCars.blend`: 생성된 원본 (스튜디오 조명·카메라 포함)
- `Car_<code>.fbx`: Unity용 (루트 Empty + Body + Wheel 4개)
- `Renders/Car_<code>.png`: 앞 3/4 (시트용), `_side/_front/_rear/_rear34.png`: 검수용. 카메라 각도는 레퍼런스 사진(앞 3/4·옆·뒤 3/4)에 맞춤
- `Renders/C04_vs_ref.png`: 레퍼런스 타일(위)과 렌더(아래) 나란히 비교
- `Renders/CityCars_Sheet.png`: 전체 비교 시트 (여러 대 생성 시 함께 만들어짐)
- `manifest.json`: 코드, 이름, 치수, 바퀴 위치, 삼각형 수, 재질, 색상 변형, 파일

## 제작 기법 (팀 선례에서 가져온 것)

- 차체: 13점 반단면 케이지(약 60 스테이션) 로프트 → Subdivision 2단계. 벨트·숄더·캐릭터라인·실은 edge crease, 굽힘(카울·루프·뒷유리·데크)은 원호 필렛+지지 루프. 앞뒤 끝은 하단 프로파일 점을 안쪽으로 당겨(tuck) 범퍼 경사면을 만든다. 휠 아치 불리언, 축 주변 펜더 볼륨.
- 유리: 로프트 면에 재질만 할당(플러시). Light Path 트릭(그림자·확산 광선 투명)으로 실내가 비치고 노이즈 없음.
- 램프(어두운 유닛 + DRL 선, 펜더로 스윕), 그릴(크롬 프레임·슬랫 사다리꼴), 번호판 홈, 평평한 머플러 팁, 윈도우 크롬 라인, 실 가니시, 도어 심: Shrinkwrap 패치 또는 레이캐스트로 표면 밀착.
- 재질: Principled. 페인트 = 메탈릭 .08 · 거칠기 .45 · Coat 1.0(거칠기 .05). 나머지는 기존 소품 스크립트와 같은 값대.
- 조명: 팀 소품 선례(DISK 소프트박스 Key/Fill/Rim + 회색 월드 + AgX)를 차 크기로 확대, 라이트는 크고 어둡게. 디노이저 OpenImageDenoise.

## 다시 생성

저장소 루트에서:

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --factory-startup --python-exit-code 1 --python 'ArtSource/Items/Vehicles/CityCars/build_city_cars.py' -- --only C04 --samples 256 --no-sheet
```

옵션: `--only C04,C05` (일부만), `--samples N` (Cycles 샘플, 기본 160), `--no-render`, `--no-sheet`, `--no-denoise`, `--no-top`(조명 진단용). 옵션 없이 실행하면 `SPECS` 전체 + 비교 시트.
RTX GPU(OptiX)가 있으면 자동 사용. 1대 기준 생성+렌더 5장 약 25초.

Unity 프로젝트에는 아무것도 쓰지 않습니다. Unity 연결(프리팹·재질 매핑)은 별도 작업입니다.
