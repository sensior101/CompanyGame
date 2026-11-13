# 펫: 누렁이 강아지 (2026-11-10)

달동네 톤에 맞춘 로우폴리 강아지입니다. 진돗개 계열의 황구(누렁이) 털색에 가슴·주둥이·발이 크림색이고, 테라코타 목줄과 금색 이름표를 달았습니다. 면이 그대로 보이는 로우폴리와 무광 단색 재질은 플레이어·가구 모델과 같은 방식입니다. 펫이라 귀엽게 보이도록 아기 강아지 비율(큰 머리, 짧은 주둥이, 어깨에 붙은 짧은 목, 짧고 통통한 몸통과 다리, 반짝임이 있는 큰 눈, 볼살)을 썼습니다.

## 파일

| 파일 | 내용 |
| --- | --- |
| `build_dog.py` | 모델·리그·모션을 모두 생성하는 단일 스크립트입니다. 다시 실행해도 같은 결과(시드 고정)가 나옵니다. |
| `Dog.blend` | 생성 결과. FK 리그와 액션 7개(`Dog_Idle` 등)가 들어 있어 Blender에서 바로 수정할 수 있습니다. |
| `Dog.fbx` | Unity용. 메시 + 변형 본 26개 + 클립 7개. `CompanyGame/Assets/Art/Pets/Dog/Dog.fbx`로 복사해서 씁니다. |
| `Renders/` | `Dog_Views`(정면·측면·후면·사선), `Dog_Hero`, 모션별 측면 연속 컷(`Dog_Walk`, `Dog_Run`, `Dog_SitDown`, `Dog_Sit`, `Dog_Attack`). |
| `validation.json` | 삼각형·본·클립 길이, 발 높이, IK 오차, 베이크 오차 기록. |

재생성:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --factory-startup --python ArtSource\Pets\Dog_20261110\build_dog.py
Copy-Item ArtSource\Pets\Dog_20261110\Dog.fbx CompanyGame\Assets\Art\Pets\Dog\Dog.fbx
```

그다음 Unity에서 **Tools → Company Game → Pets → Rebuild Dog**를 실행합니다. 배치 실행은 `-executeMethod CompanyGame.Editor.Pets.PetDogImporter.RebuildBatch`입니다.

## 규격

- 1,438삼각형, 재질 6개(털, 크림, 눈·코, 목줄, 이름표, 귀 안쪽). 텍스처는 없습니다.
- 크기: 귀 끝 높이 0.66m, 코~꼬리 0.74m, 등 높이 약 0.4m(플레이어 1.8m 기준 중형견).
- 좌표: 미터 단위, Blender에서 -Y를 바라봄 → Unity에서 +Z(앞)를 바라봄. FBX 설정은 플레이어 모델과 같습니다(`axis_forward='-Z'`, `axis_up='Y'`).
- `Root` 본은 바닥 원점에 있고 움직이지 않습니다. 모든 클립은 제자리 동작이며, 펫의 이동은 게임 코드가 담당합니다.

## 리그 (변형 본 26개)

```
Root
└ Hips ─ Spine ─ Chest ─ Neck ─ Head ─ Jaw, Ear_L, Ear_R
  │              └ Front_Upper_L/R ─ Front_Lower ─ Front_Paw
  ├ Hind_Thigh_L/R ─ Hind_Shin ─ Hind_Hock ─ Hind_Paw
  └ Tail1 ─ Tail2 ─ Tail3
```

- 모션별로 필요한 관절을 넣었습니다. 앉기는 Hips 회전과 뒷다리 4관절(허벅지·정강이·발목·발)로 접고, 공격은 `Jaw`로 입을 벌리고, 감정 표현은 `Ear`와 꼬리 3관절로 합니다.
- 다리는 제작할 때 임시 IK(앞다리 2관절, 뒷다리 2관절 + 발목 추적)로 발을 바닥에 고정해 움직이고, FK 키로 베이크한 뒤 제어 본을 지웠습니다. 그래서 Unity에서는 IK 없이 Generic 리그로 재생됩니다. 베이크 전후의 본 위치 차이는 0입니다(`validation.json`).

## 클립 (30fps)

| 클립 | 길이 | 반복 | 비고 |
| --- | --- | --- | --- |
| Idle | 2.0초 | O | 숨쉬기, 고개 돌리기, 귀 움찔, 꼬리 흔들기 |
| Walk | 0.4초 | O | 4박 걸음. 0.7m/s에서 발이 미끄러지지 않음 |
| Run | 0.27초 | O | 갤럽(척추 굽힘·폄). 3.5m/s 기준 |
| SitDown | 0.8초 | X | 뒤가 먼저 내려가고 앞이 따라옴. 앞발은 제자리 |
| Sit | 2.0초 | O | 앉은 채 숨쉬기, 꼬리 쓸기, 고개 돌리기 |
| StandUp | 0.67초 | X | SitDown의 역순 |
| Attack | 0.8초 | X | 웅크림 → 달려들며 입 벌림 → **12프레임(0.4초)에 입을 닫음** → 복귀 |

- 공격 판정은 0.4초 시점에 넣으면 됩니다(`PetDogAnimator.AttackHitTime`). Unity 애니메이션 이벤트는 넣지 않았습니다. 받는 컴포넌트가 없으면 오류가 나기 때문입니다.
- 플레이어 걷기는 3m/s, 달리기는 4.5m/s입니다. 펫이 따라갈 때는 대부분 Run을 쓰고, 3.5m/s를 넘으면 `MotionScale`로 재생 속도를 올려 발 미끄러짐을 막습니다.

## Unity 연결

- `Assets/Art/Pets/Dog/`: `Dog.fbx`(Generic), `Dog.controller`, `Dog.prefab`, `Materials/Dog_*.mat`(URP Lit 단색).
- Animator 파라미터: `Speed`(m/s, 0 → Idle, 0.7 → Walk, 3.5 → Run 블렌드), `MotionScale`, `Sit`(bool), `Attack`(trigger).
- 상태 흐름: Locomotion ⇄ SitDown → Sit → StandUp → Locomotion, Locomotion → Attack → Locomotion.
- 프리팹 루트의 `PetDogAnimator`(`Scripts/Gameplay/Pet`)에 `SetMoveSpeed`, `SetSitting`, `Attack`만 호출하면 됩니다.

## 아직 없는 것

- 따라다니기 AI, 인벤토리 펫 칸 장착 연결, 멀티플레이 동기화, 펫의 체력·공격 판정. 이 모델과 애니메이터는 그 기능들이 쓸 수 있게만 준비했습니다.
- 다른 견종이나 털색 변형. 팔레트(`PALETTE`)와 비율 상수(`HEAD_SCALE`, `SNOUT_K`, `HEAD_SINK`, `BODY_Y`, `BODY_X`, `LEG_Z`)만 바꿔 같은 리그로 만들 수 있습니다.
