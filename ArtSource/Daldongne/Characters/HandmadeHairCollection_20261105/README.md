# 현재 헤어 모델 — 걷기 중 머리·헤어 부착 오류 수정

## 신규 요청 4종 — 단발 반묶음 / 앞머리 없는 장발·단발 / 양갈래

[Blender 파일·미리보기·검증 기록](FiveStyles_20261105/README.md)에서 확인할 수 있습니다. 기존 원본은 보존하고 별도 폴더에 제작했습니다. Unity 미반영입니다.


이전 모델은 움직임용 Armature만 Head를 따라가고 실제 헤어 메시의 부모는 정지해 있었습니다. 이전 미리보기에서 최대 약 **49.0mm**의 부착 오차를 재현했습니다. 메시의 기존 부착 부모를 `Ponytail_SoftMotionRig` 아래에 연결해 메시와 뼈대가 같은 Head 변환을 따르도록 수정했습니다. 정지 상태의 메시 모양과 UV는 유지했습니다.

이전의 “뿌리 추가 이동 0” 검사는 헤어 자체의 변형만 검사해 이 오류를 놓쳤습니다. 이번에는 두상 메시 전체를 실제 플레이어 Head의 변환과 직접 비교하고, 저장 후 실제 머리 피부 정점도 따로 검증했습니다. 프로젝트의 `FemaleWalk.fbx`로 걷기 3주기와 정지 구간을 재생한 **180프레임에서 최대 부착 오차는 0.000601mm**였습니다.

## Blender 움직임 적용

포니테일에 움직임용 본 28개와 정규화된 가중치를 추가했습니다. 기존 메시의 정점·면·UV와 정지 상태 모양은 유지합니다. 묶인 뿌리, 두상, 옆머리 바탕은 고정하고 긴 꼬리 끝과 귀 앞 애교머리에 작은 관성 움직임을 줍니다. 앞머리는 끝부분에만 아주 약한 움직임을 허용합니다.

- `Ponytail_FitReview.blend`: 실제 플레이어의 `FemaleRig.Head`를 따라가는 리깅 완료 모델, 정지 상태.
- `Ponytail_Editable.blend`: 본·가중치가 포함된 헤어 전용 모델. 플레이어 리그에 대한 외부 의존성은 제거했습니다.
- `Ponytail_WalkAttached_Demo.blend`: 기존 FemaleWalk를 사용한 30fps, 180프레임(6초) 걷기 검토 동작. 열고 타임라인을 재생하면 됩니다. 처음 0.4초를 유지한 뒤 걷기 3주기를 재생하고 3.5초부터 멈춥니다. `Ponytail_SubtleMotion_Demo.blend`도 같은 수정본으로 갱신했습니다.
- `Ponytail_WalkAttached.mp4` / `.gif`: 전신 걷기를 보여 주는 실제 Eevee 렌더. MP4는 640×880, 30fps, 180프레임이며 GIF는 393×540, 총 6초입니다. GIF의 연속된 동일 프레임은 인코딩 시 병합됩니다. 이전 `Ponytail_SubtleMotion` 영상 이름도 현재 결과로 갱신했습니다.

흔들림은 Head의 실제 이동 가속도·회전 속도를 입력으로 한 감쇠 스프링으로 계산했습니다. 정지 중 바람이나 반복 사인파를 넣지 않습니다. 꼬리의 목표 기울기는 최대 2.2도, 애교머리는 2도, 앞머리는 0.35도로 제한하고, 몸체 관통이 생기면 해당 가닥만 제한합니다. 각 본에 분배되는 회전량과 실제 끝점 이동량은 별개이며, 실제 이동량은 검사 JSON에 기록합니다.

이번 걷기 동작의 실제 최대 추가 이동은 꼬리 약 **15.3mm**, 애교머리 **4.1mm**, 앞머리 **0.34mm**입니다. 입력이 멈추고 약 **0.63초** 후 모든 부위의 추가 이동이 0.1mm 미만으로 안정됩니다. 고정 뿌리의 추가 이동은 0이며, 180프레임 검사에서 강체 부착 대비 추가 몸체 교차는 0입니다. 부착을 수정한 후에는 관통에 따른 가닥 움직임 제한도 0회였습니다.

**범위:** Blender 리깅 및 소스 동작에서 계산한 베이크입니다. 저장된 미리보기는 Python 자동 실행 없이 재생됩니다. 다른 소스 동작을 쓰면 다시 베이크해야 합니다. Unity 실시간 물리·게임 이동 연동은 구현하거나 임포트하지 않았습니다. 수정하지 않은 바탕 메시의 기존 관통·위상 문제도 남아 있습니다.

### 이번 작업 파일·재사용·검증

1. 수정한 기존 파일: `build_collection.py`, `Ponytail_FitReview.blend`, `Ponytail_Editable.blend`, 기존 Demo·영상·검사 자료, 이 README, 요약 JSON. `../encode_motion_preview.py`는 기존 선택 모드를 그대로 재사용했습니다.
2. 신규 파일과 이유: `Ponytail_WalkAttached_Demo.blend`, 같은 이름의 MP4/GIF/프레임 PNG는 실제 걷기 검토용입니다. `Ponytail_WalkAttached_*validation.json` 및 `Ponytail_attachment_stress_validation.json`은 재현·부착·저장 검증 자료입니다. 잘못된 이전 결과는 `_History/BeforeHeadAttachmentFix_20261105/`에 보관했습니다. 신규 Python/게임 스크립트는 없습니다.
3. 재사용한 기능: 기존 머리 메시·HairFlow 재질·`FemaleRig.Head`·`FemaleWalk.fbx`·리뷰 카메라·`smooth`, `activate`, `geometry_signature`, `evaluated_surface`, `bake_soft_hair` 및 기존 미리보기 인코더를 사용했습니다. 새 걷기 동작을 만들지 않고 기존 클립의 24개 일치 본 포즈를 사용했습니다.
4. 변경된 흐름: FemaleWalk → 실제 플레이어 Head → 헤어 Armature와 메시 부모의 공통 변환 → 가닥별 감쇠 스프링 → Head 대비 두상 전체 정점 검사 → 저장·재열기·렌더. 게임 데이터 흐름 변경 없음.
5. 중복 구현 여부: 별도의 게임 공통 기능이나 관리 클래스 추가 없음. Blender용 베이크는 이 기존 제작 스크립트에만 구현했습니다.
6. 영향: 장발·반묶음 자료 24개, 원본 단발·플레이어·걷기 클립, Unity 코드·프리팹·공개 API·저장 데이터 변경 없음. 포니테일의 모양·본·가중치를 유지하고 메시 부모 연결을 고쳤습니다.
7. 검증: 머리 회전·이동·골반 회전 조합, 걷기 180프레임의 Head 부착, 고정 뿌리, 정지 후 감쇠, 추가 몸체 관통을 검사했습니다. 저장 후 8개 시점에서 두상과 실제 머리 피부 정점 83개가 동일한 Head 변환을 따르는지 독립적으로 확인했습니다. 실제 측정치는 부착 stress, WalkAttached 및 soft_motion 검사 JSON을 참고합니다. Unity 실행 및 다른 애니메이션 전체는 미검증입니다.

### 재생성 및 다른 동작 베이크

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --factory-startup --addons cycles,io_scene_fbx --python './build_collection.py' -- --pony-soft-rig --pony-soft-demo --pony-soft-render
python '../encode_motion_preview.py' --frames './Ponytail_WalkAttached_Frames' --output './Ponytail_WalkAttached.gif' --fps 30 --expected 180
```

Demo의 `HairMotion_tools.py` 텍스트에는 명시적으로 실행 가능한 제작 함수가 들어 있습니다. 텍스트 실행 후 `bake_soft_hair(start, end)`를 호출하면 현재 소스 Head 애니메이션에서 다시 계산합니다. Blender의 스크립트 자동 실행 설정은 변경하지 않았습니다. `--pony-soft-rig`는 백업된 정지 모델에서 시작하고, `--pony-soft-demo`는 기존 FemaleWalk를 읽어 걷기 검토 동작을 준비합니다.

## 이전 모양 수정 기록 — 애교머리 숱

최근 수정: 귀 앞쪽 애교머리를 **한쪽당 2가닥에서 4가닥**으로 늘렸습니다. 추가 가닥은 길이와 끝 위치를 조금씩 달리했습니다. 기존 메시 26개는 좌표·면·UV·행렬 해시가 동일합니다. 두상·앞머리·옆머리·목덜미·꼬리 및 기존 애교머리를 움직이지 않고, 기존 `leaf`와 `mirror_pair`로 얇은 가닥 네 개만 추가했습니다.

- 수정 파일: 기존 `build_collection.py`, 포니테일 착용/헤어 전용 `.blend`, 최신 리뷰 PNG, 전체 검사 JSON 및 이 문서.
- 신규 파일: `FullerWisps_Ponytail_*.png` 5장은 현재 Blender 렌더입니다. `Ponytail_wisp_density_validation.json`은 추가 가닥 검사·기존 메시 보존·두 저장 파일 재열기 검증 자료입니다. 수정 전 자료는 `_History/BeforePonyWispDensity_20261105/`에 보관했습니다.
- 재사용/흐름: 현재 착용 모델 읽기 → 기존 모발 재질과 Head 기준점에 가닥 추가 → 기존 `inspect`로 검사 → 저장·재열기 → 기존 `render`로 PNG 생성. 신규 게임 스크립트나 공통 기능 중복 구현 없음.
- 검증: 추가 네 가닥의 몸체 교차·열린 경계·비다양체 변·퇴화 면 모두 0. 착용/헤어 전용 파일의 저장 메시 일치. 장발·반묶음 자료 24개 SHA-256 동일. 기존 바탕 메시의 관통/위상 문제는 그대로이며 Unity·애니메이션은 미검증입니다. 게임 데이터 흐름·API·호출부 변경 없음.
- 재생성: `--pony-wisp-density --pony-wisp-final`. 이 단계는 위 백업에서 시작하므로 반복해도 숱이 누적되지 않습니다. 아래 귀·목덜미 수정 단계만 실행하면 이번 숱 추가 이전 상태가 됩니다.

아래는 이 모델에 유지된 직전 귀·목덜미 수정 내역입니다.

2026-11-05. 이번 수정은 포니테일에만 적용했습니다. 장발·반묶음의 모델·이미지·개별 검사 자료 24개는 SHA-256이 작업 전후 동일합니다.

## 포니테일 변경

- **귀 뒤·아래와 뒷머리 채움:** 기존 두상 모발의 아랫부분을 내려 귀 뒤에서 목덜미로 이어지도록 채웠습니다. 별도 도형을 덧대지 않고 기존 연속 면을 수정했습니다.
- **옆머리 밀착:** 실제 플레이어의 머리 표면을 기준으로 옆머리를 얼굴 쪽으로 좁혀 붙였습니다. 귀가 뚫고 나오지 않도록 귓바퀴 주변 경계도 조정했습니다.
- **애교머리:** 위쪽·바깥쪽에서 시작하던 넓은 가닥 1개씩을 제거하고, 귀 앞쪽에서 시작하는 얇은 가닥 2개씩으로 바꿨습니다. 주 가닥의 최대 설계 폭은 약 8mm, 보조 가닥은 약 4mm입니다. 시작 부위 높이는 약 1.705m에서 약 1.597m로 내려갔습니다.
- **보존:** 앞머리 6가닥, 긴 포니테일 꼬리와 바깥 가닥, 묶음 밴드, 플레이어 본체, 정수리 위쪽, 재질, Head 부착 기준점과 카메라. 대상 밖 메시의 좌표·면·UV·월드 행렬 해시 일치를 확인했습니다.

| 스타일 | 현재 삼각형 수 | 착용 파일 | 헤어 전용 파일 |
|---|---:|---|---|
| 장발 | 37,914 | [LongLayers_FitReview.blend](LongLayers_FitReview.blend) | [LongLayers_Editable.blend](LongLayers_Editable.blend) |
| 포니테일 | 42,906 | [Ponytail_FitReview.blend](Ponytail_FitReview.blend) | [Ponytail_Editable.blend](Ponytail_Editable.blend) |
| 반묶음 | 52,566 | [HalfUp_FitReview.blend](HalfUp_FitReview.blend) | [HalfUp_Editable.blend](HalfUp_Editable.blend) |

## 현재 이미지

- `FullerWisps_Ponytail_Front.png`, `Side.png`, `Back.png`, `ThreeQuarter.png`, `BackThreeQuarter.png`: 애교머리 숱을 늘린 현재 모델의 실제 Cycles 렌더. 모두 900×1000 PNG입니다. `EarFit_Ponytail_*.png`는 숱 추가 직전 단계입니다.
- 일반 `Ponytail_*.png`와 앞·옆·뒤 `Refined_Ponytail_*.png`도 최신 결과로 갱신했습니다.
- 장발·반묶음은 직전 단계에서 확대했던 앞머리와 완만하게 정리한 옆머리를 유지합니다. 해당 `Refined_*.png`가 그대로 현재 상태입니다.

## 검증과 한계

- `Ponytail_ear_fit_validation.json`: 수정 대상별 검사, 변경하지 않은 메시 보존, 변경 전후 몸체 교차 비교.
- `Ponytail_ear_fit_summary.json`: 두 저장 .blend 재열기, 착용 파일/헤어 전용 파일의 메시 일치, 다른 두 스타일 파일 보존 확인.
- 새 애교머리는 몸체 교차 0, 열린 경계 0, 비다양체 변 0입니다. 변경한 두상/옆머리에도 이번 수정으로 새로 생긴 몸체 교차가 없습니다.
- 원래 수정하지 않은 정수리 주변 등에 있던 몸체 교차와 바탕 메시의 기존 위상 문제는 별도 항목으로 남아 있습니다. 전체 헤어가 모든 구조 검사를 통과했다고 주장하지 않습니다. 최신 전체 수치는 `Ponytail_validation.json`에 기록했습니다.
- Blender 정지 착용과 실제 다각도 렌더를 확인했습니다. Unity 임포트, 애니메이션, 모발 흔들림, 게임용 재질 베이크는 수행하지 않았습니다.

## 작업 완료 보고

1. **수정한 기존 파일:** `build_collection.py`, `Ponytail_FitReview.blend`, `Ponytail_Editable.blend`, 포니테일 리뷰 PNG·전체 검사 JSON, 요약 및 README.
2. **신규 파일 및 이유:** `EarFit_Ponytail_*.png`는 이전 캐시와 구분되는 현재 결과입니다. `Ponytail_ear_fit_validation.json`과 `Ponytail_ear_fit_summary.json`은 수정 범위·저장 결과 검증 자료입니다. 수정 전 포니테일 자료는 `_History/BeforePonyEarFit_20261105/`에 보관했습니다. 신규 게임 스크립트 없음.
3. **재사용:** 기존 `world_bvh`, `surface_reader`, `leaf`, `mirror_pair`, `sample`, `geometry_signature`, `inspect`, `render`, 기존 두상 프로파일/UV, HairFlow 재질, `FemaleBody`, `FemaleRig.Head`, 기존 리뷰 카메라. 기존 제작 도구에 포니테일 한정 수정 기능을 추가했습니다.
4. **데이터 흐름:** 직전 수정본 읽기 → 기존 옆머리/뒷머리 면의 귀·목덜미 경계 조정 및 밀착 → 애교머리 4가닥 교체 → 원래 Head 기준점에 정렬 → 대상 밖 메시 및 관통 변화 검사 → Blender 저장·재열기·렌더. 게임 데이터 흐름 변경 없음.
5. **중복 구현:** 공통 게임 기능/관리 클래스 추가 없음.
6. **기존 기능·호출부 영향:** 장발·반묶음 파일, 원본 플레이어·단발 파일, Unity 코드·UI·프리팹·API·저장 데이터 변경 없음. 커밋/푸시 없음.
7. **검증/미검증:** 위 검증과 한계를 참고합니다. 두 .blend의 실제 헤어 메시가 같고, 이번에 추가된 몸체 교차가 없음을 확인했습니다.

## 재생성 및 기록

이번 포니테일 수정은 항상 `_History/BeforePonyEarFit_20261105/Ponytail_FitReview.blend`에서 시작하므로 반복 실행해도 변형이 누적되지 않습니다.

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --factory-startup --addons cycles --python './build_collection.py' -- --pony-ear-fit --pony-ear-final
```

`--refine`은 직전 앞머리/장발 옆머리 수정 단계입니다. 전체를 다시 만드는 경우 `--refine --pony-ear-fit --refined-final` 순서로 적용합니다. `--build`는 최초 캡처 모델 제작 경로입니다. 이전 `Ponytail_refine_validation.json`과 `restore_validation.json`은 해당 과거 단계의 기록이며, 현재 포니테일의 최신 범위 검사는 `Ponytail_ear_fit_validation.json`입니다.
