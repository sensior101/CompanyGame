import json,html,csv,os
root=r'C:/서현/프로젝트/companyGame'
data=json.load(open(root+'/ArtSource/FurnitureCollections/catalog.json',encoding='utf-8'))['items']
out=root+'/CompanyGame/Docs/FurnitureCollections'
packs={'HomeOffice':'홈오피스','Bedroom':'안방','LivingRoom':'거실','Kitchen':'주방','Essentials':'필수 가구'}
head='''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>가구 에셋 분류 · 61개</title><style>body{margin:0;background:#191d22;color:#eceff2;font:15px system-ui;padding:32px}h1{font-size:30px}p{color:#b7c1cc}header{max-width:1200px;margin:auto}input,select{padding:12px;margin:8px 8px 20px 0;background:#303740;color:white;border:1px solid #52606d;border-radius:8px}main{display:grid;grid-template-columns:repeat(auto-fill,minmax(250px,1fr));gap:20px;max-width:1440px;margin:auto}article{background:#272c33;border-radius:12px;overflow:hidden}img{width:100%;display:block}.body{padding:16px}h2{font-size:18px;margin:0 0 9px}.tag{font-size:12px;color:#a8d1cb;line-height:1.8}small{display:block;color:#abb2bd;line-height:1.6;margin-top:9px}code{font-size:11px;overflow-wrap:anywhere;color:#909aa8}article[hidden]{display:none}</style><header><h1>가구 에셋 분류</h1><p>61개 · 홈오피스 11 / 안방 10 / 거실 12 / 주방 10 / 필수 가구 18</p><p>무채색 분리 미리보기. 색칠은 후속 작업입니다. 기능과 설치 위치는 분류 데이터이며, 설치·보관·취침 등의 실행 기능은 포함하지 않습니다.</p><input id="q" placeholder="이름 / 기능 / 위치 검색"><select id="pack"><option value="">전체 모음</option>'''
head+=''.join('<option value="'+k+'">'+v+'</option>' for k,v in packs.items())+'</select><p id="count">61개</p></header><main>'
cards=[];csvrows=[]
for row in data:
 cat='Sleep' if 'Sleep' in row['functions'] else row['functions']
 if cat=='Storage':cat+='/'+row['storage']
 asset='Assets/Gameplay/Item/Furniture/'+cat+'/'+row['pack']+'/'+row['key']
 tags=row['functions']+' · '+row['storage']+' · '+row['placement']
 search=html.escape(row['label']+' '+tags+' '+row['pack'])
 cards.append('<article data-pack="'+row['pack']+'" data-search="'+search+'"><img loading="lazy" src="'+row['pack']+'_'+row['key']+'.png" alt="'+html.escape(row['label'])+'"><div class="body"><h2>'+html.escape(row['label'])+'</h2><div class="tag">'+packs[row['pack']]+' · '+tags+'</div><small>'+html.escape(row.get('note',''))+'</small><small>'+str(row['triangles'])+' triangles</small><code>'+asset+'.prefab</code></div></article>')
 csvrows.append([packs[row['pack']],row['label'],row['functions'],row['storage'],row['placement'],asset+'.prefab',row.get('note',''),row['triangles']])
end='''</main><script>function filter(){let n=0;const q=document.querySelector('#q').value.toLowerCase(),p=document.querySelector('#pack').value;document.querySelectorAll('article').forEach(a=>{a.hidden=(p&&a.dataset.pack!==p)||!a.dataset.search.toLowerCase().includes(q);if(!a.hidden)n++});document.querySelector('#count').textContent=n+'개'}document.querySelector('#q').oninput=filter;document.querySelector('#pack').onchange=filter;</script></html>'''
open(out+'/Catalog.html','w',encoding='utf-8').write(head+''.join(cards)+end)
with open(out+'/Catalog.csv','w',encoding='utf-8-sig',newline='') as f:
 w=csv.writer(f);w.writerow(['모음','가구','기능','보관종류','설치위치','프리팹','분리비고','삼각형수']);w.writerows(csvrows)
# Ensure every original connected component is accounted for; only one duplicate dining chair is omitted.
components=json.load(open(root+'/CompanyGame/Temp/FurnitureAudit/components.json'))
coverage={}
for pack in packs:
 used=[i for r in data if r['pack']==pack for i in r['parts']]
 missing=set(range(len(components[pack][0]['components'])))-set(used)
 coverage[pack]=dict(assets=sum(r['pack']==pack for r in data),duplicateAssignments=len(used)-len(set(used)),omittedComponents=sorted(missing))
 assert len(used)==len(set(used))
 assert missing==({8} if pack=='Kitchen' else set())
json.dump(coverage,open(out+'/SeparationCoverage.json','w'),indent=2)
print('Gallery and coverage complete',len(data))
