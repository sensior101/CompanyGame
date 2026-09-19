# 사진 참고 여자 플레이어

제공된 정면·후면·측면·사선 참고 이미지의 큰 머리 비율, 두꺼운 앞머리, 야구모자, 긴 머리와 리본, 오버핏 재킷, 치마, 레이스업 부츠를 반영한 로우폴리 모델입니다. 무채색 참고에 마을용 크림·밤색·세이지 팔레트를 적용했습니다. 양쪽 귀 주변에는 머리카락을 한 가닥씩 추가했습니다.

최신 장난감 캐릭터 얼굴 참고를 반영해 큰 원형 눈과 짧고 둥근 얼굴로 다시 구성했습니다. 짙은 갈색 홍채와 동공의 상대 위치를 양쪽에 동일하게 적용하고, 아이보리색 흰자와 작은 반사광을 더했습니다. 눈은 얼굴 표면을 따르는 얕은 곡면으로 만들어 측면에서 안구가 튀어나오지 않도록 했습니다. 부드러운 위 눈꺼풀 윤곽과 눈마다 짧은 속눈썹 두 가닥을 사용하며, 홍채·동공·흰자는 경계를 공유하는 면으로 구성합니다.

살구색 버튼 코, 작은 ∧ 모양 입, 둥근 볼과 짧은 턱, 옅은 홍조를 적용했습니다. 하관의 너비와 깊이를 부드러운 곡선으로 연결해 각진 턱과 수평 그림자 띠를 줄였습니다. 이전의 사실적인 쌍꺼풀, 도톰한 입술, 입 주변 S곡선은 이 귀여운 얼굴 구성으로 대체했습니다. 모자·의상·기존 머리카락과 양쪽 옆머리 안쪽의 빈틈 보강은 유지합니다.

## 파일

- `ReferenceGirl.blend`: 이름이 붙은 236개 파츠와 9개 관절 피벗을 편집할 수 있는 Blender 원본. `STUDIO - preview only` 컬렉션은 촬영용입니다.
- `build_female_player.py`: 전체 모델 생성 진입점. `build_cute_face.py`는 여기에서 실행되는 얼굴 전용 생성 모듈입니다.
- `ReferenceGirl.fbx`, `ReferenceGirl.glb`: 외부 도구용 모델. 메시와 관절 계층만 내보냈으며 촬영용 바닥·카메라·조명은 포함하지 않습니다.
- `ReferenceGirl_Hero.png`, `ReferenceGirl_Front.png`, `ReferenceGirl_Back.png`, `ReferenceGirl_Side.png`: Blender 렌더. `ReferenceGirl_Face.png`, `ReferenceGirl_FaceThreeQuarter.png`, `ReferenceGirl_FaceOblique.png`는 얼굴 확대 렌더입니다. `ReferenceGirl_ProfileStudy.png`는 머리카락을 일시적으로 숨겨 얼굴 실루엣을 검사한 이미지입니다.
- `Unity_*.png`: 실제 Unity URP 렌더 및 플레이 확인 이미지. `Unity_FaceFront`, `Unity_EarLeft`, `Unity_EarRight`는 얼굴과 양쪽 귀 주변 확대 확인용입니다. `Unity_FaceSoft`, `Unity_FaceThreeQuarterSoft`는 얼굴 형상을 보기 쉽게 촬영용 정면 보조광을 추가한 결과이며 게임 씬의 조명은 변경하지 않습니다.
- `Unity_WalkCycle.gif`, `Unity_RunCycle.gif`: Unity에서 촬영한 사선·측면 보행 미리보기. `MotionPreview`에는 24fps의 48프레임 원본이 있고 GIF는 반복 이음새를 줄이기 위해 완성된 주기에 가까운 구간을 사용합니다.
- `unity_asset_validation.json`, `scene_validation.json`, `motion_validation.json`, `runtime_motion_validation.json`: Unity 에셋, 씬 연결 및 보행 검사 결과.

이전 플레이어 통합 단계에서 Play 모드의 여자/남자 각 37개 경로를 양방향 이동한 총 148회 검사와 외형·카메라 전환을 통과했습니다. 당시 이동 결과는 상위 폴더의 `players_validation.json`에 있으며, 이번 얼굴 수정에서 새로 수행한 경로 검사 결과는 아닙니다. 현재 얼굴 모델의 에셋 검사 결과는 `unity_asset_validation.json`, 시각 확인 결과는 최신 Unity 얼굴 미리보기에서 확인할 수 있습니다.

## Unity 연결

`CompanyGame/Assets/Art/Daldongne/Players/FemaleVisual.prefab`을 기존 GUID와 루트 참조를 유지하며 갱신합니다. `PlayerFemale.prefab`과 `PlayerMale.prefab`의 여자 외형 선택에 반영됩니다. 마을, 작은 정원, 작은 맵 템플릿의 기존 여자 외형도 이 프리팹을 참조합니다.

- 18,862 삼각형, 높이 약 1.8m.
- Unity에서는 9개 관절별 메시와 URP 재질 1개, 256×16 팔레트 텍스처 1개를 사용합니다.
- 이동·충돌·카메라·성별 전환은 기존 `DaldongneVillageWalker` / `DaldongnePlayerAppearance`를 사용합니다.
- `DaldongneAvatarMotion`은 0.767m Hips를 포함한 원래 관절 자세를 보존하며 발을 딛는 구간과 옮기는 구간을 구분합니다. 두 관절 계산으로 무릎을 접고, 골반의 체중 이동과 팔의 반대 흔들림, 머리 회전 보정, 속도 전환 완충을 적용합니다. 부츠의 실제 외곽을 이용해 바닥 관통을 보정합니다.
- 최신 달리기 수정은 3.1m/s 초과에만 적용되어 확인된 걷기 동작을 유지합니다. 4.5m/s 달리기는 접지 비율 41.5%, 짧은 양발 공중 구간, 뒤꿈치를 먼저 접는 회복 궤적, 착지 무릎 완충과 상체 전경을 사용합니다.
- 기존 9개 관절을 사용하는 절차적 동작입니다. 발목·팔꿈치 관절과 Humanoid 스킨 웨이트, Animator 클립은 없으며 발의 월드 좌표 고정이나 경사면 IK는 포함하지 않습니다. 게임 이동 속도는 유지했기 때문에 빠른 이동에서는 발 미끄러짐이 일부 남을 수 있습니다.
- 여자 외형에는 충돌체가 없습니다. 플레이어 루트의 기존 CharacterController를 사용합니다.

## 수정과 재생성

Blender 5.2에서 새 백그라운드 프로세스로 실행합니다. 스크립트는 실행한 Blender 장면을 비우므로 작업 중인 파일에서 실행하지 않습니다.

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --python ArtSource\Daldongne\Characters\build_female_player.py
```

렌더 생략 시 마지막에 `-- --no-render`를 추가합니다. `build_female_player.py`의 `COLORS`와 몸·의상 파츠 좌표, `build_cute_face.py`의 얼굴 좌표를 바꾸고 전체 생성 진입점을 실행하면 모델, 소스 포맷, Unity용 `ReferenceGirl.meshdata.json`을 함께 갱신합니다. 얼굴 모듈은 단독 실행하지 않습니다. `.blend`만 수동 수정한 경우 JSON에 자동 반영되지 않습니다.

Unity에서 씬 변경을 저장한 후 **Tools → Company Game → Characters → Rebuild Reference Girl**을 실행합니다. JSON의 위치와 노멀을 유지하며 관절별 메시를 만들고, 팔레트 UV를 설정합니다. 재생성 시 에셋 GUID를 보존합니다. **Validate Reference Girl**로 에셋 참조를 확인할 수 있습니다.

`CompanyGame/AgentScripts/ReferenceGirlQA.cs`는 Unity Pipeline의 `run_script`로 실행하는 검증·촬영 도구입니다. 일반 실행에는 필요하지 않습니다. 기존 `DaldongnePlayersBuild`도 새 여자 모델을 보존하도록 수정했습니다.

`AvatarMotionQA.Validate`는 여자·남자와 4가지 속도를 총 4,800프레임 검사하며 걷기의 접지와 달리기의 접지·공중 구간을 구분합니다. `AvatarMotionQA.RuntimeLifecycle`은 Play 모드의 비활성화·재활성화와 순간이동 후 자세 복원을 별도로 검사합니다. `ReferenceGirlMotionPreview.Capture`로 걷기·달리기 프레임을 촬영한 뒤 Pillow가 설치된 Python으로 `encode_motion_preview.py`를 실행하면 GIF와 포즈 모음 이미지를 갱신합니다.
