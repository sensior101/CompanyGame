# 두부컴퍼니 화폐 이미지

게임 인벤토리와 UI에 사용할 수 있는 화폐 이미지 5종입니다. 기존 마을의 따뜻한 로우폴리 분위기를 참고하여 입체감 있는 두부 문양과 큰 금액 표기를 넣었습니다.

## 파일

Unity 폴더: `CompanyGame/Assets/Art/Items/Currency/`

| 금액 | 파일 | 디자인 | 원본 크기 |
| --- | --- | --- | --- |
| 100원 | Currency_100.png | 은색 동전 | 1254 × 1254 |
| 500원 | Currency_500.png | 은색 동전 | 1254 × 1254 |
| 1,000원 | Currency_1000.png | 청색 지폐 | 1795 × 876 |
| 5,000원 | Currency_5000.png | 갈색 지폐 | 1774 × 886 |
| 10,000원 | Currency_10000.png | 녹색 지폐 | 1796 × 876 |

모두 실제 알파 채널이 있는 개별 PNG이며, 생성 원본을 재가공 없이 보존했습니다. 원본 크기와 투명 여백이 서로 다르므로 UI에서는 Preserve Aspect를 켜고 표시 크기를 맞추세요.

## Unity 가져오기 설정

각 PNG의 .meta에 Sprite (2D and UI), Single, Full Rect, 중앙 피벗, 100 Pixels Per Unit, Alpha Is Transparency, Bilinear, Clamp, mipmap 끄기, 최대 2048, 기본 비압축 설정을 지정했습니다.

PNG 크기, 투명 영역, 파일 복사 무결성 및 이미지의 금액 표기를 확인했습니다. Unity 에디터에서의 실제 가져오기 및 화면 배치 검증은 수행하지 않았습니다. 이번 결과물은 2D 이미지 에셋이며 3D 메시, 프리팹, 화폐 계산이나 획득 로직은 포함하지 않습니다.

## 제작 기록

- 생성 방식: 내장 image_gen 도구, 금액별 독립 생성 5회.
- 최종 생성 프롬프트: [prompts.json](prompts.json).
- 제작일: 2026-09-21.

