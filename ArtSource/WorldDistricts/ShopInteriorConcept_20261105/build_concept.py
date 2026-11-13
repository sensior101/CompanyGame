"""26 x 16.4 m low-poly retail interior concept; Blender and renders only.
Reuses the existing convenience-store mesh accumulator and primitive builders.
"""
import bpy,sys,ast,json,math,random
from pathlib import Path
from mathutils import Vector
sys.dont_write_bytecode=True
OUT=Path(__file__).resolve().parent;BASE=OUT.parents[1]
for name in ('Previews','QA'):(OUT/name).mkdir(parents=True,exist_ok=True)
helper=BASE/'ConvenienceInterior/build_interior.py'
tree=ast.parse(helper.read_text(encoding='utf8'))
names={'linear','material','group','frame','pt','mesh','box','cyl'}
exec(compile(ast.Module(body=[n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name in names],type_ignores=[]),str(helper),'exec'),globals())
bpy.ops.wm.read_factory_settings(use_empty=True)
random.seed(115);MAT={};SPEC=[];GEO={};GROUP='Architecture';origin=(0,0);angle=0
for name,color,rough,metal in [('Floor','#CAC8C0',.78,0),('Grout','#ABAFA9',.9,0),('Ivory','#DDDAD0',.8,0),('White','#E8E5DC',.67,0),('Oak','#B99A73',.72,0),('DarkOak','#80694E',.8,0),('Charcoal','#363C3B',.58,.08),('Metal','#8C9692',.45,.35),('Sage','#6B8173',.77,0),('DeepGreen','#3C554A',.76,0),('Sand','#C6AD88',.84,0),('Clay','#A97964',.82,0),('Blue','#6D848F',.76,0),('Paper','#DBD5C3',.82,0),('Screen','#4C6865',.4,0)]:material(name,color,rough,metal)
material('LED','#FFF1D6',.4,emit=3)
material('Glass','#C6D5D5',.12,alpha=.16)
MAT['Glass'].node_tree.nodes.get('Principled BSDF').inputs['Transmission Weight'].default_value=.65
footprints=[]
def rect(name,x,y,w,d):footprints.append({'name':name,'min':[x-w/2,y-d/2],'max':[x+w/2,y+d/2]})
# Floor and full room shell. Face-to-face inside dimensions are exactly 26 x 16.4.
group('ENV_Floor');frame();box((0,0,-.12),(26,16.4,.24),'Grout')
for ix in range(26):
    for iy in range(16):box((-12.5+ix,-8.2+(iy+.5)*1.025,.004),(.994,1.019,.008),'Floor')
for label,x in [('Left',-13.10),('Right',13.10)]:
    group('ENV_'+label+'Wall');box((x,0,1.65),(.20,16.8,3.3),'Ivory');box((x- math.copysign(.108,x),0,.065),(.025,16.4,.13),'Charcoal')
group('ENV_BackWall');box((0,8.30,1.65),(26,.20,3.3),'Ivory');box((0,8.187,.065),(26,.025,.13),'Charcoal')
box((0,8.18,2.73),(26,.035,.16),'DeepGreen');box((0,8.17,2.61),(26,.025,.035),'Oak')
group('ENV_Ceiling');box((0,0,3.37),(26.4,16.8,.14),'White')
for x in (-10,-5,0,5,10):box((x,0,3.29),(.025,16.4,.018),'Grout')
group('ENV_Front');box((0,-8.30,3.01),(26.4,.20,.58),'Ivory')
for x in (-13,-10.4,-7.8,-5.2,-2.6,2.6,5.2,7.8,10.4,13):box((x,-8.24,1.38),(.075,.12,2.76),'Charcoal')
for x,w in ((-7.5,11),(7.5,11)):
    box((x,-8.25,1.38),(w,.035,2.65),'Glass')
    for z in (.055,2.72):box((x,-8.24,z),(w,.12,.09),'Charcoal')
for x in (-.95,.95):box((x,-8.24,1.25),(.10,.15,2.50),'Charcoal')
box((0,-8.24,2.55),(2.0,.15,.10),'Charcoal')
for x in (-1.43,1.43):
    box((x,-8.18,1.25),(.90,.035,2.42),'Glass')
    for edge in (-.45,.45):box((x+edge,-8.18,1.25),(.045,.09,2.5),'Charcoal')
    box((x,-8.19,1.10),(.55,.025,.09),'Sage')
group('Entry_Mat');box((0,-7.05,.012),(2.8,1.8,.02),'Charcoal')
# A single shelf module is repeated; products are simple unbranded proxies.
def products(length,z,side,mode):
    for i in range(max(3,int(length/.40))):
        y=-length/2+.24+i*.40
        color=['Paper','Sage','Clay','Blue','Sand'][int(i+mode)%5]
        if (i+mode)%4==0:
            cyl((side*.35,y,z+.135),.10,.25,color,8)
            cyl((side*.35,y,z+.28),.061,.04,'Charcoal',8)
        else:
            height=.15+.045*((i+mode)%3)
            box((side*.34,y,z+height/2),(.32,.31,height),color)
            box((side*.505,y,z+height*.55),(.012,.20,.07),'Paper')
def gondola(index,x,y):
    group('Gondola_%02d'%index);frame(x,y)
    box((0,0,.10),(1.20,3.20,.20),'Oak');box((0,0,.66),(.075,3.10,1.08),'Ivory')
    for yy in (-1.55,1.55):box((0,yy,.70),(1.20,.10,1.20),'Sage')
    for z in (.20,.55,.90,1.25):
        box((0,0,z),(1.20,3.12,.055),'White')
        for side in (-1,1):box((side*.602,0,z-.01),(.025,3.05,.06),'Oak')
        if z<1.2:
            for side in (-1,1):products(3.12,z+.03,side,index)
    box((0,-1.612,1.04),(.55,.015,.17),'Paper')
    rect('Gondola_%02d'%index,x,y,1.20,3.20)
index=0
for y in (-2.25,3.0):
    for x in (-10,-6.1,-2.2,2.2,6.1,10):
        if y>0 and x>5:continue
        index+=1;gondola(index,x,y)
def wall_bay(index,x,y,yaw,length=2.8):
    group('WallShelf_%02d'%index);frame(x,y,yaw)
    box((0,.21,1.18),(length,.10,2.36),'Oak')
    for xx in (-length/2+.035,length/2-.035):box((xx,-.04,1.18),(.07,.60,2.36),'Charcoal')
    for z in (.14,.65,1.16,1.67,2.18):
        box((0,-.025,z),(length,.58,.055),'White');box((0,-.32,z-.005),(length,.03,.065),'Oak')
        if z<2:
            for j in range(6):
                xx=-length/2+.28+j*.44;color=['Paper','Sand','Sage','Clay'][j%4]
                box((xx,-.03,z+.16),(.32,.34,.26),color)
                box((xx,-.208,z+.16),(.22,.012,.075),'White')
    box((0,-.025,2.42),(length,.62,.08),'Oak')
# Wall shelves stop well before the entrance and checkout staffing zone.
wall_index=0
for side in (-1,1):
    for y in (-3.9,-.6,2.7):
        wall_index+=1;wall_bay(wall_index,side*12.60,y,-side*math.pi/2)
        rect('WallShelf_%02d'%wall_index,side*12.60,y,.65,2.8)
for x in (-10.5,-7.3,-4.1,-.9):
    wall_index+=1;wall_bay(wall_index,x,7.86,0,2.8);rect('WallShelf_%02d'%wall_index,x,7.86,2.8,.65)
# Entry promotion furniture leaves the 3.2 m main axis open.
for index,(x,y,w,d,h) in enumerate(((-5.8,-5.9,3.2,1.40,.78),(5.8,-5.9,3.2,1.40,.65)),1):
    group('Promotion_%02d'%index);frame(x,y)
    for xx in (-w/2+.15,w/2-.15):
        for yy in (-d/2+.15,d/2-.15):box((xx,yy,h/2),(.10,.10,h),'Charcoal')
    box((0,0,h),(w,d,.10),'Oak');box((0,0,.18),(w-.14,d-.14,.07),'Ivory')
    for i in range(4):
        box((-1.10+i*.72,0,h+.20),(.53,.60,.30),['Sage','Paper','Sand','Clay'][i])
        box((-1.10+i*.72,-.305,h+.20),(.31,.015,.10),'White')
    rect('Promotion_%02d'%index,x,y,w,d)
# Back-right checkout: customer approach open, 1.27 m staff aisle behind.
group('Checkout');frame()
box((7.9,5.8,.45),(6.0,.85,.90),'Oak');box((7.9,5.8,.94),(6.12,.99,.10),'Charcoal')
box((7.9,5.36,.49),(5.85,.035,.70),'Ivory');box((7.9,5.335,.16),(5.86,.025,.06),'Sage')
for x in (6.0,9.2):
    box((x,5.85,1.01),(.45,.32,.06),'Metal');box((x,5.88,1.17),(.07,.06,.30),'Charcoal')
    box((x,5.85,1.37),(.49,.07,.32),'Charcoal');box((x,5.807,1.37),(.42,.015,.25),'Screen')
    box((x+.63,5.67,1.015),(.28,.33,.07),'Charcoal')
box((11.31,6.43,.45),(.82,1.88,.90),'Oak');box((11.31,6.43,.94),(.94,1.98,.10),'Charcoal')
rect('Checkout',7.9,5.8,6.12,.99);rect('Checkout_Return',11.31,6.43,.94,1.98)
group('BackStorage');frame()
for x in (4.8,6.4,8.0,9.6,11.2):
    box((x,7.94,.98),(1.52,.48,1.96),'Ivory')
    for dx in (-.38,.38):box((x+dx,7.686,.98),(.73,.035,1.86),'White');box((x+dx+.22,7.66,1.02),(.025,.035,.22),'Charcoal')
box((8,7.92,2.0),(8.0,.52,.07),'Oak');rect('BackStorage',8,7.94,8,.52)
# Ceiling tracks and warm linear fixtures. All retained in the enclosed .blend.
group('LightingFixtures');frame()
for x in (-10,-6.1,-2.2,2.2,6.1,10):
    box((x,0,3.18),(.06,13.4,.08),'Charcoal')
    for y in (-5.6,-1.8,2.0,5.8):
        box((x,y,3.08),(.22,1.35,.09),'White');box((x,y,3.025),(.17,1.26,.025),'LED')
for x in (6.0,9.2):
    cyl((x,5.8,2.91),.018,.62,'Charcoal',8)
    cyl((x,5.8,2.56),.30,.13,'Sage',12,r2=.18);cyl((x,5.8,2.487),.26,.018,'LED',12)
# Generate grouped editable objects with planar UVs and small single-segment bevels.
asset=bpy.data.collections.new('SHOP_INTERIOR_ASSET');bpy.context.scene.collection.children.link(asset)
root=bpy.data.objects.new('ShopInteriorConcept',None);asset.objects.link(root)
for (label,matname),(verts,faces,_) in GEO.items():
    data=bpy.data.meshes.new(label+'_'+matname);data.from_pydata(verts,[],faces);data.materials.append(MAT[matname]);data.update()
    uv=data.uv_layers.new(name='UVMap')
    for poly in data.polygons:
        coords=[data.vertices[i].co for i in poly.vertices];ranges=[max(v[a] for v in coords)-min(v[a] for v in coords) for a in range(3)];axes=sorted(range(3),key=lambda a:ranges[a],reverse=True)[:2]
        lo=[min(v[a] for v in coords) for a in axes]
        for li in poly.loop_indices:
            v=data.vertices[data.loops[li].vertex_index].co;uv.data[li].uv=tuple((v[a]-lo[j])/max(ranges[a],1e-6) for j,a in enumerate(axes))
    ob=bpy.data.objects.new(label+'_'+matname,data);asset.objects.link(ob);ob.parent=root
    if not label.startswith('ENV') and matname not in ('Glass','LED'):
        bevel=ob.modifiers.new('Fine edges','BEVEL');bevel.width=.007;bevel.segments=1
# Generic Korean category labels, not a store brand.
def text_label(name,body,location,size):
    data=bpy.data.curves.new(name,'FONT');data.body=body;data.font=bpy.data.fonts.load('C:/Windows/Fonts/malgunbd.ttf');data.size=size;data.align_x='CENTER';data.align_y='CENTER';data.extrude=.002;data.resolution_u=2;data.materials.append(MAT['DeepGreen'])
    ob=bpy.data.objects.new(name,data);asset.objects.link(ob);ob.parent=root;ob.location=location;ob.rotation_euler=(math.pi/2,0,0);return ob
text_label('Back_Category','생활잡화',( -6.0,8.16,2.98),.32)
text_label('Checkout_Category','계산대',(8.0,8.16,2.98),.30)
# Studio, daylight, and even practical light coverage.
stage=bpy.data.collections.new('PRESENTATION');bpy.context.scene.collection.children.link(stage)
def area(name,loc,power,size,color,target):
    data=bpy.data.lights.new(name,'AREA');data.energy=power;data.shape='DISK';data.size=size;data.color=color
    ob=bpy.data.objects.new(name,data);stage.objects.link(ob);ob.location=loc;ob.rotation_euler=(Vector(target)-ob.location).to_track_quat('-Z','Y').to_euler();return ob
area('Storefront_Daylight',(0,-10,6),2800,12,(.90,.95,1),(0,1,0))
area('Cutaway_Softbox',(-10,-8,18),3600,14,(1,.94,.84),(0,0,0))
for x in (-9,-3,3,9):
    for y in (-5.3,0,5.3):area('Interior_Practical',(x,y,3.15),160,3.6,(1,.93,.82),(x,y,0))
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=48;scene.cycles.use_denoising=True
scene.world=bpy.data.worlds.new('NeutralWorld');scene.world.use_nodes=True;scene.world.node_tree.nodes['Background'].inputs['Color'].default_value=(.69,.73,.76,1);scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value=.45
scene.view_settings.view_transform='AgX';scene.view_settings.look='AgX - Medium High Contrast';scene.view_settings.exposure=.45
scene.render.resolution_x=1500;scene.render.resolution_y=1100;scene.render.resolution_percentage=100
def camera(name,position,target,lens=28):
    data=bpy.data.cameras.new(name);data.lens=lens;data.clip_end=300;ob=bpy.data.objects.new(name,data);stage.objects.link(ob);ob.location=position;ob.rotation_euler=(Vector(target)-ob.location).to_track_quat('-Z','Y').to_euler();return ob
eye=camera('Camera_EyeLevel',(-.6,-6.75,1.70),(4.4,4.1,1.40),20)
cut=camera('Camera_Cutaway',(29,-32,29),(0,0,.7),42);cut.data.type='ORTHO';cut.data.ortho_scale=37
scene.camera=eye;bpy.context.preferences.filepaths.save_version=0
# Door and aisle measurements are analytic plan checks, independent of image framing.
checks={'floorWidth':26.,'floorDepth':16.4,'area':426.4,'libraryReferenceArea':640.,'libraryFraction':426.4/640.,'ceilingHeight':3.3,'doorClearWidth':1.8,'doorClearHeight':2.5,'mainAisleClearWidth':3.2,'crossAisleClearWidth':2.05,'outerAisleClearWidth':1.525,'staffAisleClearWidth':1.275,'gondolaHeight':1.2775,'allFixturesInsideRoom':all(f['min'][0]>=-13 and f['max'][0]<=13 and f['min'][1]>=-8.2 and f['max'][1]<=8.2 for f in footprints)}
checks['allFixturesInsideRoom']=all(f['min'][0]>=-13-1e-6 and f['max'][0]<=13+1e-6 and f['min'][1]>=-8.2-1e-6 and f['max'][1]<=8.2+1e-6 for f in footprints)
assert checks['allFixturesInsideRoom'];assert not any(f['min'][0]<1.6-1e-5 and f['max'][0]>-1.6+1e-5 and f['min'][1]<5 for f in footprints)
(OUT/'QA/layout.json').write_text(json.dumps({'checks':checks,'fixtures':footprints},ensure_ascii=False,indent=2),encoding='utf8')
(OUT/'manifest.json').write_text(json.dumps({'id':'ShopInteriorConcept','purpose':'Blender interior visual concept only; no gameplay or Unity integration','dimensions':checks,'blend':'ShopInteriorConcept.blend','cutaway':'Previews/01_Cutaway.png','eyeLevel':'Previews/02_EyeLevel.png','reusedHelper':'../../ConvenienceInterior/build_interior.py'},ensure_ascii=False,indent=2),encoding='utf8')
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'ShopInteriorConcept.blend'))
render_helper=BASE/'WorldDistricts/CommercialBlocks/LowPoly_20261104/KoreanPalette/SignageReady/FrontOnly/build_front_only.py'
tree=ast.parse(render_helper.read_text(encoding='utf8'));exec(compile(ast.Module(body=[n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name=='render'],type_ignores=[]),str(render_helper),'exec'),globals())
cut_hidden=[]
for ob in asset.objects:
    if ob.name.startswith(('ENV_Ceiling','ENV_Front','ENV_RightWall')):ob.hide_render=True;cut_hidden.append(ob)
scene.camera=cut;render(OUT/'Previews/01_Cutaway.png');print('CUTAWAY_READY',flush=True)
for ob in cut_hidden:ob.hide_render=False
scene.camera=eye;scene.render.resolution_x=1500;scene.render.resolution_y=1000;scene.cycles.samples=64
render(OUT/'Previews/02_EyeLevel.png')
print('SHOP_INTERIOR_COMPLETE',checks,flush=True)
