"""Commercial furniture recipes, reusing HouseholdLowPoly primitives and models.
Blender authoring only. Metres, Z up, front -Y. No Unity writes or exports.
"""
import bpy
import importlib.util
import json
import math
import sys
from pathlib import Path
from mathutils import Vector

sys.dont_write_bytecode = True
OUT = Path(__file__).resolve().parent
SOURCE = OUT.parent / 'HouseholdLowPoly/build_household.py'
spec = importlib.util.spec_from_file_location('household', SOURCE)
h = importlib.util.module_from_spec(spec)
spec.loader.exec_module(h)
for folder in ('Models', 'Previews', 'QA'):
    (OUT / folder).mkdir(exist_ok=True)
ROOM = OUT.parents[2] / 'WorldDistricts/ShopInteriorConcept_20261105/ShopInterior_Empty.blend'
entries = []

def item(identifier, label, category, build, placement='Floor'):
    entries.append(dict(id=identifier, label=label, category=category,
                        placement=placement, function='Decoration', build=build))

def existing(identifier):
    return next(row['build'] for row in h.CATALOG if row['id'] == identifier)

def table(width, depth, height=.74):
    h.tabletop(width, depth, height)
    h.legs(width-.16, depth-.16, height-.045, 'Dark', .027)
    h.box('Underframe', (width-.13,.035,.09), (0,depth/2-.08,height-.10), 'Dark')

def guest():
    h.legs(.43,.42,.43,'Wood',.025)
    h.box('Seat',(.50,.48,.08),(0,0,.45),'Fabric',.03)
    for x in (-.21,.21):
        h.rod('BackSupport',(x,.19,.42),(x,.24,.86),.022,'Wood')
    h.box('Back',(.48,.075,.25),(0,.23,.75),'Fabric',.026,(-.10,0,0))

def bench():
    h.legs(1.40,.42,.39,'Dark',.035)
    h.box('SeatRail',(1.62,.54,.09),(0,0,.40),'Wood')
    for x in (-.54,0,.54):
        h.box('SeatPad',(.51,.50,.07),(x,-.01,.48),'Fabric',.025)
        h.box('BackPad',(.51,.07,.27),(x,.24,.72),'Fabric',.025)
    for x in (-.73,.73):
        h.rod('BackPost',(x,.23,.40),(x,.23,.83),.022,'Dark')

def cubicle():
    table(1.40,.70)
    h.box('RearPanel',(1.40,.05,.61),(0,.365,1.035),'Fabric')
    for x in (-.73,.73):
        h.box('SidePanel',(.05,.75,.61),(x,.015,1.035),'Fabric')
        h.box('TopCap',(.052,.696,.018),(x,-.012,1.349),'Dark',.003)
    h.box('RearTopCap',(1.50,.052,.018),(0,.365,1.349),'Dark',.003)

def corner_desk():
    table(1.60,.70)
    h.box('ReturnTop',(.65,.85,.045),(.475,-.775,.7175),'Wood')
    for x in (.20,.75):
        h.box('ReturnLeg',(.055,.055,.695),(x,-1.12,.3475),'Dark')

def board(black=False):
    h.box('Backing',(1.50,.035,.90),(0,0,.45),'Dark' if black else 'Metal')
    h.box('WritingSurface',(1.44,.012,.84),(0,-.025,.45),'Rubber' if black else 'White',.002)
    for x in (-.735,.735):h.box('FrameSide',(.03,.045,.84),(x,-.025,.45),'Wood' if black else 'Metal',.004)
    for z in (.015,.885):h.box('FrameEdge',(1.50,.045,.03),(0,-.025,z),'Wood' if black else 'Metal',.004)
    h.box('Tray',(1.10,.10,.025),(0,-.075,.025),'Wood' if black else 'Metal',.004)

def counter(kind):
    width = 1.8 if kind != 4 else 2.2
    height = .90 if kind == 5 else 1.02
    role = ['Wood','Body','Dark','Fabric','Wood','Body'][kind]
    h.carcass(width,.65,height-.06,.04,role)
    h.box('Worktop',(width+.08,.73,.055),(0,0,height-.0275),'Wood' if kind in (1,2,5) else 'White')
    h.box('CustomerFront',(width-.02,.035,height-.16),(0,.33,(height+.04)/2),role)
    h.box('RecessedPlinth',(width-.12,.54,.08),(0,0,.04),'Dark')
    h.box('StaffShelf',(width-.10,.55,.035),(0,0,.45),'Wood')
    if kind == 0:
        for i in range(17):h.box('VerticalOakSlat',(.055,.028,height-.20),(-.80+i*.10,.36,height/2+.025),'Wood',.003)
    if kind == 1:
        for z in (.28,.53,.78):h.box('HorizontalTrim',(width-.12,.025,.025),(0,.36,z),'Wood',.003)
    if kind == 2:
        h.box('RaisedReceptionLedge',(1.25,.26,.055),(-.20,.24,1.18),'Wood')
        h.box('RaisedPanel',(1.25,.04,.22),(-.20,.30,1.07),'Dark')
    if kind == 3:
        h.box('ReturnBody',(.60,.90,.94),(.60,-.775,.51),'Body')
        h.box('ReturnTop',(.68,.88,.055),(.60,-.805,.9925),'White')
    if kind == 4:
        for x in (-.93,0,.93):h.box('FrontRecess',(.035,.025,.76),(x,.354,.53),'Dark',.002)
        h.box('FrontBand',(2.10,.025,.06),(0,.375,.84),'Dark',.003)
    if kind == 5:
        for x in (-.84,.84):h.box('MetalCorner',(.045,.04,.78),(x,.36,.49),'Metal',.003)
        h.box('FrontInset',(1.30,.025,.42),(0,.355,.50),'Fabric')

def display():
    h.box('Base',(1.50,.65,.30),(0,0,.15),'Wood')
    for x in (-.725,.725):
        for y in (-.30,.30):h.box('FramePost',(.035,.035,.75),(x,y,.675),'Dark',.003)
        h.box('GlassSide',(.008,.57,.69),(x,0,.675),'Glass',0)
    h.box('GlassFront',(1.42,.008,.69),(0,.30,.675),'Glass',0)
    h.box('GlassTop',(1.50,.65,.012),(0,0,1.045),'Glass',0)
    for z in (.33,.66,1.03):
        h.box('Shelf',(1.42,.57,.012),(0,0,z),'Glass',0)
        h.box('ShelfEdge',(1.46,.028,.025),(0,.30,z),'Dark',.002)
    for x in (-.36,.36):h.box('RearSlidingGlass',(.72,.008,.69),(x,-.30,.675),'Glass',0)

def partition(height):
    h.box('Panel',(1.20,.055,height-.07),(0,0,height/2+.035),'Fabric')
    for x in (-.615,.615):
        h.box('Upright',(.035,.07,height-.025),(x,0,(height-.025)/2),'Dark',.004)
        h.box('Foot',(.055,.42,.035),(x,0,.0175),'Dark',.004)
    h.box('TopRail',(1.26,.07,.025),(0,0,height-.0125),'Dark',.004)

def light(kind):
    if kind == 0:
        h.box('CeilingFixture',(1.22,.25,.065),(0,0,.065),'White')
        for y in (-.065,.065):
            h.cyl('Tube',.023,1.12,(0,y,.025),'White',12,(0,math.pi/2,0))
            for x in (-.57,.57):h.box('Socket',(.045,.055,.045),(x,y,.025),'Metal',.003)
    else:
        height=1.55 if kind==1 else .47
        radius=.19 if kind==1 else .115
        h.cyl('Base',radius,.035,(0,0,.0175),'Dark',16)
        h.rod('Stem',(0,0,.03),(0,0,height-.22),.018,'Dark')
        h.lathe('OpenShade',[(0,0),(.08,0),(radius*1.30,-.25),(radius*1.20,-.25),(.07,-.02),(0,-.02)],(0,0,height),'Body',12)
        h.cyl('Diffuser',radius*.95,.012,(0,0,height-.18),'White',12)

def clock():
    h.cyl('ClockRim',.20,.045,(0,0,.20),'Wood',32,(math.pi/2,0,0))
    h.cyl('ClockFace',.181,.008,(0,-.026,.20),'White',32,(math.pi/2,0,0))
    for i in range(12):
        a=i*math.pi/6
        h.box('HourMark',(.012,.006,.025),(.155*math.sin(a),-.033,.20+.155*math.cos(a)),'Rubber',0,(0,a,0))
    h.rod('HourHand',(0,-.039,.20),(-.075,-.039,.245),.008,'Rubber',6)
    h.rod('MinuteHand',(0,-.041,.20),(.085,-.041,.29),.006,'Rubber',6)

def artwork(frame):
    w,d=(.64,.84) if frame else (.60,.90)
    if frame:
        h.box('FrameBacking',(w,.035,d),(0,0,d/2),'Wood')
        for x in (-w/2+.018,w/2-.018):h.box('FrameSide',(.036,.055,d-.072),(x,-.025,d/2),'Wood',.004)
        for z in (.018,d-.018):h.box('FrameEdge',(w,.055,.036),(0,-.025,z),'Wood',.004)
    panel=h.mesh('ImageSurface',[(-w/2+.04,-.032,.04),(w/2-.04,-.032,.04),(w/2-.04,-.032,d-.04),(-w/2+.04,-.032,d-.04)],[(0,1,2,3)],'White')
    uv=panel.data.uv_layers.new(name='ImageUV')
    for loop,coord in zip(uv.data,((0,0),(1,0),(1,1),(0,1))):loop.uv=coord
    mat=bpy.data.materials.new('ImagePlaceholder_White');mat.diffuse_color=(1,1,1,1);mat.use_nodes=True
    mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(1,1,1,1)
    panel.data.materials.clear();panel.data.materials.append(mat)
    panel['ImageSurface']=True

def plant(kind):
    radius=[.18,.22,.20,.28,.30,.15][kind]
    potheight=[.27,.38,.30,.40,.42,.22][kind]
    h.lathe('Planter',[(0,0),(radius*.76,0),(radius,potheight),(radius*.89,potheight),(radius*.85,potheight-.04),(0,potheight-.04)],role='Body' if kind%2 else 'Wood',n=[12,8,10,4,16,12][kind])
    h.cyl('Soil',radius*(.60 if kind==3 else .87),.018,(0,0,potheight-.03),'Rubber',12)
    if kind == 2:
        for i in range(7):
            a=i*2.4
            h.cyl('Cactus',.038,.24+.08*(i%3),(.09*math.cos(a),.09*math.sin(a),potheight+.10+.04*(i%3)),'Fabric',8,r2=.025)
        return
    count=[9,10,0,13,16,9][kind]
    tall=[.55,.95,0,1.0,1.30,.32][kind]
    for i in range(count):
        a=i*2.399
        z=potheight+.12+tall*(i+1)/count
        reach=(.20 if kind==5 else .28)+.10*math.sin(i)
        start=Vector((0,0,z-.16));end=Vector((math.cos(a)*reach,math.sin(a)*reach,z+.07))
        h.rod('Stem',(0,0,potheight),start,.008,'Wood',6)
        h.rod('LeafStalk',start,end,.006,'Fabric',6)
        direction=(end-start).normalized();side=Vector((-math.sin(a),math.cos(a),0))*(.07 if kind==1 else .105)
        mid=start.lerp(end,.56);tip=end+direction*(.08 if kind==1 else .16)
        verts=[start,mid+side,tip,mid-side,mid+Vector((0,0,.045)),mid-Vector((0,0,.008))]
        h.mesh('FacetedLeaf',[tuple(v) for v in verts],[(0,1,4),(1,2,4),(2,3,4),(3,0,4),(1,0,5),(2,1,5),(3,2,5),(0,3,5)],'Fabric' if i%3 else 'Accent')

def rug(kind):
    if kind == 2:
        h.cyl('RoundRug',.85,.012,(0,0,.006),'Fabric',40)
        h.ring('WovenBorder',.78,.012,(0,0,.015),'Accent',n=40)
    elif kind == 3:
        h.rounded_loft('OvalRug',[(0,1.8,1.15,.55),(.015,1.8,1.15,.55)],'Fabric')
    else:
        w,d=[(1.80,1.20),(.70,2.20),(0,0),(0,0),(1.80,1.40),(1.10,.70)][kind]
        h.box('RugBase',(w,d,.012),(0,0,.006),'Fabric',.003)
        if kind == 0:
            for x in (-w/2+.07,w/2-.07):h.box('Border',(.035,d-.175,.003),(x,0,.013),'Accent',0)
            for y in (-d/2+.07,d/2-.07):h.box('Border',(w-.12,.035,.003),(0,y,.013),'Accent',0)
        if kind in (1,5):
            for i in range(9):h.box('WovenStripe',(w-.08,.035,.002),(0,-d*.4+i*d*.10,.013),'Accent',0)
        if kind == 4:
            for i in range(6):
                for j in range(4):
                    if (i+j)%2==0:h.box('Check',(.28,.33,.002),(-.75+i*.30,-.525+j*.35,.013),'Accent',0)

item('BarStool','바 스툴','Seating',existing('BarChair'))
item('OfficeChair','사무용 회전의자','Seating',existing('OfficeChair'))
item('GuestChair','손님용 의자','Seating',guest)
item('WaitingBench','대기벤치','Seating',bench)
item('Armchair','1인 암체어','Seating',existing('Armchair'))
item('CornerSofa','ㄱ자 소파','Seating',existing('CornerSofa'))
item('SquareTable1','1인 사각테이블','Tables',lambda:table(.60,.60))
item('SquareTable2','2인 사각테이블','Tables',lambda:table(.80,.80))
item('CommunalTable','긴 공용 테이블','Tables',lambda:table(2.40,.90))
item('PlainDesk','일반 책상','Tables',lambda:h.desk(1.30,.65,.74))
item('CubicleDesk','칸막이 오피스 책상','Tables',cubicle)
item('CornerDesk','ㄱ자 책상','Tables',corner_desk)
item('MeetingTable','회의테이블','Tables',lambda:table(2.40,1.10))
item('Whiteboard','벽걸이 화이트보드','Fixtures',lambda:board(False),'Wall')
item('Blackboard','벽걸이 블랙보드','Fixtures',lambda:board(True),'Wall')
for i,label in enumerate(('우드 슬랫','화이트 패널','높은 안내','ㄱ자','우드 프레임','낮은 화이트')):
    item('Counter%02d'%(i+1),'카운터 '+label,'Counters',lambda i=i:counter(i))
item('GlassCounter','유리진열카운터','Counters',display)
item('WallBarTable','벽을 바라보는 바 테이블','Tables',lambda:table(2.20,.42,1.05))
item('LowPartition','낮은 파티션','Fixtures',lambda:partition(1.10))
item('HighPartition','높은 파티션','Fixtures',lambda:partition(1.80))
item('ShoeCabinet','신발장','Fixtures',existing('ShoeCabinet'))
for i,label in enumerate(('형광등','스탠드조명','책상램프')):
    item(('Fluorescent','FloorLamp','DeskLamp')[i],label,'Fixtures',lambda i=i:light(i),'Ceiling' if i==0 else ('Surface' if i==2 else 'Floor'))
item('WallClock','벽시계','Fixtures',clock,'Wall')
item('PictureFrame','흰 이미지 면 액자','Fixtures',lambda:artwork(True),'Wall')
item('Poster','흰 이미지 면 포스터','Fixtures',lambda:artwork(False),'Wall')
for i in range(6):item('Plant%02d'%(i+1),('탁상 잎화분','세로 잎화분','선인장 화분','중형 관엽 화분','대형 관엽 화분','낮은 잎화분')[i],'Plants',lambda i=i:plant(i))
for i in range(6):item('Rug%02d'%(i+1),('테두리 사각 러그','줄무늬 러너','원형 러그','타원형 러그','체크 러그','현관 러그')[i],'Rugs',lambda i=i:rug(i))
item('SeatCushion','방석','Rugs',lambda:h.cushion('SeatCushion',(.43,.43,.07),(0,0,.035)))

def bounds(objects):
    bpy.context.view_layer.update()
    points=[o.matrix_world @ v.co for o in objects if o.type=='MESH' for v in o.data.vertices]
    return [min(p[i] for p in points) for i in range(3)], [max(p[i] for p in points) for i in range(3)]

def camera_scene(location, target, scale, resolution=(1400,1000)):
    scene=bpy.context.scene
    scene.render.engine='CYCLES';scene.cycles.samples=20
    scene.cycles.use_denoising=True
    scene.render.resolution_x,scene.render.resolution_y=resolution
    scene.render.resolution_percentage=100
    scene.world=bpy.data.worlds.new('StudioWorld');scene.world.use_nodes=True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.70,.74,.76,1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value=.65
    bpy.ops.object.camera_add(location=location)
    cam=bpy.context.object;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler()
    cam.data.type='ORTHO';cam.data.ortho_scale=scale;scene.camera=cam
    bpy.ops.object.light_add(type='AREA',location=(target[0]-3,target[1]-4,target[2]+8))
    bpy.context.object.data.energy=1800;bpy.context.object.data.shape='DISK';bpy.context.object.data.size=7
    scene.view_settings.view_transform='AgX'
    scene.render.image_settings.file_format='PNG'

def load_asset(identifier):
    with bpy.data.libraries.load(str(OUT/'Models'/f'{identifier}.blend'),link=False) as (src,dst):
        dst.collections=[identifier+'_ASSET']
    collection=dst.collections[0];bpy.context.scene.collection.children.link(collection)
    return next(o for o in collection.objects if o.type=='EMPTY'), collection

def build():
    rows=[]
    for index,entry in enumerate(entries):
        h.init(entry)
        colors=dict(Body='F2F0EA',Wood='C7AA80',Fabric='ECE8DF',Accent='D4BE9D',Dark='E1DFD7',Metal='C5C7C3',White='FAF9F5')
        if entry['category']=='Plants':colors.update(Fabric='536F48',Accent='81945D')
        for role,color in colors.items():
            mat=h.MATS['Cream'][role];mat.diffuse_color=h.rgba(color)
            mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=h.rgba(color)
            mat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.78
        entry['build']()
        if entry['category']=='Counters':
            for obj in h.PARTS:
                obj.location.x *= -1
                obj.location.y *= -1
                obj.rotation_euler.z += math.pi
        if entry['id']=='Armchair':
            for obj in h.PARTS:
                if 'Shell' in obj.name:
                    mod=obj.modifiers.new('LowPolyReduction','DECIMATE');mod.ratio=.25;h.apply(obj,mod)
        row=h.prepare(entry)
        row['palettes']=['WhiteAndWood'];row['source_recipe']=str(SOURCE)
        h.root['AuthoringOnly']=True
        # Wall assets use their rear mounting plane as Y=0. Floor assets sit on Z=0.
        if entry['placement']=='Wall':
            _,hi=bounds(h.PARTS)
            for obj in h.PARTS:obj.location.y-=hi[1]
        lo,hi=bounds(h.PARTS);row['bounds_min']=lo;row['bounds_max']=hi
        row['dimensions']=[round(hi[i]-lo[i],4) for i in range(3)]
        camera_scene((3,-4,2.8),(0,0,(hi[2]+lo[2])/2),max(row['dimensions'])*1.8+1)
        bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Models'/f"{entry['id']}.blend"),compress=True)
        rows.append(row);print('BUILT',entry['id'],flush=True)
    (OUT/'QA/catalog.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2),encoding='utf8')

def sheets(categories=('Seating','Tables','Counters','Fixtures','Plants','Rugs')):
    for category in categories:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        ink=bpy.data.materials.new('LabelInk');ink.use_nodes=True
        ink.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.025,.03,.025,1)
        subset=[e for e in entries if e['category']==category]
        for i,entry in enumerate(subset):
            root,col=load_asset(entry['id'])
            root.location=(i%3*3.7,-(i//3)*3.2,0)
            if entry['placement']=='Ceiling':
                root.rotation_euler.x=math.pi
                root.location.z=.10
            bpy.ops.object.text_add(location=(root.location.x-1.2,root.location.y-1.45,.01))
            text=bpy.context.object;text.data.body=entry['id'];text.data.size=.20;text.data.extrude=0
            text.data.materials.append(ink)
        rows=math.ceil(len(subset)/3);target=(3.7,-(rows-1)*1.6,.3)
        bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.025))
        ground=bpy.data.materials.new('Backdrop');ground.use_nodes=True
        ground.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.28,.31,.30,1)
        ground.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.85
        bpy.context.object.data.materials.append(ground)
        camera_scene((target[0]+1,target[1]-12,13),target,max(12,rows*3.8),(1500,1200))
        scene=bpy.context.scene;scene.render.filepath=str(OUT/'Previews'/f'{category}.png')
        bpy.ops.render.render(write_still=True)
        print('RENDERED',category,flush=True)

def room_preview():
    bpy.ops.wm.open_mainfile(filepath=str(ROOM))
    bpy.context.view_layer.update()
    evidence=[]
    for obj in bpy.context.scene.objects:
        if obj.type=='MESH':
            evidence.append(dict(name=obj.name,dimensions=list(obj.dimensions),location=list(obj.location)))
        if obj.type=='LIGHT':obj.hide_render=True
        if any(word in obj.name.lower() for word in ('ceiling','front','rightwall')):obj.hide_render=True
    (OUT/'QA/room_source.json').write_text(json.dumps(evidence,indent=2),encoding='utf8')
    floor_index=0
    wall_index=0
    for i,entry in enumerate(entries):
        root,col=load_asset(entry['id'])
        if entry['placement']=='Wall':
            root.location=(-10+wall_index*3.0,8.18,1.40)
            wall_index+=1
        elif entry['placement']=='Ceiling':
            root.location=(0,0,3.20)
        else:
            root.location=(-10.4+floor_index%8*3,-5.7+floor_index//8*2.45,0)
            floor_index+=1
    camera_scene((24,-31,28),(0,0,0),36,(1800,1200))
    bpy.context.scene.render.filepath=str(OUT/'Previews/RoomScale.png')
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'CommercialFurniture_RoomScale.blend'),compress=True)
    bpy.ops.render.render(write_still=True)

if __name__=='__main__':
    if '--preview-only' not in sys.argv:build()
    sheets()
    room_preview()
