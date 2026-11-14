# 단발 반묶음 · 앞머리 없는 장발/단발 · 양갈래

2026-11-05. 기존 여성 플레이어 크기에 맞춘 Blender 검토본 4종입니다. 실제 메시와 기존 HairFlow 재질을 사용한 렌더이며, Unity에는 임포트하지 않았습니다.

| 종류 | 삼각형 | 헤어 본 | Blender 파일 |
|---|---:|---:|---|
| 단발 반묶음 | 41,056 | 7 | [착용](BobHalfUp_FitReview.blend) · [헤어만](BobHalfUp_Editable.blend) · [걷기](BobHalfUp_Walk.blend) |
| 앞머리 없는 장발 | 44,800 | 4 | [착용](LongNoBangs_FitReview.blend) · [헤어만](LongNoBangs_Editable.blend) · [걷기](LongNoBangs_Walk.blend) |
| 앞머리 없는 단발 | 44,800 | 4 | [착용](BobNoBangs_FitReview.blend) · [헤어만](BobNoBangs_Editable.blend) · [걷기](BobNoBangs_Walk.blend) |
| 양갈래 | 52,298 | 33 | [착용](TwinTails_FitReview.blend) · [헤어만](TwinTails_Editable.blend) · [걷기](TwinTails_Walk.blend) |

- `FitReview`: 기존 플레이어에게 착용한 정지 모델입니다.
- `Editable`: 플레이어 몸체 없이 헤어 메시와 본만 남긴 편집 파일입니다.
- `Walk`: 기존 FemaleWalk를 적용한 30fps, 180프레임 검토 동작입니다. 타임라인을 재생하면 됩니다. 자동 실행 Python 핸들러는 필요 없습니다.
- `Bob_*`: 형상과 부착 비교를 위해 같은 폴더에 둔 기존 기본 단발 참고본입니다. 요청 4종 ZIP에는 포함하지 않았습니다.
- `*_Shape.blend`: 리깅 직전의 재생성용 중간 파일입니다. 일반 확인에는 FitReview를 열면 됩니다.

[사선 비교 PNG](RequestedStyles_ThreeQuarter.png) · [정면/측면/후면 PNG](RequestedStyles_AllViews.png)

## 형태와 움직임

단발 반묶음은 턱선 길이를 유지하고 위쪽 머리를 뒤의 짧은 묶음으로 모았습니다. 앞머리 없는 두 모델은 기존 단발의 두상 곡선을 다시 샘플링해 이마가 열린 연속 면을 만들었습니다. 장발은 옆면이 귀 바깥을 돌아 어깨 뒤로 내려갑니다. 양갈래는 기존 포니테일의 연속된 머릿결과 귀 앞 가닥을 재사용하고 꼬리를 좌우에 배치했습니다.

머리와 헤어가 분리되지 않도록 실제 메시 부모와 헤어 Armature가 함께 `FemaleRig.Head`를 따릅니다. 머리 뿌리는 고정하고 아래쪽 가중치에만 감쇠 스프링을 적용했습니다. 정지 중 반복 바람은 넣지 않았습니다. 이번 걷기에서 측정한 최대 추가 이동:

- 단발 반묶음: 묶음 끝 4.687mm, 아래 머리 3.71mm
- 앞머리 없는 장발: 아래 머리 5.31mm
- 앞머리 없는 단발: 아래 머리 3.756mm
- 양갈래: 묶음 끝 14.134mm, 애교머리 4.121mm, 앞머리 0.34mm

이는 Blender에서 기존 걷기 동작으로 계산한 베이크입니다. 새 동작에는 다시 베이크해야 하며 Unity 실시간 흔들림 구현은 포함하지 않습니다.

## 수행한 검증과 남은 사항

- 기존 FemaleWalk 24개 일치 본을 사용해 각 모델 180프레임을 검사했습니다. 두상 전체를 강체 부착한 상태의 정점이 실제 Head 변환을 따르는지 독립적으로 검사했고, 고정 뿌리의 추가 이동은 0입니다.
- FitReview / Editable / Walk를 각각 다시 열어 메시 좌표·면·UV 해시 일치, Armature 연결, 정규화된 가중치 및 헤어 전용 파일의 정지 위치를 확인했습니다.
- 재열기 후 1/13/25/49/73/97/121/180프레임에서 Head에 100% 고정된 실제 메시 정점들을 비교했습니다. 최대 오차는 0.000001m 미만입니다.
- 모든 모델은 삼각형 면으로 저장했습니다. 단발 반묶음, 앞머리 없는 장발, 앞머리 없는 단발은 정지 상태 몸체 교차 쌍이 0입니다.
- 양갈래의 재사용한 포니테일 바탕 메시에는 기존 몸체 교차 31쌍과 경계/중복 위상 문제가 남아 있습니다. 새 꼬리나 흔들림으로 추가된 교차와 구분해 검사 JSON에 기록했습니다. 이를 모든 메시의 무관통/매니폴드 판정으로 해석하면 안 됩니다.
- 걷기 각 프레임의 강체 부착 상태 대비 흔들림으로 추가된 몸체 교차는 0입니다. 정지 후 추가 변위가 0.1mm 아래로 감쇠했습니다.
- 기존 원본 및 컬렉션 .blend 9개의 SHA-256과 여성 플레이어 원본 SHA-256이 유지됐습니다. PNG는 실제 Blender 렌더를 사람이 확인했으며, 비교 시트는 해당 PNG의 크기 조절과 배치만 수행했습니다.
- Unity 플레이 모드, 다른 애니메이션 전체, 옷 종류별 충돌 및 게임용 재질 베이크는 미검증입니다. 원본 머리의 모든 표면 문제를 해결한 최종 엔진용 패키지는 아닙니다.

세부 측정: `*_validation.json`, `*_soft_motion_rig_validation.json`, `*_walk_validation.json`, `saved_files_validation.json`, `requested_styles_summary.json`.

## 변경 파일과 재사용

1. 수정한 기존 파일: 상위 `build_collection.py`와 상위 `README.md`. 제작 함수와 기존 명령행 진입점을 확장했습니다.
2. 신규 파일: 이 폴더의 요청 4종 .blend, PNG, 검사 JSON, 이 문서 및 배포 ZIP. 착용/편집/걷기 확인과 재현을 위한 산출물이며 신규 Python 또는 게임 스크립트는 만들지 않았습니다.
3. 재사용: 기존 단발/포니테일 형상, HairFlow 재질, FemaleBody/FemaleRig.Head, FemaleWalk, `mesh`, `sample`, `mirror_pair`, `leaf`, `add_tail`, `world_bvh`, `setup_stage`, `geometry_signature`, `evaluated_surface`, `pony_soft_rig`, `bake_soft_hair`, `apply_project_walk`.
4. 데이터 흐름: 원본 읽기 → 별도 변형 메시 제작 → 기존 Head 연결과 끝부분 본 가중치 → 실제 걷기에서 작은 관성 움직임 계산 → 별도 파일 저장 → 재열기 검증 → 실제 PNG 렌더. 게임 데이터 흐름 변경 없음.
5. 중복 구현: 게임 공통 기능이나 별도 관리자 클래스 추가 없음. 기존 제작 스크립트에 스타일별 형상 규칙과 검증 경로만 확장했습니다.
6. 영향: 기존 제작 함수의 기본 호출 방식은 유지했습니다. 기존 단발/장발/반묶음/포니테일 .blend와 플레이어 원본, Unity 코드·프리팹·UI·공개 API·저장 데이터는 이번 작업에서 수정하지 않았습니다. 기존 미리보기 인코더 파일도 작업 시작 시 해시와 동일합니다.
7. 검증 범위와 한계는 위 항목 및 JSON에 기록했습니다. 기존 작업 전 파일은 상위 `_History/BeforeFiveStyles_20261105/`, 이번 작업 중간본은 `_History/FiveStyles_Work_20261105/`에 보관했습니다.

## 재생성

컬렉션 폴더에서 기존 제작 스크립트에 다음 옵션을 순서대로 실행합니다. Blender 5.2.1 LTS에서 검사했습니다.

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --addons cycles,io_scene_fbx --python './build_collection.py' -- --variants-build
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --addons cycles --python './build_collection.py' -- --variants-render
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --addons cycles,io_scene_fbx --python './build_collection.py' -- --variants-walk
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --addons cycles --python './build_collection.py' -- --variants-validate
```

각 명령에 `--variant BobHalfUp`, `LongNoBangs`, `BobNoBangs`, `TwinTails` 중 하나를 덧붙이면 해당 모델만 처리합니다. `--variants-validate`를 전체로 실행하면 기본 단발 참고본까지 검사합니다.
