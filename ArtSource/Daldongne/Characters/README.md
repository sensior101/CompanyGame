# 플레이어 모델 소스

## 남녀 손 메시 복구 — 2026-10-30

- 독립된 마감 원통으로 만든 손가락과 부정확한 엄지 위치 때문에 주먹이 네모난 고리처럼 보였습니다. 기존 `prepare_articulated.py`의 손 전용 경로에서 원본 `FemaleRigged.glb` / `MaleRigged.glb`의 연결된 손바닥·엄지 두덩·손가락 표면을 재사용합니다. 실제 끝마디의 연결 영역으로 다섯 손가락을 구분하고, 손 길이·변형 지지면·30개 손가락 본과 가중치만 조정했습니다. 원본 GLB는 읽기 전용입니다.
- 현재 소스는 `MeshyArticulated_20261028/FemaleArticulated.blend`와 `MaleArticulated.blend`입니다. 손 이외의 여성 11,387면 / 남성 11,011면은 실제 메시의 정점·면·재질·UV·가중치 SHA-256이 보정 전과 일치합니다. 기존 24개 본과 전체 54개 본의 이름·부모 관계를 유지합니다. 커스텀 노멀 재인코딩의 최대 단위벡터 오차는 여성 0.000576 미만, 남성 0.000980 미만입니다.
- 삼각형은 여성 16,821개 / 남성 16,892개이며 18,000개 제한 이내입니다. 저장한 Blender 파일을 다시 열어 보존 해시와 24개 본을 검사했습니다. FBX 재임포트에서도 보호 영역 정점 위치·재질·UV·가중치 누락과 불일치는 0개이며, 좌표의 최대 변환 오차는 0.4마이크로미터 미만입니다. `MeshyArticulated_20261028/QA/*-hand-repair.json`과 `HandRepair-saved-file-audit.json`에 실제 결과가 있습니다.
- 재생성은 별도 Blender 백그라운드 프로세스에서 아래 경로를 사용합니다. 기존 작성된 몸·의상을 읽어 유지하고 결과는 지정한 검토 폴더에만 저장합니다. 과거 `--build`는 몸 전체와 이전 원통 손을 덮어쓰던 명령이므로 실행을 거부합니다. 저장소 내 호출부는 없으며, 새 손 복구에는 사용하지 않습니다.

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --python ArtSource\Daldongne\Characters\prepare_articulated.py -- Female Male --stage-hands --output=C:/Users/usercom/AppData/Local/Temp/companygame-hands-review
```

- 편 손·주먹·가리키기·잡기 각각 좌우 손의 등/바닥/측면/사선 총 64개 실제 Blender 렌더를 생성하고 주요 자세를 시각 검토했습니다. 주먹 미리보기의 손가락 관절 회전은 80/100/60도, 엄지는 단순 35/25/20도이며 최종 엄지 맞섬과 게임 속 포즈 검증은 Unity 단계에서 수행합니다. Blender 정적 확인을 게임 검증으로 간주하지 않습니다.
- 공개 에셋 반영은 검토된 `.blend` 2개와 기존 FBX 2개만 교체하고, Unity에서 해당 FBX의 손가락 rest 메타데이터 및 기존 남녀 시각/시민 프리팹 연결을 좁게 동기화합니다. `ReferenceGirlImporter.RebuildArticulated` 전체 실행은 재질과 다른 콘텐츠까지 바꾸므로 이 손 복구의 적용 경로가 아닙니다. 공통 런타임 관리자나 별도 손 전용 런타임 스크립트는 만들지 않습니다.

## 여성 브라 색상 보정 — 2026-10-29 최종 경계 도색

- 원인: 텍스처 기반 면 분류에 이어 적용했던 경계 평활화가 실제 메시의 V자 목선·굽은 끈과 달랐습니다. 옷의 얇은 접힌 측면과 상단 면까지 피부색으로 남아 있었습니다. 해당 분할·평활화 처리를 제거하고 원본 메시의 실제 옷 테두리에 맞춰 도색했습니다.
- 수정한 기존 파일 4개: `prepare_articulated.py`, `MeshyArticulated_20261028/FemaleArticulated.blend`, `CompanyGame/Assets/Art/Daldongne/Players/MeshyFemale/FemaleArticulated.fbx`, 이 README.
- 신규 제작 파일 1개: `MeshyArticulated_20261028/FemaleArticulated.bra-materials.json`. 검토한 면별 색을 재생성 때 그대로 복원하기 위한 아트 데이터입니다. 몸통 영역 4,003면의 최종 재질과 좌표 기반 키·형상 SHA-256을 저장합니다. 원본 메시가 달라지거나 선택 면이 중복되면 적용 전에 오류를 내도록 했습니다. 신규 런타임 스크립트는 없습니다.
- 데이터 흐름: 기존 `solid_materials`의 면 분류 → 기존 `refine_female_bra_boundaries`에서 검토한 면별 색 지정 → 기존 FBX 경로로 내보내기·재임포트. 기존 분류 보정본 대비 앞 목선 122면, 끈 안쪽 122면, 등 윗단 34면, 끈 바깥·겨드랑이 299면, 밑단 73면, 목선 접힌 면 52면, 총 702면의 피부색을 옷 색으로 바꿨습니다.
- 형상 보존: 분할 전 원본 13,630정점·13,927삼각형을 사용합니다. 도색 전후 정점 위치·면 연결·UV·커스텀 노멀·스킨 가중치·54본이 모두 동일합니다. 새 정점·삼각형은 생성하지 않습니다. 원본 `FemaleRigged.glb`는 읽기만 합니다.
- 재사용: `prepare_articulated.solid_materials`, 기존 FBX 내보내기 설정, 기존 Skin/Female 재질, `ReferenceGirlImporter.ValidateArticulated`, `CharacterQA.PreviewSave`, `DaldongneAvatarMotion.ConfigureHumanoid/Pose`. 여성 플레이어와 NPC의 기존 FBX 연결을 유지합니다. 공통 기능 중복 구현, 공개 API·저장·이벤트·게임플레이 데이터 흐름 변경은 없습니다.
- 검증: 재적용 변경 0면. 원본 GLB부터 다시 생성한 몸통 4,003면은 저장본과 좌표·재질이 모두 일치했으며 누락·추가·불일치 0개입니다. Unity에서 13,927삼각형·54본·손가락 관절 30개·높이 1.8m·Humanoid 검사를 통과했습니다. 기존 프리팹의 8방향 확대 및 걷기 두 자세를 Editor 미리보기로 확인했습니다.
- 참조/범위: 여성 FBX `.meta`/GUID, `FemaleVisual.prefab`, `CitizenFemale.prefab`, 남성 FBX의 SHA-256이 보정 전과 동일합니다. Unity에 적용된 FBX는 최종 내보낸 파일과 바이트가 일치합니다. 활성 씬 및 수정 상태도 유지했습니다.
- 검증 자료와 일회성 도구·백업은 Assets 밖 `_temp/female-bra-20261029/rev3/`에 있습니다. `boundary-refinement.json`, `source-regeneration.json`, `after-unity.json`, `final-file-verification.json`과 `after-unity-*.png`를 참고하세요. 이번 도색 보정에서 Play 모드 직접 보행은 실행하지 않았으며, 커밋·푸시는 수행하지 않았습니다.

## 현재 남성 플레이어: Meshy Male 원본 해상도 (2026-10-05)

- `MeshyMaleHigh.fbx`: 제공된 `Meshy_AI_Low_Poly_Character_Mo_1005131415_generate.fbx` 원본입니다. 226,436개 삼각형을 유지하며 높이를 1.8m로 맞춥니다.
- `MeshyMaleHigh.shorts-faces.json`: 원본 반바지의 허리·밑단 경계에 맞춰 선택하고 앞뒤·양옆에서 검토한 면 목록입니다. 원본 SHA-256과 삼각형 수를 검사하며, 회색은 이 면들에만 적용합니다.
- `import_meshy_male.py`: 원본 메시의 연결 관계로 양팔을 분리하고 어깨의 관절 영향을 부드럽게 연결합니다. UV2는 관절 구분과 팔 영향도만 저장하며 색상에는 사용하지 않습니다.
- `MeshyMaleImporter.cs`: 기존 `MaleVisual.prefab` 루트와 GUID를 유지하면서 10개 관절, 연속 스킨 메시, 피부·반바지 재질을 생성합니다. `DaldongneAvatarMotion`, `PlayerHeldItem`, `InventoryCharacterPreview`가 기존 경로로 보행·소지 자세·남성 모델의 오른쪽 아래팔·인벤토리 미리보기를 처리합니다.
- 재생성: Blender에서 `--background --factory-startup --python ArtSource/Daldongne/Characters/import_meshy_male.py`를 실행한 뒤 Unity의 **Tools → Company Game → Characters → Rebuild Meshy Male**을 실행합니다. 기존 **Rebuild Reference Boy** 메뉴도 새 남성 모델을 사용합니다. 런타임 에셋은 `Assets/Art/Daldongne/Players/MeshyMale/`에 있습니다.
- 원본에는 텍스처·뼈대·애니메이션이 없습니다. 새 얼굴이나 머리카락을 추가하지 않고 원본 형상에 기존 플레이어 관절 동작을 연결합니다.

## 현재 여성 플레이어: Meshy Female 원본 해상도 (2026-10-05)

- `MeshyFemaleHigh.fbx`: 제공된 `Meshy_AI_Female_Base_Avatar_Ch_1005111639_generate.fbx` 원본입니다. 모델의 모든 212,746개 삼각형을 유지합니다.
- `MeshyFemale.glb`: 이전에 제공된 부위 분리 원본입니다. 관절의 영역 판별에만 쓰며 런타임 형상으로 사용하지 않습니다.
- `import_meshy_female_high.py`: FBX 형상을 높이 1.8m로 정규화하고 검토된 속옷 면 선택과 관절 영역 정보를 추가합니다. 메시 감축이나 무릎 절단을 하지 않습니다.
- `MeshyFemaleImporter.cs`: 기존 여성 프리팹 루트를 보존하며 팔꿈치를 포함한 10개 관절, 연속 스킨 메시 1개, 피부와 속옷 재질 2개를 생성합니다. 기존 `DaldongneAvatarMotion`이 관절을 구동합니다. 남성 rigid 메시도 같은 스크립트에서 계속 지원합니다.
- 원본에 머리카락, 얼굴 텍스처, 스켈레톤, 애니메이션은 없습니다. 원본 자체의 각진 형상은 유지됩니다. 추가한 스킨 웨이트는 무릎/어깨/팔꿈치/목 전환을 지원하며 손가락 애니메이션은 포함하지 않습니다.
- 여성 보행은 좁은 발 간격과 짧은 보폭, 낮은 발 들기로 설정합니다. `PlayerHeldItem`은 팔꿈치를 접어 아이템을 들고, 1인칭에서는 스킨의 오른쪽 아래팔 영역을 복사한 별도 오브젝트를 기존 손 카메라로 오른쪽 아래에 표시합니다. 빈손에서는 소지 자세를 해제하고, 연속 공격은 현재 동작에서 부드럽게 이어집니다.
- 기존 `FemaleVisual.prefab`의 GUID와 루트는 유지합니다. `Resources/Player.prefab`, `PlayerFemale.prefab`, `PlayerMale.prefab`의 여성 외형 참조에 반영되며 남성 외형은 변경하지 않습니다.
- 재생성: Blender에서 `--background --factory-startup --python ArtSource/Daldongne/Characters/import_meshy_female_high.py`를 실행한 후 Unity의 **Tools → Company Game → Characters → Rebuild Meshy Female**을 실행합니다. **Rebuild Female Character** 메뉴도 현재 모델로 연결됩니다. 런타임 에셋은 `Assets/Art/Daldongne/Players/MeshyFemale/`에 있습니다. 피부와 속옷은 텍스처 혼합 없이 각각 단색 재질을 사용합니다. `MeshyFemaleHigh.underwear-faces.json`은 고해상도 원본의 옷 테두리에 맞춘 면 선택이며 원본 SHA-256으로 일치 여부를 검사합니다. `MeshyFemaleHigh.underwear-contours.json`에는 선택 기준으로 사용한 정투영 윤곽을 보관합니다.

## 이전 모델 삭제 — 2026-11-11

이전 플레이어 모델(`ReferenceGirl`·`ReferenceBoy`의 .blend·.fbx·.glb, `build_female_player.py`, `build_male_player.py`, `build_cute_face.py`, `build_male_hair.py`, Unity의 `ReferenceGirl`·`ReferenceBoy` 폴더)과 흰 반팔 티 작업본(`LowPolyWhiteTee_20261105`)은 삭제했다. 옛 재생성 경로도 코드에서 뺐다. 메뉴는 **Rebuild/Validate Female·Male Character**이며 현재 Meshy 몸만 다룬다. 옷은 `Outfits_20261111/README.md`를 본다.
