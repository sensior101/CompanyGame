"""Reuse the original eight-design sheet, plus a 24-variant comparison sheet."""
from pathlib import Path
import json
from PIL import Image,ImageDraw,ImageFont
HERE=Path(__file__).resolve();ROOT=HERE.parent
original=HERE.parents[5]/'build_overview.py'
rows=json.loads((ROOT/'manifest.json').read_text(encoding='utf8'))['items']
assert len(rows)==24
base_code=original.read_text(encoding='utf8')
for floor in (3,4,5):
    code=base_code.replace("['items']\nassert len(rows)",f"['items']\nrows=[r for r in rows if r['floorCount']=={floor}]\nassert len(rows)")
    code=code.replace('COMPANY GAME  /  LOW-POLY COMMERCIAL BLOCKS',f'COMPANY GAME  /  {floor} STOREYS / 8 DESIGNS')
    description='승인된 3층 원본 유지' if floor==3 else '실제 3.2 m 층 추가'
    code=code.replace('12 × 8 m · 3층 · 무광 단색 재질 · 기존 8종 설계 기반  /  Blender 프리뷰',f'12 × 8 m · {floor}층 · 1층과 전면 간판 유지 · {description} / Blender 프리뷰')
    code=code.replace("row['id']+'  '+row['styleLabel']","row['baseId']+'  '+row['styleLabel']")
    code=code.replace("원본 대비 {row['triangleReductionPercent']:.1f}% 감소","지붕 {row['roofSlabHeight']:.1f} m")
    code=code.replace("ROOT/'Overview.png'",f"ROOT/'Overview_{floor}F.png'")
    exec(compile(code,str(original),'exec'),{'__file__':str(HERE),'__name__':'__main__'})

font=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',18)
title=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',34)
label=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',25)
cell=300;gap=14;header=100;band=60;caption=55
canvas=Image.new('RGB',(8*cell+9*gap,header+3*(band+cell+caption+gap)),(236,233,220))
draw=ImageDraw.Draw(canvas)
draw.text((gap,16),'COMPANY GAME / 24 COMMERCIAL BUILDINGS',font=title,fill=(44,65,58))
draw.text((gap,62),'3층 8종 + 4층 8종 + 5층 8종 / 동일한 12×8m 본체 / 1층·간판 유지 / Blender 프리뷰',font=label,fill=(80,90,75))
for rowindex,floor in enumerate((3,4,5)):
    y=header+rowindex*(band+cell+caption+gap)
    draw.text((gap,y+12),f'{floor}F  /  {floor}층',font=label,fill=(44,65,58))
    variants=sorted((r for r in rows if r['floorCount']==floor),key=lambda r:r['baseId'])
    for col,row in enumerate(variants):
        x=gap+col*(cell+gap)
        im=Image.open(ROOT/row['preview']).convert('RGB').resize((cell,cell),Image.Resampling.LANCZOS)
        canvas.paste(im,(x,y+band))
        draw.text((x,y+band+cell+8),f"{row['baseId']}  ·  {row['triangles']:,} tri",font=font,fill=(44,65,58))
        draw.text((x,y+band+cell+30),f"지붕 {row['roofSlabHeight']:.1f} m",font=font,fill=(80,90,75))
canvas.save(ROOT/'Overview_24.png')
print(ROOT/'Overview_24.png')
