from pathlib import Path
import subprocess,json,shutil,hashlib
ROOT=Path(__file__).resolve().parents[2]; HERE=Path(__file__).resolve().parent
ASSETS=ROOT/'CompanyGame/Assets'; SRC=Path('C:/서현/3D 모델')
def gitfile(p):
    data=subprocess.check_output(['git','show','whitewood-furniture:'+p],cwd=ROOT)
    if data.startswith(b'version https://git-lfs.github.com/spec/v1'):
        sha=data.decode().split('oid sha256:')[1].splitlines()[0]
        data=(ROOT/'.git/lfs/objects'/sha[:2]/sha[2:4]/sha).read_bytes()
        if hashlib.sha256(data).hexdigest()!=sha:raise RuntimeError('LFS hash mismatch '+p)
    return data
catalog=json.loads(gitfile('ArtSource/FurnitureCollections/WhiteWood/catalog.json'))
prefix='CompanyGame/Assets/Art/Items/Furniture/Themes/WhiteWood'
paths=subprocess.check_output(['git','ls-tree','-r','--name-only','whitewood-furniture',prefix],cwd=ROOT).decode().splitlines()
for rel in paths:
    out=ROOT/rel
    if out.exists() and not out.read_bytes()[:80].startswith(b'version https://git-lfs.github.com/spec/v1'):continue
    out.parent.mkdir(parents=True,exist_ok=True);out.write_bytes(gitfile(rel))
items=[]
for row in catalog['items']:
    row=dict(row);row['id']=row['pack']+'_'+row['key'];row['category']=row['functions'].split(',')[0].strip()
    if row['category']=='Storage':row['category']+='/'+row['storage']
    row['modelPath']=row['themeModelPath'];row['source']='C:/서현/3D 모델/가구/'+row['sourceFile'];row['reused']=True
    items.append(row)
specs=[
('armchair','라운지 암체어','Seating',.92),('bar chair','바 의자','Seating',1.05),('barrel','나무 통','Decoration',.9),('barrel_shaded','나무 통 채색','Decoration',.9),('closet','옷장','Storage/Clothing',2),('door1','문 1','Fixtures/Doors',2.2),('door2','문 2','Fixtures/Doors',2.2),('door3','문 3','Fixtures/Doors',2.2),('sink','세면대','WaterSource',.9),('sofa_company','회사 소파','Seating',.85),('sofa_company2','회사 소파 2','Seating',.85),('toilet','변기','WaterSource',.8),
('bakery_show','베이커리 진열장','Display',1.5),('bakery_show2','베이커리 진열장 2','Display',1.7),('cafe_show','카페 진열장','Display',1.4),('coffee_machine_cafe','에스프레소 머신','Cooking',.6),('salon_shampoo_chair','미용실 샴푸 의자','Seating',1.05),('salon_shampoo_chair2','미용실 샴푸 의자 2','Seating',1.05)]
jobs=[];seen={};duplicates=[]
for key,label,cat,height in specs:
    path=SRC/'가구'/key
    model=next(path.glob('*.fbx')) if path.is_dir() else path.with_suffix('.fbx')
    digest=hashlib.sha256(model.read_bytes()).hexdigest()
    if digest in seen:duplicates.append({'source':str(model),'sameAs':seen[digest]});continue
    seen[digest]=str(model)
    dest=HERE/'Sources'/key;dest.mkdir(parents=True,exist_ok=True)
    shutil.copy2(model,dest/model.name)
    if model.parent==path:
        for tex in path.glob('*.png'):shutil.copy2(tex,dest/tex.name)
    jobs.append({'id':key.replace(' ','_'),'label':label,'category':cat,'height':height,'source':str(model),'input':str(dest/model.name),'hash':digest})
links=[('standing_chalkboard_01','입간판','Decoration',1.2),('Chandelier_02','샹들리에','Lighting',1),('outdoor_table_chair_set_01','야외 카페 세트','Surface',.8),('wooden_picnic_table','피크닉 테이블','Surface',.8),('plastic_monobloc_chair_01','플라스틱 의자','Seating',.9),('steel_frame_shelves_01','철제 선반','Storage/General',1.8)]
for key,label,cat,height in links:
    folder=Path('C:/Users/usercom/Downloads')/(key+'_4k.blend');path=folder/(key+'_4k.blend')
    jobs.append({'id':key,'label':label,'category':cat,'height':height,'source':str(path),'input':str(path),'hash':hashlib.sha256(path.read_bytes()).hexdigest()})
for key,label,cat,height,path in [('wheelchair','휠체어','Seating',1.1,SRC/'기타/wheelchair.fbx'),('delivery_box','배달 상자','Storage/General',.5,SRC/'기타/delivery box/base_basic_pbr.fbx')]:
    dest=HERE/'Sources'/key;dest.mkdir(parents=True,exist_ok=True)
    shutil.copy2(path,dest/path.name)
    for tex in path.parent.glob('*.png'):shutil.copy2(tex,dest/tex.name)
    jobs.append({'id':key,'label':label,'category':cat,'height':height,'source':str(path),'input':str(dest/path.name),'hash':hashlib.sha256(path.read_bytes()).hexdigest()})
(HERE/'jobs.json').write_text(json.dumps(jobs,ensure_ascii=False,indent=2),encoding='utf8')
(HERE/'collection_catalog.json').write_text(json.dumps(items,ensure_ascii=False,indent=2),encoding='utf8')
(HERE/'duplicates.json').write_text(json.dumps(duplicates,ensure_ascii=False,indent=2),encoding='utf8')
print('Existing collection items',len(items),'New source jobs',len(jobs),'Exact duplicates',len(duplicates))
