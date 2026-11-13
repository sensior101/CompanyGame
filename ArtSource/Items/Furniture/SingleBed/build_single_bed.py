"""Isolated Blender authoring recipe. Metres, Z up; shared geometry, five palettes.
Uses the LibraryKiosk metre/asset-only FBX export conventions. No runtime logic.
Run Blender --background --factory-startup --python this_file.
"""
import bpy, math, json, hashlib
from pathlib import Path
from mathutils import Vector

HERE = Path(__file__).resolve().parent
OUT = HERE / 'Exports'
QA = HERE / 'QA'
OUT.mkdir(exist_ok=True); QA.mkdir(exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1
asset = bpy.data.collections.new('SingleBed_ASSET')
studio = bpy.data.collections.new('Studio_NOT_EXPORTED')
scene.collection.children.link(asset); scene.collection.children.link(studio)
root = bpy.data.objects.new('SingleBed', None); asset.objects.link(root)
root['FurnitureFunction'] = 'Sleep'; root['FurniturePlacement'] = 'Floor'
root['MattressMetres'] = [1.0, 2.0, .18]
root['CharacterHeightMetres'] = 1.8
root['PaletteUsage'] = 'Swap materials only. All five exports have identical geometry.'

def move(o, collection):
    for c in list(o.users_collection): c.objects.unlink(o)
    collection.objects.link(o)

def linear(v):
    return v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4

def rgba(h):
    return tuple(linear(int(h[i:i+2],16)/255) for i in (0,2,4)) + (1,)

slots = ['Frame','Mattress','Duvet','Fold','Pillow']
palettes = {
    '01_Cream': ['AD8057','F2EDE2','D6C6A8','EDE1C9','F8F1E5'],
    '02_Sage': ['B5A083','EFEDE5','798F7C','B4C2A2','E6E9D9'],
    '03_Blue': ['77614F','E4E8ED','526F91','9CAFC7','DFE7F0'],
    '04_Rose': ['C1A38B','F2E8E4','AD7683','D7ADB1','F4E2E0'],
    '05_Charcoal': ['44464A','D9DADD','555C67','9499A2','D6D8DF'],
}
mats = {}
for palette, colors in palettes.items():
    mats[palette] = []
    for slot, color in zip(slots, colors):
        m = bpy.data.materials.new('SingleBed_'+palette+'_'+slot)
        m.use_nodes = True; m.diffuse_color = rgba(color)
        p = m.node_tree.nodes['Principled BSDF']
        p.inputs['Base Color'].default_value = rgba(color)
        p.inputs['Roughness'].default_value = .72 if slot == 'Frame' else .9
        mats[palette].append(m)

def finish(o, name, slot):
    o.name=name; move(o,asset); o.parent=root
    o.data.materials.append(mats['01_Cream'][slots.index(slot)])
    o['MaterialRole']=slot
    return o

def box(name, size, loc, slot, bevel=.012):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    o=bpy.context.object; o.scale=size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    finish(o,name,slot)
    if bevel:
        mod=o.modifiers.new('One_segment_soft_edges','BEVEL'); mod.width=bevel; mod.segments=1
        bpy.ops.object.modifier_apply(modifier=mod.name)
    return o

# Legs and rails: open space below, no baked-in room or collision mesh.
for x in (-.485,.485):
    for y in (-.91,.91): box('Leg',(.09,.09,.27),(x,y,.135),'Frame',.009)
for x in (-.53,.53): box('SideRail',(.06,2.02,.17),(x,0,.285),'Frame',.01)
box('FootRail',(1.12,.065,.17),(0,-1.025,.285),'Frame')
box('HeadSupport',(1.03,.065,.16),(0,1.025,.285),'Frame')
box('MattressSupport',(.995,1.98,.045),(0,0,.3225),'Frame',.005)
box('Headboard',(1.12,.075,.52),(0,1.025,.66),'Frame',.025)
box('Mattress',(1,2,.18),(0,0,.435),'Mattress',.04)

# Pillow: sparse chamfered rings with a raised centre, visibly soft but faceted.
def pillow():
    outline=[(-.5,-.32),(-.39,-.5),(.39,-.5),(.5,-.32),(.5,.32),(.39,.5),(-.39,.5),(-.5,.32)]
    verts=[]
    for scale,z in ((.87,.535),(1,.575),(.77,.63),(.37,.655)):
        verts += [(x*.68*scale,y*.40*scale+.70,z) for x,y in outline]
    faces=[tuple(reversed(range(8)))]
    for r in range(3):
        for i in range(8): faces.append((r*8+i,r*8+(i+1)%8,(r+1)*8+(i+1)%8,(r+1)*8+i))
    faces.append(tuple(range(24,32)))
    mesh=bpy.data.meshes.new('PillowMesh'); mesh.from_pydata(verts,[],faces); mesh.update()
    o=bpy.data.objects.new('Pillow',mesh); asset.objects.link(o); finish(o,'Pillow','Pillow')
pillow()

# Duvet shell follows mattress top and drops down both sides and at the foot.
xs=[-.55,-.535,-.49,-.43,-.23,0,.23,.43,.49,.535,.55]
ys=[-1.055,-1.04,-1.005,-.96,-.82,-.55,-.25,.05,.32,.43]
def blanket_z(x,y):
    ax=abs(x)
    edge=.0 if ax<=.43 else (.015 if ax<=.49 else (.105 if ax<=.535 else .19))
    foot=max(0,(-y-1.0)/.055)*.17
    swell=.012*math.cos(x*5)*math.sin((y+1)*3)
    return .557-max(edge,foot)+swell
verts=[(x,y,blanket_z(x,y)) for y in ys for x in xs]
faces=[]
for j in range(len(ys)-1):
    for i in range(len(xs)-1):
        a=j*len(xs)+i; faces.append((a,a+1,a+len(xs)+1,a+len(xs)))
mesh=bpy.data.meshes.new('DuvetMesh'); mesh.from_pydata(verts,[],faces); mesh.update()
o=bpy.data.objects.new('Duvet',mesh); asset.objects.link(o); finish(o,'Duvet','Duvet')
bpy.context.view_layer.objects.active=o
sol=o.modifiers.new('Fabric_thickness','SOLIDIFY'); sol.thickness=.012
bpy.ops.object.modifier_apply(modifier=sol.name)
# Folded-back cuff is a separate colour region, following the same side drape.
verts=[(x,y,blanket_z(x,y)+.022) for y in (.255,.285,.435,.455) for x in xs]
faces=[]
for j in range(3):
    for i in range(len(xs)-1):
        a=j*len(xs)+i; faces.append((a,a+1,a+len(xs)+1,a+len(xs)))
mesh=bpy.data.meshes.new('FoldMesh'); mesh.from_pydata(verts,[],faces); mesh.update()
o=bpy.data.objects.new('Fold',mesh); asset.objects.link(o); finish(o,'Fold','Fold')
bpy.context.view_layer.objects.active=o
sol=o.modifiers.new('Fold_thickness','SOLIDIFY'); sol.thickness=.014
bpy.ops.object.modifier_apply(modifier=sol.name)

meshes=[o for o in asset.objects if o.type=='MESH']
# Export stable triangulation and usable UVs; no texture files required.
for o in meshes:
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active=o
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(island_margin=.025); bpy.ops.object.mode_set(mode='OBJECT')
    mod=o.modifiers.new('Stable_triangulation','TRIANGULATE'); bpy.ops.object.modifier_apply(modifier=mod.name)

def palette_set(name):
    for o in meshes: o.data.materials[0]=mats[name][slots.index(o['MaterialRole'])]
    root['Palette']=name

def geometry_hash():
    data=[(o.name,[list(v.co) for v in o.data.vertices],[list(p.vertices) for p in o.data.polygons]) for o in meshes]
    return hashlib.sha256(json.dumps(data).encode()).hexdigest()

bpy.context.view_layer.update()
points=[o.matrix_world@Vector(v) for o in meshes for v in o.bound_box]
lo=[min(p[i] for p in points) for i in range(3)]; hi=[max(p[i] for p in points) for i in range(3)]
report={'units':'metres','dimensions_xyz':[hi[i]-lo[i] for i in range(3)],'bounds_min':lo,'bounds_max':hi,
        'triangles':sum(len(o.data.polygons) for o in meshes),'mesh_count':len(meshes),'material_slots':slots,
        'mattress_size':[1,2,.18],'player_prefab_height':1.8,'palettes_srgb':palettes,'geometry_sha256':geometry_hash(),
        'unity_runtime_verified':False,'exports':[]}
assert report['triangles']<2500
assert abs(lo[2])<.0001
for palette in palettes:
    palette_set(palette)
    bpy.ops.object.select_all(action='DESELECT')
    for o in asset.objects: o.select_set(True)
    bpy.context.view_layer.objects.active=root
    base='SingleBed_'+palette
    bpy.ops.export_scene.fbx(filepath=str(OUT/(base+'.fbx')),use_selection=True,object_types={'MESH','EMPTY'},
        axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',
        use_custom_props=True,add_leaf_bones=False,bake_anim=False)
    bpy.ops.export_scene.gltf(filepath=str(OUT/(base+'.glb')),use_selection=True,export_format='GLB')
    assert geometry_hash()==report['geometry_sha256']
    report['exports'].append(base)

# Render studio, deliberately excluded from exports.
palette_set('01_Cream')
floor=box('StudioFloor',(200,200,.04),(0,0,-.025),'Mattress',0)
floor.parent=None; move(floor,studio)
for name,loc,power,size in [('Key',(-3,-4,6),850,4),('Fill',(4,-1,4),550,3),('Rim',(0,4,5),800,3)]:
    d=bpy.data.lights.new(name,'AREA'); d.energy=power; d.shape='DISK'; d.size=size
    o=bpy.data.objects.new(name,d); studio.objects.link(o); o.location=loc
    o.rotation_euler=(Vector((0,0,.4))-o.location).to_track_quat('-Z','Y').to_euler()
world=bpy.data.worlds.new('StudioWorld'); world.use_nodes=True
world.node_tree.nodes['Background'].inputs[0].default_value=(.35,.35,.35,1)
scene.world=world
camera=bpy.data.objects.new('PreviewCamera',bpy.data.cameras.new('PreviewCamera')); studio.objects.link(camera)
camera.data.type='ORTHO'; scene.camera=camera
scene.render.engine='CYCLES'; scene.cycles.samples=24; scene.cycles.use_denoising=True
scene.render.resolution_x=1000; scene.render.resolution_y=900; scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'; scene.view_settings.view_transform='AgX'
def render(name,loc=(3,-4,3),target=(0,0,.4),scale=3.1):
    camera.location=loc; camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.ortho_scale=scale; scene.render.filepath=str(QA/(name+'.png'))
    bpy.ops.render.render(write_still=True)
for palette in palettes:
    palette_set(palette); render(palette)
palette_set('01_Cream')
render('Rear',(-3,4,2.5)); render('Top',(0,-.01,5),scale=2.7)

# Load actual authored character for a size check, only in QA collection.
character_path=HERE.parents[3]/'CompanyGame/Assets/Art/Daldongne/Players/MeshyMale/MaleArticulated.fbx'
if character_path.exists():
    reference=bpy.data.collections.new('CharacterReference_QA_ONLY'); scene.collection.children.link(reference)
    before=set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(character_path))
    refs=[o for o in bpy.data.objects if o not in before]
    for o in refs: move(o,reference)
    bpy.context.view_layer.update()
    pts=[o.matrix_world@Vector(c) for o in refs if o.type=='MESH' for c in o.bound_box]
    mn=Vector([min(p[i] for p in pts) for i in range(3)]); mx=Vector([max(p[i] for p in pts) for i in range(3)])
    report['source_character_bounds']={'min':list(mn),'max':list(mx),'height':mx.z-mn.z}
    assert abs(mx.z-mn.z-1.8)<.02, report['source_character_bounds']
    # Authored source is metre scale; preserve size and move only its root objects.
    delta=Vector((1.35-(mn.x+mx.x)/2,-(mn.y+mx.y)/2,-mn.z))
    for o in refs:
        if o.parent not in refs: o.location+=delta
    render('CharacterScale',(4,-6,3.8),(.5,0,.85),4.2)
    reference.hide_render=True; reference.hide_viewport=True

palette_set('01_Cream')
camera.location=(3,-4,3); camera.rotation_euler=(Vector((0,0,.4))-camera.location).to_track_quat('-Z','Y').to_euler()
camera.data.ortho_scale=3.1
bpy.ops.object.select_all(action='DESELECT'); root.select_set(True); bpy.context.view_layer.objects.active=root
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.region_3d.view_distance=3.8
            area.spaces.active.region_3d.view_location=(0,0,.4)
            area.spaces.active.shading.color_type='MATERIAL'
for group in mats.values():
    for m in group: m.use_fake_user=True
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'SingleBed.blend'))
(QA/'BuildReport.json').write_text(json.dumps(report,indent=2),encoding='utf8')
print('SINGLE_BED_COMPLETE',json.dumps(report),flush=True)
