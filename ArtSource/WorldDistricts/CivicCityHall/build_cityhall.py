"""Reference-led three-storey civic hall, using established asset authoring helpers.

42 x 34 m site versus the live library's 38 x 29 m paving. Metres, -Y front.
Only ArtSource outputs are generated. Unity scene integration is a later stage.
"""
import ast
import importlib.util
import json
import math
import random
import sys
from pathlib import Path
from types import SimpleNamespace
import bpy
import bmesh
from mathutils import Vector

HERE=Path(__file__).resolve().parent
WORLD=HERE.parent
EXPORT=HERE/'UnityExport';REVIEW=HERE/'BlockoutRenders'
EXPORT.mkdir(parents=True,exist_ok=True);REVIEW.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version=0
scene=bpy.context.scene
scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1.0
COLS={}
for name in ['Structure','Glass','Frames','Entrance','RoofDetails','Steps','Ramp','Railings','Landscape','Bench','Signage','_Presentation']:
    c=bpy.data.collections.new(name);scene.collection.children.link(c);COLS[name]=c
ROOT=bpy.data.objects.new('CivicCityHall',None);COLS['Structure'].objects.link(ROOT)
ROOT['units']='metres';ROOT['stage']='First proportional exterior model'
ROOT['front']='Blender -Y';ROOT['site_dimensions_m']=[42.0,34.0]
ROOT['library_reference_site_m']=[38.0,29.0]

def definitions(path,names,namespace):
    tree=ast.parse(path.read_text(encoding='utf-8'))
    nodes=[n for n in tree.body if isinstance(n,(ast.FunctionDef,ast.ClassDef)) and n.name in names]
    assert {n.name for n in nodes}==set(names),(path,names)
    exec(compile(ast.Module(body=nodes,type_ignores=[]),str(path),'exec'),namespace)

definitions(WORLD/'HandaeHQ/v02/build_handae.py',{'MeshBatch','box','loft'},globals())
definitions(WORLD/'CompanyBuildings/01_LimestoneTower/build_office01.py',{'material','camera'},globals())
definitions(WORLD/'CompanyBuildings/OfficeSet_ABCD/build_office_set.py',{'finish','panel_grid','merge_batches'},globals())
CURRENT_TYPE='CH'
MATS={
    'Stone':material('CityHall_WarmGranite',(.61,.60,.55),.78),
    'StoneLight':material('CityHall_StoneEdges',(.68,.67,.61),.75),
    'Concrete':material('CityHall_EntryConcrete',(.56,.57,.55),.82),
    'Pavement':material('CityHall_PlazaPaving',(.47,.49,.48),.86),
    'PavingJoint':material('CityHall_PavingJoint',(.30,.33,.33),.85),
    'Frame':material('CityHall_DarkMetal',(.045,.06,.065),.35,.65),
    'RailMetal':material('CityHall_RailMetal',(.42,.45,.46),.3,.76),
    'Glass':material('CityHall_BlueGlass',(.095,.175,.21),.20,.52),
    'GlassDark':material('CityHall_ShadowGlass',(.038,.060,.068),.27,.45),
    'GlassWarm':material('CityHall_LobbyGlass',(.19,.155,.095),.30,.22,.15),
    'Roof':material('CityHall_Roof',(.19,.21,.21),.87),
    'Grass':material('CityHall_Grass',(.16,.24,.105),.96),
    'LeafDark':material('CityHall_FoliageDark',(.07,.16,.050),.95),
    'LeafLight':material('CityHall_FoliageLight',(.18,.28,.078),.90),
    'Soil':material('CityHall_Soil',(.16,.12,.079),.98),
    'Paper':material('CityHall_Flower',(.79,.73,.60),.89),
    'Bark':material('CityHall_Bark',(.20,.15,.09),.91),
    'Oak':material('CityHall_BenchOak',(.39,.25,.115),.7),
    'SignInk':material('CityHall_SignInk',(.035,.068,.085),.60),
    'White':material('CityHall_FlagWhite',(.90,.90,.85),.78),
    'FlagRed':material('CityHall_FlagRed',(.66,.017,.040),.78),
    'FlagBlue':material('CityHall_FlagBlue',(.012,.075,.31),.78),
    'CivicBlue':material('CityHall_CivicBlue',(.035,.19,.30),.74),
    'CivicGreen':material('CityHall_CivicGreen',(.10,.26,.10),.77),
    'Light':material('CityHall_Downlight',(.92,.79,.55),.36,0,2.0),
}
MATS['Metal']=MATS['Frame']
library_namespace={'bpy':bpy,'math':math,'random':random,'Vector':Vector,'groups':COLS,'M':MATS}
definitions(WORLD/'CivicLibrary/build_library.py',
            {'assign','uv_box','box','beam','ico','ramp','ramp_rail','shrub'},library_namespace)
lib=SimpleNamespace(**{name:library_namespace[name] for name in ['box','beam','ico','ramp','ramp_rail','shrub']})
random.seed(913)

# Editable exterior mass. Backing is inside the stone/window envelope.
mass=MeshBatch();stone=MeshBatch();trim=MeshBatch();roof=MeshBatch();entrance=MeshBatch()
box(mass,(0,4.0,6.925),(32.60,18.50,12.01))
box(stone,(0,4,.53),(34.0,20.0,.78))
box(stone,(0,4,13.27),(34.15,20.15,.48))

def stone_elevation(label,axis,plane,lo,hi,normal,centers,window_width):
    """Punched stone elevation around reused curtain-window modules."""
    cursor=lo
    for i,u in enumerate(centers):
        left,right=u-window_width/2,u+window_width/2
        if left>cursor:
            pos=((left+cursor)/2,plane+normal*.15,7.01) if axis=='x' else (plane+normal*.15,(left+cursor)/2,7.01)
            size=(left-cursor,.68,12.02) if axis=='x' else (.68,left-cursor,12.02)
            box(stone,pos,size)
        panel_grid(axis,plane-normal*.22,left,right,1.50,12.70,1,3,normal,
                   frame_width=.065,frame_depth=.12,spandrel=.18,
                   frame_material='Frame',name=f'CH_{label}_{i:02d}')
        cursor=right
    if cursor<hi:
        pos=((hi+cursor)/2,plane+normal*.15,7.01) if axis=='x' else (plane+normal*.15,(hi+cursor)/2,7.01)
        size=(hi-cursor,.68,12.02) if axis=='x' else (.68,hi-cursor,12.02)
        box(stone,pos,size)
    for z,h in [(1.17,.68),(4.99,.29),(8.72,.29),(13.28,1.12)]:
        pos=((lo+hi)/2,plane+normal*.15,z) if axis=='x' else (plane+normal*.15,(lo+hi)/2,z)
        size=(hi-lo,.68,h) if axis=='x' else (.68,hi-lo,h)
        box(stone,pos,size)
    # Shallow stone courses, enough to establish scale without final textures.
    for z in [2.55,3.8,6.2,7.45,9.9,11.15,12.4]:
        for a,b in zip([lo]+[u+window_width/2 for u in centers],
                       [u-window_width/2 for u in centers]+[hi]):
            if b-a<=.05:continue
            pos=((a+b)/2,plane+normal*.495,z) if axis=='x' else (plane+normal*.495,(a+b)/2,z)
            size=(b-a,.015,.018) if axis=='x' else (.015,b-a,.018)
            box(trim,pos,size,1)

stone_elevation('FrontWest','x',-5.85,-17,-7.8,-1,[-15.3,-12.5,-9.7],1.45)
stone_elevation('FrontEast','x',-5.85,7.8,17,-1,[9.7,12.5,15.3],1.45)
stone_elevation('West','y',-16.60,-5.85,13.70,-1,[-4.1,-1.7,.7,3.1,5.5,7.9,10.3,12.4],1.24)
stone_elevation('East','y',16.60,-5.85,13.70,1,[-4.1,-1.7,.7,3.1,5.5,7.9,10.3,12.4],1.24)
stone_elevation('Rear','x',13.70,-17,17,1,[-14.5,-10.9,-7.3,-3.7,0,3.7,7.3,10.9,14.5],1.75)

# The deep civic portico, with columns stopping at the thin entrance canopy.
for x in (-7.20,7.20):
    box(stone,(x,-6.345,6.97),(1.20,1.55,12.10))
box(stone,(0,-5.995,14.30),(15.6,2.25,3.0))
panel_grid('x',-5.45,-6.60,6.60,1.05,12.80,8,3,-1,
           frame_width=.085,frame_depth=.15,spandrel=.17,
           frame_material='Frame',name='CH_CentralPublicGlazing')
for x in (-4.20,4.20):
    lib.beam('CH_PorticoRoundColumn',(x,-6.45,4.65),(x,-6.45,12.80),.235,'Stone','Structure',20)
    lib.beam('CH_ColumnBase',(x,-6.45,4.59),(x,-6.45,4.75),.32,'StoneLight','Structure',20)
    lib.beam('CH_ColumnCapital',(x,-6.45,12.64),(x,-6.45,12.82),.32,'StoneLight','Structure',20)
box(entrance,(0,-6.70,4.46),(16.8,4.0,.26))
box(entrance,(0,-8.64,4.47),(16.85,.14,.32))
for x in [-7.5,-5.0,-2.5,0,2.5,5.0,7.5]:
    box(entrance,(x,-6.7,4.30),(.08,3.84,.10))
    lib.ico('CH_CanopyDownlight',(x,-7.65,4.31),(.065,.065,.028),'Light','Entrance',1)
# Door leaves have their own frames; the upper glazing remains a simple exterior representation.
entry_glass=MeshBatch()
for x in (-1.35,1.35):
    box(entry_glass,(x,-5.73,2.36),(2.60,.09,2.86))
for x in (-2.71,0,2.71):box(entrance,(x,-5.83,2.39),(.085,.15,2.92))
box(entrance,(0,-5.83,3.85),(5.52,.15,.09))
for x in (-.16,.16):lib.beam('CH_EntryPullHandle',(x,-5.96,1.75),(x,-5.96,2.44),.024,'RailMetal','Entrance',10)
finish('CH_MainEntranceGlazing','Entrance',entry_glass,['Glass'])

# Side and rear service doors sit between stone piers, with a discrete canopy.
rear_panel=MeshBatch()
box(rear_panel,(-3.70,14.21,2.03),(1.70,.10,2.22))
box(entrance,(-3.70,14.27,3.28),(2.30,1.18,.16))
finish('CH_RearServicePanel','Entrance',rear_panel,['GlassDark'])
finish('CH_RecessedExteriorBacking','Structure',mass,['GlassDark'])
finish('CH_GraniteFacadeAndPortico','Structure',stone,['Stone'])
finish('CH_FacadeCourses','Frames',trim,['Frame','Concrete'])
finish('CH_EntranceCanopyAndFrames','Entrance',entrance,['Frame'])

# Parapet ring and recessed roof surfaces avoid broad coincident faces.
box(roof,(0,4.0,13.57),(32.8,18.8,.12))
# Separate batches keep already finalized facade meshes independent.
parapet=MeshBatch()
for y in (-5.75,13.75):box(parapet,(0,y,13.94),(34.10,.55,.35))
for x in (-16.78,16.78):box(parapet,(x,4,13.94),(.55,18.45,.35))
box(parapet,(-8,7.6,14.20),(6.2,4.4,1.22))
box(roof,(-8,7.6,14.86),(6.4,4.6,.12))
skylight=MeshBatch()
loft(skylight,[[(-3,0,13.70),(3,0,13.70),(3,7,13.70),(-3,7,13.70)],
               [(-2.75,.25,14.1),(2.75,.25,14.1),(2.75,6.75,14.1),(-2.75,6.75,14.1)]])
finish('CH_LowRoofSkylight','RoofDetails',skylight,['Glass'])
finish('CH_ExposedRoofFinish','RoofDetails',roof,['Roof'])
finish('CH_RoofParapetsAndServiceScreen','RoofDetails',parapet,['Stone'])

def run_recipe(filename,api):
    spec=importlib.util.spec_from_file_location(filename.removesuffix('.py'),str(HERE/filename))
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    return module.build(api)

site_details=run_recipe('site_design.py',lib)

# Foliage uses the library's closed ico and cylinder tools, with restrained density.
for number,(x,y,h) in enumerate([(-19,8,5.8),(19,9,6.0),(-19,-1,5.8),(19,0,5.6),(-18.6,-11.6,5.3),(18.7,-14.0,4.9)]):
    lib.beam(f'CH_Tree{number}_Trunk',(x,y,.14),(x+.08,y,h-.70),.085,'Bark','Landscape',9)
    for j in range(5):
        a=j*2.4;cx=x+math.cos(a)*.62;cy=y+math.sin(a)*.56;cz=h-.9+(j%3)*.36
        lib.beam(f'CH_Tree{number}_Branch',(x,y,h-2.3),(cx,cy,cz),.035,'Bark','Landscape',7)
        lib.ico(f'CH_Tree{number}_Canopy',(cx,cy,cz),(1.02,.96,1.22),'LeafDark' if j%2 else 'LeafLight','Landscape',2)
for x,y in [(-16.8,-13.0),(13.8,-14.3)]:
    for j in range(5):lib.box('CH_BenchSeat',(x,y+j*.10,.66),(2.15,.085,.07),'Oak','Bench',.008)
    for j in range(3):lib.box('CH_BenchBack',(x,y+.45,.88+j*.12),(2.15,.065,.10),'Oak','Bench')
    for dx in (-.75,.75):
        lib.box('CH_BenchLeg',(x+dx,y+.21,.38),(.085,.47,.49),'Frame','Bench')

font_path=WORLD.parents[1]/'CompanyGame/Assets/Art/font/NotoSansKR-Regular.ttf'
font=bpy.data.fonts.load(str(font_path))
try:font.pack()
except Exception:pass

def text_mesh(name,body,location,size,material='SignInk',align='CENTER'):
    curve=bpy.data.curves.new(name+'_Text','FONT');curve.body=body;curve.size=size
    curve.font=font;curve.align_x=align;curve.extrude=.008;curve.resolution_u=4
    ob=bpy.data.objects.new('_TemporaryText',curve);COLS['Signage'].objects.link(ob)
    ob.location=location;ob.rotation_euler=(math.pi/2,0,0)
    bpy.ops.object.select_all(action='DESELECT');ob.select_set(True);bpy.context.view_layer.objects.active=ob
    bpy.ops.object.convert(target='MESH')
    batch=MeshBatch()
    for p in ob.data.polygons:
        batch.face([tuple(ob.matrix_world@ob.data.vertices[i].co) for i in p.vertices])
    bpy.data.objects.remove(ob,do_unlink=True)
    return finish(name,'Signage',batch,[material])

signage_details=run_recipe('signage_design.py',SimpleNamespace(MeshBatch=MeshBatch,box=box,finish=finish,text=text_mesh,
                                                               beam=lib.beam,ico=lib.ico))

# Parent, triangulate and UV-map library primitives with the existing mesh tool.
for ob in [o for col in COLS.values() if col.name!='_Presentation' for o in list(col.objects) if o.type=='MESH']:
    ob.parent=ROOT
    mesh=ob.data
    bm=bmesh.new();bm.from_mesh(mesh)
    bmesh.ops.triangulate(bm,faces=list(bm.faces),quad_method='FIXED',ngon_method='EAR_CLIP')
    zero=[f for f in bm.faces if f.calc_area()<=1e-10]
    if zero:bmesh.ops.delete(bm,geom=zero,context='FACES')
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
    if not mesh.uv_layers:library_namespace['uv_box'](ob)
    ob['export_asset']=True
bpy.context.view_layer.update()
bpy.ops.object.select_all(action='DESELECT')
for ob in ROOT.children_recursive:
    if ob.type=='MESH':ob.select_set(True);bpy.context.view_layer.objects.active=ob
bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
merge_batches()
bpy.context.view_layer.update()
meshes=[o for o in ROOT.children_recursive if o.type=='MESH']
points=[o.matrix_world@Vector(c) for o in meshes for c in o.bound_box]
lower=[min(v[i] for v in points) for i in range(3)];upper=[max(v[i] for v in points) for i in range(3)]
dimensions=[round(upper[i]-lower[i],4) for i in range(3)]
assert abs(lower[2])<.001,(lower,upper)
assert dimensions[0]<=42.01 and dimensions[1]<=34.01,dimensions
assert abs(upper[2]-15.8)<.02,upper
bad=[(o.name,p.index) for o in meshes for p in o.data.polygons if len(p.vertices)!=3 or p.area<=1e-10]
assert not bad,bad[:20]
manifest={'asset':'CivicCityHall','stage':'First proportional exterior model / review',
          'building_dimensions_xyz_m':dimensions,'site_dimensions_m':[42,34],
          'nominal_main_building_m':[34,20,15.8],'library_reference_site_m':[38,29],
          'site_area_increase_percent':round((42*34/(38*29)-1)*100,2),
          'storeys':3,'units':'metres','front':'Blender -Y',
          'site_details':site_details,'signage_details':signage_details,
          'meshes':len(meshes),'triangles':sum(len(o.data.polygons) for o in meshes),
          'materials':sorted({m.name for o in meshes for m in o.data.materials}),
          'features':['Deep central stone portico and round columns','Three-storey glazed public facade',
                      'Thin entrance canopy','Central steps and connected return ramp',
                      'Open forecourt, two civic flags and stone city hall sign','Complete side and rear elevations'],
          'scope':'Exterior design model only. No Unity scene or gameplay changes.',
          'pending':['Final material detailing','Unity integration, collision, LOD and lightmaps','Playable interior']}
(HERE/'model_manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')

# Proportionate studio setup: same established camera/material tools, shorter civic scale.
MATS['Studio']=material('_CityHall_StudioGround',(.52,.55,.57),.88)
ground=MeshBatch();box(ground,(0,0,-.18),(3000,3000,.30))
studio=ground.object('_StudioGround',['_Presentation'],['Studio'],parent=None);studio['export_asset']=False
world=bpy.data.worlds.new('CityHall_StudioWorld');world.use_nodes=True
world.node_tree.nodes['Background'].inputs[0].default_value=(.53,.66,.83,1)
world.node_tree.nodes['Background'].inputs[1].default_value=.50;scene.world=world
for name,position,power,size,color in [('Key',(-28,-35,52),6500,28,(1,.91,.76)),('Fill',(38,-12,29),3200,27,(.73,.86,1)),('Rim',(8,32,40),4800,25,(1,.96,.87))]:
    data=bpy.data.lights.new(name,'AREA');data.energy=power;data.shape='DISK';data.size=size;data.color=color
    ob=bpy.data.objects.new(name,data);COLS['_Presentation'].objects.link(ob);ob.location=position
    ob.rotation_euler=(Vector((0,0,6))-ob.location).to_track_quat('-Z','Y').to_euler()
data=bpy.data.lights.new('Sun','SUN');data.energy=2.0;data.angle=.13
sun=bpy.data.objects.new('Sun',data);COLS['_Presentation'].objects.link(sun);sun.rotation_euler=(.48,-.32,-.45)
CAMS={
    'front_corner':camera('Camera_FrontCorner',(46,-62,31),(0,-1.0,5.1)),
    'rear_corner':camera('Camera_RearCorner',(-48,57,30),(0,1.5,5.5)),
    'front_elevation':camera('Camera_FrontElevation',(0,-80,7.0),(0,0,7.0),47),
    'site_top':camera('Camera_SiteTop',(0,0,75),(0,0,0),47),
}
scene.render.engine='CYCLES';scene.cycles.samples=40;scene.cycles.use_denoising=True
scene.cycles.max_bounces=6;scene.cycles.diffuse_bounces=2;scene.cycles.glossy_bounces=3
scene.cycles.device='CPU'
try:
    cp=bpy.context.preferences.addons['cycles'].preferences;cp.compute_device_type='OPTIX';cp.get_devices()
    if any(d.type=='OPTIX' for d in cp.devices):
        for d in cp.devices:d.use=d.type=='OPTIX'
        scene.cycles.device='GPU'
except Exception:pass
scene.view_settings.view_transform='AgX';scene.view_settings.exposure=.2
scene.render.resolution_x=1800;scene.render.resolution_y=1350;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG';scene.camera=CAMS['front_corner']
for name,point in [('EntranceAnchor',(0,-5.95,.92)),('FrontAnchor',(0,-17,.14)),('ServiceAnchor',(-3.70,14.75,.92))]:
    ob=bpy.data.objects.new(name,None);COLS['Entrance'].objects.link(ob);ob.parent=ROOT;ob.location=point
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'CivicCityHall_Blockout.blend'))
bpy.ops.object.select_all(action='DESELECT');ROOT.select_set(True)
for ob in ROOT.children_recursive:ob.select_set(True)
bpy.context.view_layer.objects.active=ROOT
bpy.ops.export_scene.fbx(filepath=str(EXPORT/'CivicCityHall_Blockout.fbx'),use_selection=True,
    global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',axis_forward='-Z',axis_up='Y',
    object_types={'MESH','EMPTY'},use_mesh_modifiers=True,mesh_smooth_type='FACE',use_tspace=True,
    add_leaf_bones=False,bake_anim=False,path_mode='RELATIVE')
bpy.ops.export_scene.gltf(filepath=str(EXPORT/'CivicCityHall_Blockout.glb'),export_format='GLB',use_selection=True,export_yup=True,export_apply=True)
if '--skip-render' not in sys.argv:
    for name in ['front_corner','rear_corner','front_elevation','site_top']:
        scene.camera=CAMS[name];scene.render.filepath=str(REVIEW/(name+'.png'));bpy.ops.render.render(write_still=True)
scene.camera=CAMS['front_corner']
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.region_3d.view_distance=60;area.spaces.active.region_3d.view_location=(0,0,6)
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'CivicCityHall_Blockout.blend'))
print('CITY_HALL_COMPLETE '+json.dumps(manifest,ensure_ascii=False))
