# 두부컴퍼니 음식 아이템 이미지

인벤토리·UI·퀘스트 물품 아이콘용 음식 스프라이트 12종입니다. 화폐 이미지와 같은 로우폴리 톤(좌상단 광원, 따뜻한 저채도 색)으로 맞췄고, 베이커리·분식집·편의점 소재 위주로 골랐습니다.

## 파일

Unity 폴더: `CompanyGame/Assets/Art/Items/Food/` (512 × 512, 투명 배경 PNG)

| 파일 | 음식 | 제안 아이템 ID |
| --- | --- | --- |
| Food_Bread.png | 식빵 | `item_bread` |
| Food_Croissant.png | 크루아상 | `item_croissant` |
| Food_Baguette.png | 바게트 | `item_baguette` |
| Food_RiceBall.png | 삼각김밥 | `item_rice_ball` |
| Food_Kimbap.png | 김밥 | `item_kimbap` |
| Food_Tteokbokki.png | 떡볶이 | `item_tteokbokki` |
| Food_Hamburger.png | 햄버거 | `item_hamburger` |
| Food_Milk.png | 우유 | `item_milk` |
| Food_Tofu.png | 두부 | `item_tofu` |
| Food_Egg.png | 달걀 | `item_egg` |
| Food_Flour.png | 밀가루 | `item_flour` |
| Food_Coffee.png | 커피 | `item_coffee` |

아이템 ID는 `Unity_Script_Implementation_Plan.md`의 `item_flour` 규칙에 맞춘 제안입니다. `ItemData` 에셋이 아직 없어서 연결은 하지 않았습니다.

## 다시 만들기

`ArtSource/Items/Food/generate_food.py`가 원본입니다. 색이나 모양을 바꾸려면 이 스크립트를 고치고 `python generate_food.py`를 실행하세요. (Pillow 필요: `pip install pillow`)

- PNG를 Unity 폴더에 덮어쓰고, 미리보기 `Food_Sheet.png`를 이 폴더에 다시 씁니다.
- 새 PNG에만 `.meta`를 만듭니다. 이미 있는 `.meta`는 GUID 보존을 위해 건드리지 않습니다.

## Unity 가져오기 설정

`.meta`에 Sprite (2D and UI), Single, 중앙 피벗, 100 Pixels Per Unit, Alpha Is Transparency, Bilinear, Clamp, mipmap 끄기, 최대 1024를 지정했습니다(화폐 `.meta`를 기준으로 함). UI에서는 Preserve Aspect를 켜세요.

이미지 크기와 투명 배경, 미리보기 시트에서 각 음식의 모양은 확인했습니다. Unity 에디터에서의 실제 가져오기와 화면 배치는 확인하지 않았습니다. 3D 메시, 프리팹, `ItemData` 연결은 포함하지 않습니다.

## 제작 기록

- 생성 방식: Python(Pillow) 폴리곤 드로잉, 4배 슈퍼샘플링 후 축소.
- 제작일: 2026-09-21.
