"""Label Blender comparison renders and document the four material variants."""
from pathlib import Path
import json
from PIL import Image,ImageDraw,ImageFont
BASE=Path(__file__).resolve().parent;OUT=BASE/'MaterialVariants'
entries=json.loads((BASE/'QA/catalog.json').read_text(encoding='utf8'))
report=json.loads((OUT/'QA/validation.json').read_text(encoding='utf8'))
styles={'WhiteWood':'화이트앤우드','WhiteBlack':'화이트앤블랙','GlossBlack':'광택 블랙 금속','MarbleGold':'대리석앤골드'}
font=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',25)
small=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',21)
bold=ImageFont.truetype('C:/Windows/Fonts/malgunbd.ttf',34)
for page in range(4):
    photo=Image.open(OUT/'Previews'/f'Compare_{page+1:02d}.png').convert('RGB')
    im=Image.new('RGB',(2220,1750),'#f4f2ed');im.paste(photo,(220,120));d=ImageDraw.Draw(im)
    d.text((24,20),f'상가 추가 가구 · 4가지 테마 비교  {page+1}/4',font=bold,fill='#303833')
    for i,e in enumerate(entries[page*5:page*5+5]):
        center=220+1000+(i*2.9-5.8)/16.6*2000
        d.text((center,85),e['label'],font=font,fill='#303833',anchor='mm')
    for row,(key,label) in enumerate(styles.items()):
        yy=120+800-(.817*(5.25-row*3.5)+.576*.2)/13.28*1600
        d.text((14,yy-15),label,font=font,fill='#303833')
    d.text((24,1720),'형상·실제 치수 동일 / 비교 화면에서만 품목별 배율 조정',font=small,fill='#59615c')
    im.save(OUT/'Previews'/f'ThemeComparison_{page+1:02d}.jpg',quality=95)
lines=['# 상가 추가 가구 19종 — 4개 테마','',
'19종 × 4테마 = 개별 Blender 76개 + 테마별 상가 배치본 4개. 원본의 형상·UV·크기·원점은 유지하고 재질만 변경했다.','',
'| 테마 | 상가 배치본 | 개별 모델 폴더 |','|---|---|---|']
for key,label in styles.items():lines.append(f'| {label} | [Blender]({key}/CommercialExtras_{key}.blend) | [{key}/Models]({key}/Models/) |')
lines+=['','## 비교 미리보기','']
for i in range(4):
    names=', '.join(e['label'] for e in entries[i*5:i*5+5])
    lines.append(f'- [{names}](Previews/ThemeComparison_{i+1:02d}.jpg)')
lines+=['','## 재질 규칙','',
'- 기존 상가 가구의 surface/material/style_material/make_style_textures를 재사용. 목재와 대리석 텍스처는 Blender 파일 안에 포함했다.',
'- 화이트앤우드는 밝은 본체와 오크, 화이트앤블랙은 밝은 본체와 검은 프레임, 광택 블랙은 금속성 0.94의 유광 표면, 대리석앤골드는 대리석 판재와 금색 프레임이다.',
'- 빈백·미용실 쿠션·파라솔 천은 비금속 재질이다. 블랙 좌석은 가죽 느낌으로 처리했다. 유리·화면·고무·표시등은 기능상 식별을 유지한다.',
'- 빨간 플라스틱 의자는 화이트앤우드에서 빨강을 유지하고 다른 세 테마에서는 테마 색으로 바뀐다.',
'- 메뉴판·입간판의 ImageUV와 순백색 교체 면을 유지했다. 천장 번호판은 01 기본 표시다.','',
'## 완료 보고','',
'1. 기존 파일 수정 없음. 기본 19종·배치본 및 앞서 제작한 45종의 테마 파일을 해시로 비교해 보존했다.',
'2. build_styles.py는 추가 품목의 재질 예외와 기존 테마 도구 연결을, package_styles.py는 비교 이미지 라벨과 문서 생성을 담당한다. 새 산출물은 MaterialVariants 아래에 저장한다.',
'3. CommercialLowPoly/build_styles.py의 apply_style/material/save/configure_render/reflection_lights, HouseholdLowPoly의 style_surface/style_material/make_style_textures/material_geometry_signature 재사용.',
'4. 기본 모델 읽기 → 기존 테마와 추가 품목별 재질 규칙 적용 → 텍스처 포함 저장 → 4개 상가 배치본·비교 렌더 → 재개방 검증.',
'5. 공통 메시·재질 생성 기능의 중복 구현 없음. 게임 공통 기능 추가 없음.',
'6. Unity API·저장 데이터·이벤트·호출부 변경 없음. 게임 반입과 기능 연결은 수행하지 않았다.',
f"7. 개별 {report['validated_models']}개와 배치본 {report['validated_rooms']}개 검증. 형상·UV·변환 유지, 텍스처 포함, 흰 이미지 면, 블랙 금속 및 천의 비금속 속성, 상가 범위 확인. 원본 {report['source_files_unchanged']}개 해시 일치. Unity 외형·플레이는 미검증.",
'','## 파일 규칙','','개별 파일은 `테마/Models/품목ID_테마.blend` 형식이다. 카메라·조명·스튜디오 바닥과 분리된 `품목ID_ASSET` 컬렉션이 실제 모델이다.']
(OUT/'README.md').write_text('\n'.join(lines)+'\n',encoding='utf8')
print('PACKAGED 76 variants, 4 rooms, 4 comparison sheets')
