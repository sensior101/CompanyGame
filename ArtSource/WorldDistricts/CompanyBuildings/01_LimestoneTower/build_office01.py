"""Office building 01: reference-led first proportional model, metres, Blender -Y front.

Reuses the existing Handae MeshBatch, box and loft authoring helpers without running
its scene-building code. This recipe and its outputs stay outside Unity Assets.
"""
import ast
import bpy
import bmesh
import json
import math
import sys
from pathlib import Path
from mathutils import Vector

HERE = Path(__file__).resolve().parent
EXPORT = HERE / 'UnityExport'
REVIEW = HERE / 'BlockoutRenders'
EXPORT.mkdir(parents=True, exist_ok=True)
REVIEW.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version = 0
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1.0
scene.unit_settings.length_unit = 'METERS'

COLS = {}
for name in ['Structure', 'Podium', 'Glass', 'Frames', 'Entrance', 'RoofDetails', 'GroundDetails', '_Presentation']:
    c = bpy.data.collections.new(name)
    scene.collection.children.link(c)
    COLS[name] = c
ROOT = bpy.data.objects.new('Office01_LimestoneTower', None)
COLS['Structure'].objects.link(ROOT)
ROOT['units'] = '1 Blender unit = 1 metre'
ROOT['front'] = 'Blender -Y; FBX -Z forward / Y up'
ROOT['stage'] = 'Proportional first model for review'
ROOT['target_height_m'] = 148.0
ROOT['handae_reference_height_m'] = 160.012

def material(name, color, roughness=.55, metallic=0, emission=0):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    p = m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value = (*color, 1)
    p.inputs['Roughness'].default_value = roughness
    p.inputs['Metallic'].default_value = metallic
    if emission:
        p.inputs['Emission Color'].default_value = (*color, 1)
        p.inputs['Emission Strength'].default_value = emission
    return m

MATS = {
    'Limestone': material('Office01_Limestone', (.66, .56, .40), .66),
    'StoneShadow': material('Office01_StoneShadow', (.40, .34, .25), .8),
    'DarkMetal': material('Office01_DarkMetal', (.055, .063, .070), .33, .68),
    'GlassCool': material('Office01_GlassCool', (.095, .17, .215), .22, .60),
    'GlassDark': material('Office01_GlassDark', (.027, .050, .068), .26, .52),
    'GlassWarm': material('Office01_GlassWarm', (.28, .18, .074), .31, .30, .22),
    'Terracotta': material('Office01_TerracottaFins', (.40, .12, .042), .55, .12),
    'Roof': material('Office01_Roof', (.18, .19, .18), .88),
    'Paving': material('Office01_Paving', (.37, .36, .32), .86),
    'Door': material('Office01_ServiceDoor', (.17, .18, .18), .62, .42),
}
helper_path = HERE.parents[1] / 'HandaeHQ' / 'v02' / 'build_handae.py'
source = ast.parse(helper_path.read_text(encoding='utf-8'))
names = {'MeshBatch', 'box', 'loft'}
nodes = [n for n in source.body if isinstance(n, (ast.FunctionDef, ast.ClassDef)) and n.name in names]
if {n.name for n in nodes} != names:
    raise RuntimeError('Existing Handae authoring helpers changed; review before regenerating.')
exec(compile(ast.Module(body=nodes, type_ignores=[]), str(helper_path), 'exec'), globals())

def finish(name, collection, batch, materials):
    return batch.object(name, [collection], materials)

def corners(cx, cy, width, depth, z):
    return [(cx-width/2, cy-depth/2, z), (cx+width/2, cy-depth/2, z),
            (cx+width/2, cy+depth/2, z), (cx-width/2, cy+depth/2, z)]

# Constant 48 x 40 m shaft. Only the roof edge steps; no pyramidal setbacks.
TX0, TX1, TY0, TY1 = -36., 12., -28., 12.
PODIUM_TOP = 22.
def tower_top(x, y):
    west_step = 6.4 if x < -20.001 else 0.
    rear_step = 2.2 if y > 2.001 else 0.
    return 147.0 - west_step - rear_step

mass = MeshBatch()
for xa, xb in [(TX0, -20), (-20, TX1)]:
    for ya, yb in [(TY0, 2), (2, TY1)]:
        height = tower_top((xa+xb)/2, (ya+yb)/2)
        box(mass, ((xa+xb)/2, (ya+yb)/2, (height+PODIUM_TOP)/2),
            (xb-xa-1.2, yb-ya-1.2, height-PODIUM_TOP))
finish('Tower_ShadowBody', 'Structure', mass, ['GlassDark'])

# Four genuine outer elevations: projecting stone piers, recessed window bands.
ribs, windows, frames, crossbands, crown = [MeshBatch() for _ in range(5)]
def facade(axis, plane, lo, hi, normal, divisions):
    interval = (hi-lo)/divisions
    for i in range(divisions+1):
        u = lo+i*interval
        x, y = (u, plane) if axis=='x' else (plane, u)
        top = tower_top(min(TX1-.1, max(TX0+.1, x)), min(TY1-.1, max(TY0+.1, y)))
        width = 1.18 if i%4==0 or i in (0, divisions) else .83
        depth = 1.02
        if axis=='x':
            position=(u, plane+normal*.45, (PODIUM_TOP+top)/2)
            size=(width, depth, top-PODIUM_TOP)
            cx, cy, cw, cd = u, plane+normal*.45, width, depth
        else:
            position=(plane+normal*.45, u, (PODIUM_TOP+top)/2)
            size=(depth, width, top-PODIUM_TOP)
            cx, cy, cw, cd = plane+normal*.45, u, depth, width
        box(ribs, position, size)
        tip = corners(cx, cy, cw, cd, top)
        cap = corners(cx, cy, cw*.5, cd*.52, top+1.)
        loft(crown, [tip, cap])
    for i in range(divisions):
        a, b = lo+i*interval+.58, lo+(i+1)*interval-.58
        middle=(a+b)/2
        x, y=(middle,plane) if axis=='x' else (plane,middle)
        top=tower_top(x,y)-.45
        for floor in range(32):
            lower=PODIUM_TOP+.42+floor*4.
            upper=min(lower+3.45,top)
            if upper-lower<.7: continue
            # Closed shallow panels avoid backface ambiguity on FBX export.
            tint = 2 if (i*7+floor*11+(1 if normal>0 else 0))%17==0 else (floor+i)%2
            if axis=='x':
                box(windows,(middle,plane-normal*.18,(lower+upper)/2),(b-a,.10,upper-lower),tint)
                box(frames,(middle,plane-normal*.08,lower-.16),(b-a,.14,.24))
                box(frames,(middle,plane-normal*.06,(lower+upper)/2),(.11,.16,upper-lower))
            else:
                box(windows,(plane-normal*.18,middle,(lower+upper)/2),(.10,b-a,upper-lower),tint)
                box(frames,(plane-normal*.08,middle,lower-.16),(.14,b-a,.24))
                box(frames,(plane-normal*.06,middle,(lower+upper)/2),(.16,.11,upper-lower))
    # Sparse 6–8-storey stone bridges, intentionally not continuous floor stripes.
    for z, first, count in [(48.,0,4),(77.,4,4),(106.,0,4),(130.,8,divisions-8)]:
        if count<=0 or first>=divisions: continue
        span=min(count,divisions-first)*interval
        u=lo+first*interval+span/2
        if axis=='x': box(crossbands,(u,plane+normal*.36,z),(span,1.04,1.5))
        else: box(crossbands,(plane+normal*.36,u,z),(1.04,span,1.5))

facade('x', TY0, TX0, TX1, -1, 12)
facade('x', TY1, TX0, TX1, 1, 12)
facade('y', TX0, TY0, TY1, -1, 10)
facade('y', TX1, TY0, TY1, 1, 10)
finish('Tower_LimestoneVerticalPiers', 'Structure', ribs, ['Limestone'])
finish('Tower_RecessedGlazing', 'Glass', windows, ['GlassCool','GlassDark','GlassWarm'])
finish('Tower_WindowFrames', 'Frames', frames, ['DarkMetal'])
finish('Tower_StoneBridgeBands', 'Structure', crossbands, ['Limestone'])
finish('Tower_CrownRibTips', 'RoofDetails', crown, ['Limestone'])

roof = MeshBatch()
for xa, xb in [(TX0+.7,-20),(-20,TX1-.7)]:
    for ya, yb in [(TY0+.7,2),(2,TY1-.7)]:
        z=tower_top((xa+xb)/2,(ya+yb)/2)
        box(roof,((xa+xb)/2,(ya+yb)/2,z+.01),(xb-xa,yb-ya,.12))
# A low screened service head sits below the highest roofline.
box(roof,(-8,-7,146.1),(12,10,1.1))
finish('Tower_RoofPlanes', 'RoofDetails', roof, ['Roof'])

# 88 x 64 m podium, high ground floor and two upper commercial floors.
stone, podium_glass, podium_frames, orange, lobby, service = [MeshBatch() for _ in range(6)]
for z,h in [(0.16,.32),(7.5,.72),(14.5,.55),(21.3,1.4)]:
    # The middle slab stays behind the uninterrupted side screens.
    box(stone,(0,0,z),(85.4 if z==14.5 else 88,61.4 if z==14.5 else 64,h))
for x in [-42,-31.5,-21,-10.5,0,10.5,21,31.5,42]:
    for y in [-30.2,30.2]: box(stone,(x,y,3.75),(1.15,1.65,7.5))
for y in [-21,-10.5,0,10.5,21]:
    for x in [-42,42]: box(stone,(x,y,3.75),(1.65,1.15,7.5))

# Front and rear glazed galleries; ground floor is recessed behind the colonnade.
for side in [-1,1]:
    y=side*31.6
    for i in range(32):
        pitch=87.4/32
        x=-43.7+(i+.5)*pitch
        for low, high in [(7.88,14.2),(14.78,20.62)]:
            tint=0 if (i+int(low))%5 else 1
            box(podium_glass,(x,y,(low+high)/2),(pitch-.16,.14,high-low),tint)
        box(podium_frames,(x-pitch/2,y+side*.12,14.35),(.20,.32,12.9))
    box(podium_frames,(43.7,y+side*.12,14.35),(.20,.32,12.9))
    for z in [7.78,14.5,21.02,11.,17.8]:
        box(podium_frames,(0,y+side*.15,z),(87.4,.34,.38 if z in [7.78,14.5,21.02] else .16))
    for z,h in [(7.5,1.1),(14.5,.82)]:
        box(podium_frames,(0,side*31.86,z),(88,.28,h))
    for i in range(16):
        x=-40+i*5.32
        box(lobby,(x,side*28.1,3.65),(5.14,.16,6.65),i%2)
        box(podium_frames,(x-2.6,side*28.22,3.65),(.18,.24,6.7))

# Adjacent elevation's warm terracotta brise-soleil in a cream stone portal.
for side in [-1,1]:
    x=side*43.0
    box(podium_glass,(x,0,14.3),(.15,53,12.5),0)
    for y in [-28,28]: box(stone,(side*43.0,y,14.45),(1.9,3.2,15.1))
    for z in [7.9,21.2]: box(stone,(side*43.0,0,z),(1.9,59,1.6))
    for j in range(25):
        y=-25.2+j*2.1
        box(orange,(side*43.48,y,14.45),(.72,.24,11.8))
    for z in [8.5,14.5,20.3]: box(podium_frames,(side*43.38,0,z),(.28,53,.19))
    for j in range(10):
        y=-25+j*5.55
        box(lobby,(side*39.7,y,3.65),(.16,5.3,6.6),j%2)
        box(podium_frames,(side*39.83,y-2.7,3.65),(.24,.16,6.6))

finish('Podium_StoneColonnadeAndSlabs','Podium',stone,['Limestone'])
finish('Podium_GlassGallery','Glass',podium_glass,['GlassCool','GlassWarm'])
finish('Podium_MetalGrid','Frames',podium_frames,['DarkMetal'])
finish('Podium_TerracottaScreens','Podium',orange,['Terracotta'])
finish('Ground_RecessedLobbyGlass','Glass',lobby,['GlassDark','GlassWarm'])

entries, entrance_glass = MeshBatch(), MeshBatch()
# Readable main double-door location under the tower side of the podium.
for x in [-21.1,-17.8,-14.5]: box(entries,(x,-28.5,2.4),(.19,.27,4.65))
box(entries,(-17.8,-28.5,4.72),(6.8,.27,.21))
for x in [-19.45,-16.15]:
    box(entrance_glass,(x,-28.42,2.4),(3.08,.1,4.4))
    box(entries,(x+(.9 if x<-17.8 else -.9),-28.69,2.1),(.09,.09,1.1))
box(entries,(-17.8,-29.25,5.0),(8,3.4,.25))
finish('MainEntrance_FramesAndCanopy','Entrance',entries,['DarkMetal'])
finish('MainEntrance_DoorPanels','Entrance',entrance_glass,['GlassCool'])
# Recessed loading doors on the rear corner, preserved as simple massing.
for x in [26.25,36.75]:
    box(service,(x,29.8,2.8),(8.5,.35,5.3))
    for z in [i*.42+.5 for i in range(12)]: box(service,(x,30.02,z),(8.3,.08,.06),1)
finish('Rear_ServiceShutters','Entrance',service,['Door','DarkMetal'])

terrace = MeshBatch()
box(terrace,(0,0,21.98),(85.9,61.9,.12))
finish('Podium_RoofTerrace','RoofDetails',terrace,['Roof'])

# Root markers carry future integration meaning; no gameplay components are added.
for name, point in [('FrontAnchor',(0,-40,0)),('EntranceAnchor',(-17.8,-30,0)),('ServiceAnchor',(28.5,32,0))]:
    ob=bpy.data.objects.new(name,None);COLS['Entrance'].objects.link(ob);ob.parent=ROOT;ob.location=point
    ob.empty_display_size=1.0;ob.empty_display_type='ARROWS'

model_meshes=[o for o in ROOT.children_recursive if o.type=='MESH']
# Modelled dimensions already fit the envelope; preserve intact closed volumes.
bpy.context.view_layer.update()

# Neutral architectural presentation is not in the export hierarchy.
ground=MeshBatch();box(ground,(0,0,-.42),(1800,1800,.65))
ground_ob=ground.object('_StudioGround',['_Presentation'],['Paving'],parent=None)
ground_ob['export_asset']=False
world=bpy.data.worlds.new('Office01_StudioSky');world.use_nodes=True
world.node_tree.nodes['Background'].inputs[0].default_value=(.44,.57,.72,1)
world.node_tree.nodes['Background'].inputs[1].default_value=.50
scene.world=world
def light(name,position,power,size,color):
    data=bpy.data.lights.new(name,'AREA');data.energy=power;data.shape='DISK';data.size=size;data.color=color
    ob=bpy.data.objects.new(name,data);COLS['_Presentation'].objects.link(ob);ob.location=position
    ob.rotation_euler=(Vector((0,0,65))-ob.location).to_track_quat('-Z','Y').to_euler()
light('Key',(-120,-180,210),480000,100,(1,.87,.71))
light('Fill',(150,-50,140),310000,120,(.64,.81,1))
light('Rim',(10,130,185),420000,110,(1,.94,.83))
sun_data=bpy.data.lights.new('Sun','SUN');sun_data.energy=2.0;sun_data.angle=.12
sun=bpy.data.objects.new('Sun',sun_data);COLS['_Presentation'].objects.link(sun)
sun.rotation_euler=(.51,-.38,-.52)

def camera(name,position,target,ortho=None):
    data=bpy.data.cameras.new(name);ob=bpy.data.objects.new(name,data);COLS['_Presentation'].objects.link(ob)
    ob.location=position;ob.rotation_euler=(Vector(target)-ob.location).to_track_quat('-Z','Y').to_euler()
    data.clip_end=2500;data.lens=49
    if ortho:data.type='ORTHO';data.ortho_scale=ortho
    return ob
CAMS={
    'front_corner':camera('Camera_FrontCorner',(165,-215,105),(-4,0,71)),
    'rear_corner':camera('Camera_RearCorner',(-185,190,102),(-4,0,72)),
    'front_elevation':camera('Camera_FrontElevation',(-4,-350,74),(-4,0,74),171),
    'side_elevation':camera('Camera_SideElevation',(350,0,74),(0,0,74),171),
    'street_corner':camera('Camera_StreetCorner',(180,-230,15),(-4,0,71)),
}
scene.render.engine='CYCLES';scene.cycles.samples=32;scene.cycles.use_denoising=True
scene.cycles.max_bounces=5;scene.cycles.diffuse_bounces=2;scene.cycles.glossy_bounces=3
scene.cycles.device='CPU'
try:
    cp=bpy.context.preferences.addons['cycles'].preferences
    cp.compute_device_type='OPTIX';cp.get_devices()
    gpu=[d for d in cp.devices if d.type=='OPTIX']
    if gpu:
        for d in cp.devices:d.use=d.type=='OPTIX'
        scene.cycles.device='GPU'
except Exception:
    pass
scene.view_settings.view_transform='AgX';scene.view_settings.exposure=.30
scene.render.resolution_x=1400;scene.render.resolution_y=1700;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
scene.camera=CAMS['front_corner']

points=[o.matrix_world@Vector(corner) for o in model_meshes for corner in o.bound_box]
mins=[min(p[i] for p in points) for i in range(3)];maxs=[max(p[i] for p in points) for i in range(3)]
dimensions=[round(maxs[i]-mins[i],4) for i in range(3)]
triangles=sum(len(o.data.polygons) for o in model_meshes)
assert abs(maxs[2]-148)<.01, maxs
assert dimensions[0]<=88.01 and dimensions[1]<=64.01, dimensions
bad_faces=[(o.name,p.index,len(p.vertices),p.area) for o in model_meshes for p in o.data.polygons if len(p.vertices)!=3 or p.area<=1e-10]
assert not bad_faces, bad_faces[:20]
manifest={
    'asset':'Office01_LimestoneTower','stage':'First proportional model / review',
    'building_dimensions_xyz_m':dimensions,'podium_height_m':22.0,'tower_plan_m':[48,40],
    'handae_dimensions_xyz_m':[89.584,63.959,160.012],'height_ratio':round(148/160.012,4),
    'meshes':len(model_meshes),'triangles':triangles,'materials':sorted({m.name for o in model_meshes for m in o.data.materials}),
    'unit':'metre','front':'Blender -Y','topology':'explicit triangles, outward normals, metric UV0',
    'source_helpers':str(helper_path.relative_to(HERE.parents[3])),
    'scope':'Exterior first model. No full interior; no Unity scene or runtime changes.',
    'reference_features':['Constant rectangular shaft','Deep cream stone piers','Recessed dark glazing',
        'Mild crown steps and projecting tips','Sparse stone bridges','Dark glazed podium face',
        'Terracotta side screens','Tall recessed ground-floor entrance','Rear loading shutters'],
    'pending_detail':['Stone panel seams and final texture treatment','Entrance signage and company identity','Final roof/service detailing']
}
(HERE/'model_manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
blend_path=HERE/'Office01_LimestoneTower_Blockout.blend'
bpy.ops.wm.save_as_mainfile(filepath=str(blend_path))
bpy.ops.object.select_all(action='DESELECT');ROOT.select_set(True)
for obj in ROOT.children_recursive:obj.select_set(True)
bpy.context.view_layer.objects.active=ROOT
bpy.ops.export_scene.fbx(filepath=str(EXPORT/'Office01_LimestoneTower_Blockout.fbx'),use_selection=True,
    global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',axis_forward='-Z',axis_up='Y',
    object_types={'MESH','EMPTY'},use_mesh_modifiers=True,mesh_smooth_type='FACE',use_tspace=True,
    add_leaf_bones=False,bake_anim=False,path_mode='RELATIVE')
bpy.ops.export_scene.gltf(filepath=str(EXPORT/'Office01_LimestoneTower_Blockout.glb'),export_format='GLB',use_selection=True,export_yup=True,export_apply=True)
if '--skip-render' not in sys.argv:
    for name in ['front_corner','rear_corner','front_elevation','street_corner']:
        scene.camera=CAMS[name];scene.render.filepath=str(REVIEW/(name+'.png'))
        bpy.ops.render.render(write_still=True)
scene.camera=CAMS['front_corner']
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.region_3d.view_distance=220
            area.spaces.active.region_3d.view_location=(0,0,70)
bpy.ops.wm.save_as_mainfile(filepath=str(blend_path))
print('OFFICE01_COMPLETE '+json.dumps(manifest,ensure_ascii=False))
