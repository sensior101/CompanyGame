"""Build offline preview catalogue and contact sheets from actual Blender renders."""
import json,html,sys
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
HERE=Path(__file__).resolve().parent

def material_catalog():
 base=HERE/'MaterialVariants';styles={'WhiteWood':'화이트앤우드','Eclectic':'유니크 컬러','MarbleGold':'대리석 + 금','ChicBlack':'시크 블랙'}
 requested=json.loads((HERE/'RequestedCatalog.json').read_text(encoding='utf8'))
 rows=[];font=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',22);small=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',17)
 for entry in requested:
  variants={s:json.loads((base/s/entry['id']/'Manifest.json').read_text(encoding='utf8')) for s in styles}
  rows.append({**entry,'triangles':variants['WhiteWood']['triangles'],'dimensions':variants['WhiteWood']['dimensions'],'styles':list(styles)})
 for style,label in styles.items():
  canvas=Image.new('RGB',(1680,3280),'#efede8');draw=ImageDraw.Draw(canvas);draw.text((22,24),label+' / 가정용 가구 60종',font=font,fill='#242c30')
  for i,e in enumerate(rows):
   x=(i%6)*280;y=80+(i//6)*320
   canvas.paste(Image.open(base/style/e['id']/'Preview.png').convert('RGB').resize((280,280),Image.Resampling.LANCZOS),(x,y));draw.text((x+10,y+282),e['label'],font=small,fill='#30352f')
  canvas.save(base/('Overview_'+style+'.jpg'),quality=93)
 selected=['Vanity','Armchair','GamingChair','IslandTable'];canvas=Image.new('RGB',(1600,1760),'#efede8');draw=ImageDraw.Draw(canvas)
 for j,id in enumerate(selected):
  for i,(style,label) in enumerate(styles.items()):
   x=i*400;y=j*440;draw.text((x+15,y+10),label,font=font,fill='#242c30')
   canvas.paste(Image.open(base/style/id/'Preview.png').convert('RGB').resize((400,400),Image.Resampling.LANCZOS),(x,y+40))
 canvas.save(base/'StyleComparison.jpg',quality=95)
 short=canvas.crop((0,880,1600,1760));short.save(base/'StyleComparisonCompact.jpg',quality=95)
 (base/'Catalog.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2),encoding='utf8')
 data=json.dumps(rows,ensure_ascii=False).replace('</','<\\/')
 page='''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>가구 재질 컬렉션 · 4 Styles</title>
<style>*{box-sizing:border-box}body{margin:0;background:#eeeae3;color:#242b2b;font:15px system-ui,sans-serif}header{background:#233b37;color:#f6f1e6;padding:42px 5vw}h1{font-size:34px;margin:8px 0}header p{line-height:1.7;opacity:.85}.eyebrow{letter-spacing:3px;font-size:12px}nav{position:sticky;top:0;background:#eeeae3f5;padding:16px 5vw;display:flex;gap:10px;flex-wrap:wrap;z-index:2;box-shadow:0 3px 12px #0001}button,input,select{font:inherit;border:1px solid #bdbfb7;background:#fff;border-radius:7px;padding:10px 14px}button{cursor:pointer}button.active{background:#233b37;color:#fff}#count{align-self:center;color:#5b6660}main{padding:16px 5vw 60px;display:grid;grid-template-columns:repeat(auto-fill,minmax(280px,1fr));gap:22px}article{background:#fff;border-radius:12px;overflow:hidden}article img{width:100%;aspect-ratio:1;display:block}.body{padding:18px}h2{font-size:18px;margin:0 0 8px}.meta{font-size:13px;color:#5d6962;line-height:1.6}a{display:inline-block;margin:12px 15px 0 0;color:#285f51}.caption{padding:0 5vw;color:#52645a;line-height:1.6}</style>
<header><div class="eyebrow">HOUSEHOLD / MATERIAL COLLECTION</div><h1>같은 가구, 네 가지 분위기</h1><p>60종 × 4 스타일 · 총 240개 재질 버전<br>화이트앤우드 / 유니크 컬러 / 대리석 + 금 / 시크 블랙</p></header>
<nav><div id="styles"></div><input id="search" placeholder="가구 이름 검색" aria-label="가구 이름 검색"><select id="category" aria-label="가구 분류"><option value="">전체 분류</option></select><span id="count"></span></nav>
<p class="caption" id="description"></p><main id="grid"></main>
<script>const items=DATA;const styles={WhiteWood:'화이트앤우드',Eclectic:'유니크 컬러',MarbleGold:'대리석 + 금',ChicBlack:'시크 블랙'};const descriptions={WhiteWood:'따뜻한 화이트와 내추럴 오크. 패브릭은 아이보리와 베이지.',Eclectic:'틸, 코랄, 머스터드와 딥퍼플의 색 조합.',MarbleGold:'대리석 표면과 금속 골드. 패브릭은 아이보리와 딥그린.',ChicBlack:'블랙 오크, 차콜 패브릭, 건메탈의 차분한 조합.'};let style='WhiteWood';const cat=document.querySelector('#category');[...new Set(items.map(x=>x.category))].forEach(x=>{let o=document.createElement('option');o.value=x;o.textContent=x;cat.append(o)});Object.entries(styles).forEach(([k,v])=>{let b=document.createElement('button');b.textContent=v;b.dataset.style=k;b.onclick=()=>{style=k;render()};document.querySelector('#styles').append(b)});
function render(){const q=document.querySelector('#search').value.toLowerCase(),shown=items.filter(x=>(!cat.value||x.category===cat.value)&&(x.label+' '+x.id).toLowerCase().includes(q));document.querySelector('#count').textContent=shown.length+'종';document.querySelector('#description').textContent=descriptions[style];document.querySelectorAll('[data-style]').forEach(b=>{b.classList.toggle('active',b.dataset.style===style);b.setAttribute('aria-pressed',b.dataset.style===style)});document.querySelector('#grid').innerHTML=shown.map(x=>`<article><img loading="lazy" src="${style}/${x.id}/Preview.png" alt="${x.label} ${styles[style]}"><div class="body"><h2>${x.label}</h2><div class="meta">${styles[style]} · ${x.category}<br>폭 ${x.dimensions[0].toFixed(2)} × 깊이 ${x.dimensions[1].toFixed(2)} × 높이 ${x.dimensions[2].toFixed(2)} m</div><a href="${style}/${x.id}/${x.id}_${style}.blend">Blender</a><a href="${style}/${x.id}/${x.id}_${style}.fbx">FBX</a><a href="${style}/${x.id}/${x.id}_${style}.glb">GLB</a></div></article>`).join('')};document.querySelector('#search').oninput=render;cat.onchange=render;render();</script></html>'''.replace('DATA',data)
 (base/'Catalog.html').write_text(page,encoding='utf8');print('MATERIAL_CATALOG_READY',len(rows)*4)

if '--material-variants' in sys.argv:
 material_catalog();sys.exit(0)

requested=json.loads((HERE/'RequestedCatalog.json').read_text(encoding='utf8'))
rows=[json.loads((HERE/'QA'/e['id']/'Manifest.json').read_text(encoding='utf8')) for e in requested]
for e in rows:
 strip=HERE/'QA'/e['id']/'Palettes.png'
 if strip.exists() and not (strip.parent/'Cream.png').exists():
  im=Image.open(strip)
  im.crop((0,0,540,540)).save(strip.parent/'Cream.png')
(HERE/'Catalog.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2),encoding='utf8')
font=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',22)
small=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',16)
groups={}
for e in rows:groups.setdefault(e['category'],[]).append(e)
for group,items in groups.items():
 cols=4;w=360;h=405
 canvas=Image.new('RGB',(cols*w,80+((len(items)+cols-1)//cols)*h),'#eeedea')
 draw=ImageDraw.Draw(canvas);draw.text((20,22),group+' / Household Low Poly',font=font,fill='#30352f')
 for i,e in enumerate(items):
  x=(i%cols)*w;y=80+(i//cols)*h
  im=Image.open(HERE/'QA'/e['id']/'Cream.png').convert('RGB').resize((w,w),Image.Resampling.LANCZOS)
  canvas.paste(im,(x,y));draw.text((x+12,y+357),e['label'],font=font,fill='#30352f')
  draw.text((x+12,y+383),str(e['triangles'])+' tris',font=small,fill='#686e68')
 canvas.save(HERE/'QA'/('Overview_'+group+'.jpg'),quality=92)
# One full collection sheet; labels remain readable at the saved full resolution.
w=280;h=320;cols=6
canvas=Image.new('RGB',(cols*w,80+((len(rows)+cols-1)//cols)*h),'#eeedea');draw=ImageDraw.Draw(canvas)
draw.text((20,22),'가정용 가구 60종 · 모델링 형태 검토 · Blender 제작',font=font,fill='#30352f')
for i,e in enumerate(rows):
 x=(i%cols)*w;y=80+(i//cols)*h
 canvas.paste(Image.open(HERE/'QA'/e['id']/'Cream.png').convert('RGB').resize((w,w),Image.Resampling.LANCZOS),(x,y))
 draw.text((x+10,y+279),e['label'],font=small,fill='#30352f')
canvas.save(HERE/'QA/AllFurniture.jpg',quality=93)
data=json.dumps(rows,ensure_ascii=False).replace('</','<\\/')
page='''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>가정용 가구 컬렉션</title>
<style>*{box-sizing:border-box}body{margin:0;background:#f2f0eb;color:#27332d;font:15px system-ui,sans-serif}header{padding:38px 5vw;background:#253d33;color:#fff}h1{font-size:30px;margin:0 0 12px}header p{opacity:.8}nav{position:sticky;top:0;background:#f2f0ebed;padding:16px 5vw;display:flex;gap:12px;flex-wrap:wrap;z-index:2}input,select,button{font:inherit;padding:9px 14px;border:1px solid #c9cfc8;border-radius:8px;background:#fff}button{cursor:pointer}button.active{background:#253d33;color:white}main{padding:10px 5vw 60px;display:grid;grid-template-columns:repeat(auto-fill,minmax(260px,1fr));gap:22px}article{background:#fff;border-radius:12px;overflow:hidden}article img{display:block;width:100%;aspect-ratio:1}article .body{padding:16px}h2{font-size:18px;margin:0 0 8px}.meta{color:#667269;font-size:13px}a{display:inline-block;margin:12px 10px 0 0;color:#2a6650}.note{font-size:12px;color:#667269}#count{align-self:center}</style>
<header><h1>가정용 가구 모델링</h1><p>요청 품목 59종 + 싱크대 타공 하부장 1종 / 1.8m 캐릭터 기준 / 기본 재질로 형태 검토</p><p>각 항목의 이미지는 실제 Blender 렌더입니다. 색상 작업은 모델링 확정 후 진행합니다.</p></header>
<nav><input id="search" placeholder="가구 이름 검색"><select id="category"><option value="">전체 공간</option></select><div id="colors" hidden></div><span id="count"></span></nav><main id="grid"></main>
<script>const items=DATA;let palette='Cream';const labels={Cream:'크림',Sage:'세이지',Blue:'블루',Rose:'로즈',Charcoal:'차콜'};
const cat=document.querySelector('#category');[...new Set(items.map(x=>x.category))].forEach(x=>{let o=document.createElement('option');o.value=x;o.textContent=x;cat.append(o)});
Object.entries(labels).forEach(([k,v])=>{let b=document.createElement('button');b.textContent=v;b.dataset.color=k;b.onclick=()=>{palette=k;render()};document.querySelector('#colors').append(b)});
function render(){const q=document.querySelector('#search').value.toLowerCase();const shown=items.filter(x=>(!cat.value||x.category===cat.value)&&(x.label+' '+x.id).toLowerCase().includes(q));document.querySelector('#count').textContent=shown.length+'개';document.querySelectorAll('[data-color]').forEach(b=>b.classList.toggle('active',b.dataset.color===palette));document.querySelector('#grid').innerHTML=shown.map(x=>`<article><img loading="lazy" src="QA/${x.id}/${palette}.png" alt="${x.label}"><div class="body"><h2>${x.label}</h2><div class="meta">${x.category} · ${x.triangles.toLocaleString()} tris<br>폭 ${x.dimensions[0].toFixed(2)} × 깊이 ${x.dimensions[1].toFixed(2)} × 높이 ${x.dimensions[2].toFixed(2)} m</div><a href="Models/${x.id}.blend">Blender 원본</a><a href="Exports/${x.id}/${x.id}_${palette}.fbx">FBX</a><a href="Exports/${x.id}/${x.id}_${palette}.glb">GLB</a></div></article>`).join('')};document.querySelector('#search').oninput=render;cat.onchange=render;render();</script></html>'''.replace('DATA',data)
(HERE/'Catalog.html').write_text(page,encoding='utf8')
print('CATALOG_READY',len(rows),'categories',len(groups))
