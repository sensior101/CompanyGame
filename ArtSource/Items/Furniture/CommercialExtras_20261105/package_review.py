"""Compose rendered Blender previews and write the checked asset catalogue."""
from pathlib import Path
import json, hashlib
from PIL import Image, ImageDraw, ImageFont
OUT=Path(__file__).resolve().parent
rows=json.loads((OUT/'QA/catalog.json').read_text(encoding='utf8'))
report=json.loads((OUT/'QA/validation.json').read_text(encoding='utf8'))
before=json.loads((OUT/'QA/source_hashes_before.json').read_text(encoding='utf-8-sig'))
for file in before:
    assert hashlib.sha256(Path(file['Path']).read_bytes()).hexdigest().upper()==file['Hash'],file['Path']
(OUT/'QA/source_preservation.json').write_text(json.dumps(dict(unchanged=True,checked_files=len(before)),indent=2),encoding='utf8')
font=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',24)
small=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',19)
title=ImageFont.truetype('C:/Windows/Fonts/malgunbd.ttf',36)
for page in range(4):
    subset=rows[page*5:page*5+5];im=Image.new('RGB',(1500,1120),'#f4f2ec');draw=ImageDraw.Draw(im)
    draw.text((32,22),f'상가 가구 추가 19종 · WHITE & WOOD   {page+1}/4',font=title,fill='#343b37')
    for i,row in enumerate(subset):
        x=(i%3)*500;y=100+(i//3)*500
        photo=Image.open(OUT/'Previews'/f"{row['id']}.png").convert('RGB');photo.thumbnail((430,410))
        im.paste(photo,(x+(500-photo.width)//2,y))
        draw.text((x+35,y+415),f"{page*5+i+1:02d}  {row['label']}",font=font,fill='#343b37')
        dims=' × '.join(f'{v*100:.1f}' for v in row['dimensions'])+' cm'
        draw.text((x+35,y+451),dims,font=small,fill='#646d66')
    im.save(OUT/'Previews'/f'ContactSheet_{page+1:02d}.jpg',quality=94)
overview=Image.new('RGB',(1500,1540),'#f4f2ec');draw=ImageDraw.Draw(overview)
draw.text((30,20),'상가 가구 추가 19종 · WHITE & WOOD',font=title,fill='#343b37')
for i,row in enumerate(rows):
    x=i%5*300;y=90+i//5*355
    photo=Image.open(OUT/'Previews'/f"{row['id']}.png").convert('RGB');photo.thumbnail((280,280))
    overview.paste(photo,(x+10,y))
    draw.text((x+12,y+285),row['label'],font=small,fill='#343b37')
    draw.text((x+12,y+313),' × '.join(f'{v*100:.0f}' for v in row['dimensions'])+' cm',font=small,fill='#646d66')
overview.save(OUT/'Previews/Overview.jpg',quality=95)
lines=['# 상가 추가 가구 19종 — 화이트앤우드','',
'기존 가구와 같은 미터 단위, Z 위쪽, -Y 정면. 상가 내부 26 × 16.4m / 천장 3.3m 기준이다. 빨간 플라스틱 의자는 요청한 빨간색이며, 업소용 기기는 위생 작업면과 손잡이에 금속을 사용한다.','',
'- `Models/`: 19개 개별 Blender 원본. 각 파일의 `ID_ASSET` 컬렉션에 모델만 들어 있다. 카메라·조명·바닥은 별도 프레젠테이션 요소다.',
'- `CommercialExtras_WhiteWood_RoomScale.blend`: 창문 없는 기존 상가에서 크기를 확인하는 배치본. 전화기는 기존 74cm 책상 위에 놓았다. 야외 가구도 크기 비교를 위해 이 배치본에 포함했다.',
'- `Previews/`: 품목별 렌더, 4장 미리보기, 상가 배치 렌더. 품목별 미리보기는 화면에 맞춘 배율이며, 실제 상대 크기는 상가 배치본에서 확인한다.',
'- 바닥·탁상 모델은 아래쪽 Z=0, 벽 메뉴판은 뒤쪽 Y=0 / 아래쪽 Z=0, 천장 번호판은 천장 접점 Z=0에서 아래로 내려온다.',
'- 메뉴판·입간판의 흰 면은 별도 UV와 재질을 갖춘 이미지 교체용 면이다. 번호판은 01 기본 표시. 기능 연결은 포함하지 않는다.','',
'## 제작 및 검증','',
'1. 기존 파일 수정 없음. 기존 제작 라이브러리·상가·가구 파일의 SHA-256을 비교해 보존 확인.',
'2. 이 폴더에 모델, 미리보기, QA, recipes.py, build_extras.py, package_review.py를 새로 생성했다. recipes는 추가 품목 형상, build는 기존 제작·카메라 도구 연결 및 검증, package는 검토용 이미지와 문서를 담당한다.',
'3. HouseholdLowPoly의 box/cyl/rod/lathe/rounded_loft/cushion/basin/faucet/handle/init/prepare, CommercialLowPoly의 bounds/camera_scene 재사용.',
'4. 기존 치수·형상 도구 → 추가 품목 조합·재질 → 개별 Blender 저장 → 파일 재개방 검사 → 기존 상가 크기 배치와 렌더.',
'5. 공통 형상 생성·게임 공통 로직의 중복 구현 없음.',
'6. Unity 스크립트·공개 API·저장 데이터·이벤트·호출부 변경 없음. Blender 제작 산출물만 추가.',
'7. 19개 파일 재개방, 치수, 원점, 삼각형 수, UV·재질, 비정상 모서리·퇴화 면, 상가 범위 검사를 통과했다. Unity 반입·상호작용·플레이 검증은 수행하지 않았다.','',
'## 품목','',
'| 품목 | 원본 | 너비 × 깊이 × 높이(cm) | 삼각형 | 설치 |','|---|---|---|---:|---|']
for row in rows:
    dims=' × '.join(f'{v*100:.1f}' for v in row['dimensions'])
    lines.append(f"| {row['label']} | [Blender](Models/{row['id']}.blend) | {dims} | {row['triangles']} | {row['placement']} |")
(OUT/'README.md').write_text('\n'.join(lines)+'\n',encoding='utf8')
print('PACKAGED',len(rows),'assets; originals preserved',len(before))
