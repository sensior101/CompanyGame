# 기본 옷 (반팔 티 + 긴바지) — 2026-11-11

지금 게임 아바타(Meshy 관절형 남녀 몸)에 입히는 기본 옷이다. 옷이 몸과 같은 뼈대에 스키닝되어 있어서, 걷기·앉기·팔 들기 등 어떤 자세에서도 몸을 따라 함께 휜다.

## 만드는 방식 (`build_outfits.py`)
1. Unity가 쓰는 몸 파일(`Players/Meshy<성별>/<성별>Articulated.fbx`)을 그대로 불러온다.
2. 몸 표면을 평면으로 잘라 옷 모양을 만든다. 상의는 목둘레(타원), 소매(위팔 절반), 밑단(골반 아래)으로, 바지는 허리와 발목으로 자른다.
3. 피부에서 1~2 cm 띄우고, 천처럼 매끄럽게 다듬는다. 바지는 몸에 붙은 속옷 경계가 비치지 않도록 무릎 아래를 일자로 편다.
4. 두께 2.5 mm를 준다.
5. 각 옷 정점은 원래 있던 피부 자리의 뼈 가중치를 그대로 쓴다. 정점당 뼈는 4개까지다.
6. 옷이 덮는 피부를 뺀 몸(`<성별>BodyMasked`)도 함께 만든다. 움직여도 피부나 속옷이 옷 밖으로 비치지 않는다. 소매·밑단 근처 3 cm는 남긴다.

산출물:
- `<성별>BasicOutfit.blend`: 편집용. 몸이 함께 들어 있다.
- `CompanyGame/Assets/Art/Daldongne/Players/Outfits/<성별>BasicOutfit.fbx`: 뼈대, Top, Bottom, BodyMasked.
- `Renders/`: Blender에서 본 4가지 자세.
- `<성별>-fit.json`: Blender에서 잰 수치.

## Unity
- `CharacterOutfitBuilder.Build`가 FBX를 `CharacterOutfit` 에셋(`Outfits/<성별>BasicOutfit.asset`)으로 만들고, `MaleVisual`·`FemaleVisual` 프리팹에 `AvatarOutfit`을 붙인다. 옷 메시는 몸 메시와 같은 공간, 같은 바인드 포즈로 맞춘다(차이 0.00005 이하).
- 플레이어, 원격 플레이어, 시민, 고시원 주인, 한대건설·부동산 직원은 모두 이 프리팹에서 나오므로 같은 옷을 입는다. 시민은 셔츠·바지 색이 사람마다 다르다(`CitizenInfo.Dress`). 부동산 직원은 흰 셔츠(안내 직원은 하늘색)에 짙은 회색 바지다.
- 1인칭에서는 몸과 함께 옷도 숨는다. 1인칭 팔에는 소매가 같이 나온다.

## 다시 만들기
```
blender -b --factory-startup --python ArtSource/Daldongne/Characters/Outfits_20261111/build_outfits.py
```
그다음 Unity를 닫고 다음을 차례로 실행한다:
- `-executeMethod CompanyGame.Editor.Characters.CharacterOutfitBuilder.Build -quit`
- `CompanyGame.Editor.Citizens.CitizenPrefabBuilder.Build`: 시민 프리팹이 아바타 복사본이라 다시 만들어야 한다.
- `RealEstateInteriorBuilder.Build`

## 검증
- `UnityQA/unity_outfit_qa.txt`: Unity의 실제 뼈로 편 자세, 걷기, 앉기, 팔 들기를 만들고 옷과 피부 사이 거리를 쟀다. 중간값 1.3~1.9 cm, 최대 5.8 cm로 옷이 몸을 따라간다.
- `UnityQA/*_UnitySheet.png`: 같은 자세를 Unity에서 렌더한 화면.
- 부동산 사무실 플레이 검사(`RealEstateInterior_20261107/QA/22·23`)에서 직원이 옷을 입고 앉아 있다.

## 한계
- 신발과 머리카락은 없다. 맨발이고 민머리다.
- 천 시뮬레이션이 아니다. 주름이 생기지 않고, 옷이 피부와 똑같이 휜다.
- 바지 표면이 조금 울퉁불퉁하다. 남자 바지는 무릎 위에 옷이 살짝 넓어지는 선이 남아 있다.
- 바지에 바늘구멍만 한 틈 몇 개가 보인다.
- 옷을 갈아입는 아이템과 기능은 아직 없다. 지금은 모두 같은 기본 옷을 입는다.
