"""Build a labeled contact sheet of the actual Blender asset renders (requires Pillow)."""
from pathlib import Path
import json
from PIL import Image, ImageDraw, ImageFont

ROOT=Path(__file__).resolve().parent
rows=json.loads((ROOT/'manifest.json').read_text(encoding='utf8'))['items']
assert len(rows)==8, 'Generate all eight buildings first.'
fontpath='C:/Windows/Fonts/malgun.ttf'
font=ImageFont.truetype(fontpath,22)
small=ImageFont.truetype(fontpath,16)
title=ImageFont.truetype(fontpath,32)
cell=470;gap=18;top=108;bottom=85
canvas=Image.new('RGB',(4*cell+5*gap,top+2*(cell+bottom)+3*gap),(236,233,220))
draw=ImageDraw.Draw(canvas)
draw.text((gap,18),'COMPANY GAME  /  LOW-POLY COMMERCIAL BLOCKS',font=title,fill=(44,65,58))
draw.text((gap,65),'12 × 8 m · 3층 · 무광 단색 재질 · 기존 8종 설계 기반  /  Blender 프리뷰',font=font,fill=(80,90,75))
for index,row in enumerate(rows):
    x=gap+(index%4)*(cell+gap);y=top+gap+(index//4)*(cell+bottom+gap)
    im=Image.open(ROOT/row['preview']).convert('RGB').resize((cell,cell),Image.Resampling.LANCZOS)
    canvas.paste(im,(x,y))
    draw.text((x,y+cell+11),row['id']+'  '+row['styleLabel'],font=font,fill=(44,65,58))
    draw.text((x,y+cell+45),f"{row['triangles']:,} triangles  ·  원본 대비 {row['triangleReductionPercent']:.1f}% 감소",font=small,fill=(80,90,75))
canvas.save(ROOT/'Overview.png')
print(ROOT/'Overview.png')
