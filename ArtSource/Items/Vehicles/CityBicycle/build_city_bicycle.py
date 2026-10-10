"""Reference city bicycle. Blender 5.2; shared geometry, six paint finishes."""
import bpy, math, os, json
from mathutils import Vector
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '../../../..'))
REPO = ROOT
OUT = os.path.join(REPO, 'CompanyGame/Assets/Art/Items/Vehicles/CityBicycle')
SRC = os.path.dirname(__file__)
os.makedirs(OUT+'/Models', exist_ok=True)
os.makedirs(OUT+'/Icons', exist_ok=True)
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
PALETTE=[('Mint','민트','#7FA28D'),('Cream','크림','#E7DCC7'),('Brown','브라운','#916750'),('Blue','블루','#5E7F9F'),('Coral','코랄','#DA8580'),('Charcoal','차콜','#53585B')]
def color(h):
    v=[int(h[i:i+2],16)/255 for i in (1,3,5)]
    return tuple(x/12.92 if x<=.04045 else ((x+.055)/1.055)**2.4 for x in v)+(1,)
def mat(name,h,metal=0,rough=.45):
    m=bpy.data.materials.new(name);m.diffuse_color=color(h);m.use_nodes=True
    n=m.node_tree.nodes.get('Principled BSDF');n.inputs['Base Color'].default_value=color(h);n.inputs['Metallic'].default_value=metal;n.inputs['Roughness'].default_value=rough
    return m
paint=mat('Bike_Paint','#7FA28D',.35,.3);rubber=mat('Bike_Rubber','#292D2E',0,.8);chrome=mat('Bike_Chrome','#BFC4C3',.8,.25)
leather=mat('Bike_Leather','#80573F',0,.5);wicker=mat('Bike_Wicker','#B58A5A',0,.8);dark=mat('Bike_DarkMetal','#464B4D',.6,.4)
red=mat('Bike_ReflectorRed','#DD3934',.15,.25);amber=mat('Bike_ReflectorAmber','#ECA43C',.1,.35);lamp=mat('Bike_Lamp','#ECECDA',.1,.2)
parts=[]
def finish(o,name,m):
    o.name=name;o.data.materials.append(m);parts.append(o)
    for p in o.data.polygons:p.use_smooth=True
    return o
def tube(name,points,r,m,sides=8):
    # Input uses Unity-like x lateral, y height, z forward; Blender x,y,z = x,-z,y.
    ps=[Vector((p[0],-p[2],p[1])) for p in points];verts=[];faces=[]
    for i,p in enumerate(ps):
        tangent=(ps[min(i+1,len(ps)-1)]-ps[max(0,i-1)]).normalized()
        ref=Vector((1,0,0)) if abs(tangent.x)<.8 else Vector((0,1,0));u=tangent.cross(ref).normalized();v=tangent.cross(u)
        for j in range(sides):verts.append(p+r*(u*math.cos(j*math.tau/sides)+v*math.sin(j*math.tau/sides)))
    for i in range(len(ps)-1):
        for j in range(sides):a=i*sides+j;b=i*sides+(j+1)%sides;faces.append((a,b,b+sides,a+sides))
    faces.extend([tuple(reversed(range(sides))),tuple((len(ps)-1)*sides+j for j in range(sides))])
    me=bpy.data.meshes.new(name);me.from_pydata(verts,[],faces);me.update();o=bpy.data.objects.new(name,me);bpy.context.collection.objects.link(o);return finish(o,name,m)
def box(name,p,size,m,bevel=.01):
    bpy.ops.mesh.primitive_cube_add(size=1,location=(p[0],-p[2],p[1]));o=bpy.context.object;o.scale=(size[0],size[2],size[1]);bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    if bevel:
        mod=o.modifiers.new('Soft edges','BEVEL');mod.width=bevel;mod.segments=2
        bpy.context.view_layer.objects.active=o;bpy.ops.object.modifier_apply(modifier=mod.name)
    return finish(o,name,m)
def ring(name,z,r,minor,m,seg=32,cross=6):
    pts=[];faces=[]
    for i in range(seg):
        a=math.tau*i/seg
        for j in range(cross):
            b=math.tau*j/cross;rr=r+minor*math.cos(b)
            pts.append((minor*math.sin(b),-(z+rr*math.cos(a)),.34+rr*math.sin(a)))
    for i in range(seg):
        for j in range(cross):faces.append((i*cross+j,((i+1)%seg)*cross+j,((i+1)%seg)*cross+(j+1)%cross,i*cross+(j+1)%cross))
    me=bpy.data.meshes.new(name);me.from_pydata(pts,[],faces);me.update();o=bpy.data.objects.new(name,me);bpy.context.collection.objects.link(o);return finish(o,name,m)
for label,z in [('Front',.62),('Rear',-.60)]:
    wheel=[]
    wheel.append(ring('Wheel'+label+'_Tyre',z,.308,.032,rubber))
    wheel.append(ring('Wheel'+label+'_Rim',z,.277,.013,chrome,32,5))
    for i in range(16):
        a=math.tau*i/16
        wheel.append(tube('Wheel'+label+'_Spoke',[(.018 if i%2 else -.018,.34,z),(0,.34+.265*math.sin(a),z+.265*math.cos(a))],.0027,chrome,3))
    wheel.append(tube('Wheel'+label+'_Hub',[(-.07,.34,z),(.07,.34,z)],.022,chrome))
    # Join moving wheel parts with a hub-centred origin.
    bpy.ops.object.select_all(action='DESELECT')
    for o in wheel:o.select_set(True)
    bpy.context.view_layer.objects.active=wheel[0];bpy.ops.object.join();o=bpy.context.object;o.name='Wheel'+label
    bpy.context.scene.cursor.location=(0,-z,.34);bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    pts=[(0,.34+.365*math.sin(a),z+.365*math.cos(a)) for a in [math.radians(-12+i*204/18) for i in range(19)]]
    tube('Fender'+label,pts,.031,paint,6)
    for x in [-.045,.045]:tube('FenderStay',[(x,.34,z),(x,.57,z-.23)],.006,chrome,5)
# Curving low step-through frame and seat triangle.
tube('LowStepFrame',[(0,.86,.44),(0,.67,.36),(0,.44,.22),(0,.33,-.03),(0,.32,-.27)],.034,paint)
tube('UpperStepFrame',[(0,1.01,.39),(0,.84,.26),(0,.58,.02),(0,.49,-.18),(0,.51,-.28)],.029,paint)
tube('SeatTube',[(0,.30,-.25),(0,.84,-.39)],.032,paint)
tube('HeadTube',[(0,.77,.48),(0,1.05,.40)],.037,paint)
for x in [-.053,.053]:
    tube('ChainStay',[(x,.31,-.25),(x,.34,-.60)],.018,paint)
    tube('SeatStay',[(x,.78,-.375),(x,.34,-.60)],.018,paint)
    tube('FrontFork',[(x,.79,.47),(x,.43,.60),(x,.34,.62)],.022,paint)
tube('SeatPost',[(0,.79,-.385),(0,.96,-.43)],.016,chrome)
verts=[];faces=[]
for height,scale in [(.95,.85),(.98,1),(1.025,.80)]:
    for i in range(16):
        a=math.tau*i/16;z=-.40+math.cos(a)*.16*scale;x=math.sin(a)*(.085-.035*math.cos(a))*scale
        verts.append((x,-z,height))
for ring_index in range(2):
    for i in range(16):faces.append((ring_index*16+i,ring_index*16+(i+1)%16,(ring_index+1)*16+(i+1)%16,(ring_index+1)*16+i))
faces.extend([tuple(reversed(range(16))),tuple(range(32,48))])
me=bpy.data.meshes.new('Saddle');me.from_pydata(verts,[],faces);me.update();o=bpy.data.objects.new('Saddle',me);bpy.context.collection.objects.link(o);finish(o,'Saddle',leather)
tube('HandleStem',[(0,1.04,.40),(0,1.19,.36)],.019,chrome)
tube('Handlebar',[(-.32,1.20,.20),(-.23,1.22,.23),(-.15,1.23,.37),(0,1.22,.39),(.15,1.23,.37),(.23,1.22,.23),(.32,1.20,.20)],.014,chrome)
for side in [-1,1]:
    tube('Grip',[(side*.23,1.22,.23),(side*.33,1.20,.19)],.024,leather)
    tube('BrakeLever',[(side*.21,1.21,.23),(side*.30,1.17,.28)],.009,chrome,6)
    tube('BrakeCable',[(side*.21,1.2,.24),(side*.16,1.06,.52),(side*.04,.69,.63)],.0035,dark,4)
box('ChainGuard',(-.061,.337,-.35),(.034,.17,.57),paint,.055)
for side in [-1,1]:
    tube('Crank',[(side*.078,.32,-.24),(side*.078,.20 if side<0 else .44,-.33 if side<0 else -.15)],.013,chrome,6)
    box('Pedal',(side*.125,.20 if side<0 else .44,-.33 if side<0 else -.15),(.12,.027,.085),dark,.008)
    box('PedalReflector',(side*.125,.20 if side<0 else .44,-.375 if side<0 else -.105),(.074,.015,.004),amber,.002)
# Rear rack, lamp and reflectors.
for x in [-.10,.10]:
    tube('RackSide',[(x,.83,-.28),(x,.83,-.84)],.014,paint)
    tube('RackSupport',[(x,.34,-.60),(x,.82,-.78)],.010,chrome,6)
for z in [-.30,-.43,-.57,-.71,-.84]:tube('RackCross',[(-.10,.83,z),(.10,.83,z)],.011,paint,6)
box('TailLight',(0,.61,-.87),(.065,.065,.035),red,.012)
box('Headlight',(0,.83,.78),(.09,.095,.075),chrome,.025)
box('HeadlightLens',(0,.83,.821),(.073,.072,.009),lamp,.02)
tube('Kickstand',[(-.06,.32,-.33),(-.14,.025,-.46)],.011,dark,6)
# Open wicker basket, visible crossing weave and sturdy rounded lip.
def basket_ring(y,w,d):
    return [(-w,y,.75-d),(-w,y,.75+d),(w,y,.75+d),(w,y,.75-d),(-w,y,.75-d)]
for i in range(9):
    t=i/8;y=.91+t*.26
    tube('BasketWeaveHorizontal',basket_ring(y,.15+.035*t,.105+.025*t),.0045,wicker,4)
for side in [-1,1]:
    for i in range(13):
        t=-1+i/6
        tube('BasketWeaveVertical',[(side*.15,.91,.75+t*.105),(side*.185,1.17,.75+t*.13)],.004,wicker,4)
    for i in range(17):
        t=-1+i/8
        tube('BasketWeaveVertical',[(t*.15,.91,.75+side*.105),(t*.185,1.17,.75+side*.13)],.004,wicker,4)
tube('BasketRim',basket_ring(1.175,.185,.13),.012,wicker,6)
box('BasketBase',(0,.91,.75),(.3,.015,.21),wicker,.008)
for x in [-.09,.09]:tube('BasketMount',[(x,.91,.75),(x,.84,.56)],.009,dark,6)
# Combine fixed parts by material; keep wheel hubs and stand as independent pivots.
groups={}
for o in list(bpy.context.scene.objects):
    if o.type=='MESH' and o.name not in ['WheelFront','WheelRear','Kickstand']:
        groups.setdefault(o.data.materials[0].name,[]).append(o)
for material,objects in groups.items():
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects:o.select_set(True)
    bpy.context.view_layer.objects.active=objects[0];bpy.ops.object.join();o=bpy.context.object;o.name='Body_'+material
    bpy.context.scene.cursor.location=(0,0,0);bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
# Apply object transforms without losing wheel pivots. Export all meshes only.
bpy.ops.object.select_all(action='DESELECT')
models=[o for o in bpy.context.scene.objects if o.type=='MESH']
for o in models:o.select_set(True)
bpy.context.view_layer.objects.active=models[0]
bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
tris=sum(len(p.vertices)-2 for o in models for p in o.data.polygons)
bpy.ops.wm.save_as_mainfile(filepath=SRC+'/CityBicycle.blend')
bpy.ops.export_scene.fbx(filepath=OUT+'/Models/CityBicycle.fbx',use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',global_scale=1,apply_unit_scale=True,add_leaf_bones=False,bake_anim=False)
json.dump({'triangles':tris,'variants':[{'id':n,'name':k,'color':h} for n,k,h in PALETTE]},open(SRC+'/manifest.json','w',encoding='utf8'),ensure_ascii=False,indent=2)
# Product images are rendered from the actual game model.
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=24
scene.render.resolution_x=512;scene.render.resolution_y=512;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGBA';scene.render.film_transparent=True
scene.world.color=(.5,.5,.5)
def area(name,p,power,size):
    bpy.ops.object.light_add(type='AREA',location=p);o=bpy.context.object;o.name=name;o.data.energy=power;o.data.shape='DISK';o.data.size=size;o.rotation_euler=(Vector((0,0,.6))-o.location).to_track_quat('-Z','Y').to_euler()
area('Key',(3,-4,5),450,4);area('Fill',(-3,-1,3),300,3);area('Rim',(1,4,4),400,3)
bpy.ops.object.camera_add(location=(2.8,-3.4,2.1));cam=bpy.context.object;cam.rotation_euler=(Vector((0,0,.65))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=2.22;scene.camera=cam
scene.view_settings.view_transform='AgX'
for n,k,h in PALETTE:
    paint.diffuse_color=color(h);paint.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=color(h)
    scene.render.filepath=OUT+'/Icons/CityBicycle_'+n+'.png';bpy.ops.render.render(write_still=True)
paint.diffuse_color=color(PALETTE[0][2]);paint.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=color(PALETTE[0][2])
scene.render.resolution_x=1200;scene.render.resolution_y=1000;scene.render.filepath=SRC+'/Preview.png';bpy.ops.render.render(write_still=True)
print('CITY_BICYCLE_DONE triangles='+str(tris))
