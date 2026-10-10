"""Four office exterior recipes sharing the established Blender authoring tools.

Blender --background --factory-startup --python build_office_set.py -- --types A,B,C,D
Geometry helpers: HandaeHQ/v02. Materials/camera/lights: Office01. Assets stay in ArtSource.
"""
import argparse
import ast
import importlib.util
import json
import math
import sys
from pathlib import Path
from types import SimpleNamespace
import bpy
import bmesh
from mathutils import Vector

HERE=Path(__file__).resolve().parent
BASE=HERE.parent
parser=argparse.ArgumentParser()
parser.add_argument('--types',default='A,B,C,D')
parser.add_argument('--skip-render',action='store_true')
args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
CONFIG={
    'A':('02_SlimGlass','Office02_SlimGlass',148.0),
    'B':('03_UrbanTerrace','Office03_UrbanTerrace',144.0),
    'C':('04_MinimalDark','Office04_MinimalDark',146.0),
    'D':('05_ClassicStone','Office05_ClassicStone',148.0),
}
XY_SCALE=1.2
HAND_HELPERS=BASE.parent/'HandaeHQ/v02/build_handae.py'
OFFICE_HELPERS=BASE/'01_LimestoneTower/build_office01.py'

def reuse(path,names):
    tree=ast.parse(path.read_text(encoding='utf-8'))
    nodes=[n for n in tree.body if isinstance(n,(ast.FunctionDef,ast.ClassDef)) and n.name in names]
    if {n.name for n in nodes}!=set(names):
        raise RuntimeError(f'Existing helper contract changed: {path}')
    exec(compile(ast.Module(body=nodes,type_ignores=[]),str(path),'exec'),globals())

def finish(name,collection,batch,material_names):
    return batch.object(name,[collection],material_names)

def panel_grid(axis,plane,lo,hi,z0,z1,bays,floors,normal,frame_width=.12,
               frame_depth=.20,spandrel=.28,glass_material='Glass',
               frame_material='Metal',name='Grid',fins=0):
    """Reusable office curtain facade; glass on plane and mullions in front."""
    panes,frames,bands=MeshBatch(),MeshBatch(),MeshBatch()
    du=(hi-lo)/bays;dz=(z1-z0)/floors
    for i in range(bays):
        u=lo+(i+.5)*du
        for j in range(floors):
            lower=z0+j*dz+spandrel*.5
            upper=z0+(j+1)*dz-spandrel*.5
            tint=2 if glass_material=='Glass' and (i*23+j*11)%83==41 else (1 if (i*7+j*3)%29==4 else 0)
            center=(u,plane-normal*.05,(lower+upper)/2) if axis=='x' else (plane-normal*.05,u,(lower+upper)/2)
            size=(du-frame_width,.10,upper-lower) if axis=='x' else (.10,du-frame_width,upper-lower)
            box(panes,center,size,tint)
    depth=frame_depth+fins
    for i in range(bays+1):
        u=lo+i*du
        center=(u,plane+normal*depth/2,(z0+z1)/2) if axis=='x' else (plane+normal*depth/2,u,(z0+z1)/2)
        size=(frame_width,depth,z1-z0) if axis=='x' else (depth,frame_width,z1-z0)
        box(frames,center,size)
    for j in range(floors+1):
        z=z0+j*dz
        center=((lo+hi)/2,plane+normal*.065,z) if axis=='x' else (plane+normal*.065,(lo+hi)/2,z)
        size=(hi-lo,.16,spandrel) if axis=='x' else (.16,hi-lo,spandrel)
        box(bands,center,size)
    finish(name+'_Glass','Glass',panes,[glass_material,'GlassDark','GlassWarm'])
    finish(name+'_Frames','Frames',frames,[frame_material])
    finish(name+'_Spandrels','Frames',bands,[frame_material])

def merge_batches():
    """Same collection and material slots share one mesh, keeping export draw count modest."""
    groups={}
    for ob in list(ROOT.children_recursive):
        if ob.type!='MESH':continue
        key=(ob.users_collection[0].name,tuple(m.name for m in ob.data.materials))
        groups.setdefault(key,[]).append(ob)
    for (collection,_),objects in groups.items():
        if len(objects)<2:continue
        bpy.ops.object.select_all(action='DESELECT')
        for ob in objects:ob.select_set(True)
        bpy.context.view_layer.objects.active=objects[0]
        bpy.ops.object.join()
        objects[0].name=f'{CURRENT_TYPE}_{collection}_{objects[0].data.materials[0].name.split("_")[-1]}'

def setup(letter):
    global scene,COLS,ROOT,MATS,CURRENT_TYPE
    CURRENT_TYPE=letter
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.save_version=0
    scene=bpy.context.scene
    scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
    COLS={}
    for name in ['Structure','Glass','Frames','Entrance','RoofDetails','GroundDetails','_Presentation']:
        c=bpy.data.collections.new(name);scene.collection.children.link(c);COLS[name]=c
    asset=CONFIG[letter][1]
    ROOT=bpy.data.objects.new(asset,None);COLS['Structure'].objects.link(ROOT)
    ROOT['units']='metres';ROOT['stage']='Proportional exterior review'
    ROOT['reference_type']=letter;ROOT['front']='Blender -Y'
    reuse(OFFICE_HELPERS,{'material','camera','light'})
    reuse(HAND_HELPERS,{'MeshBatch','box','loft'})
    glass={'A':(.12,.23,.32),'B':(.12,.20,.25),'C':(.055,.105,.15),'D':(.085,.15,.18)}[letter]
    metal={'A':(.060,.075,.087),'B':(.065,.072,.080),'C':(.030,.037,.041),'D':(.125,.100,.066)}[letter]
    stone={'A':(.55,.56,.55),'B':(.60,.56,.48),'C':(.20,.23,.25),'D':(.66,.59,.46)}[letter]
    MATS={
        'Stone':material(asset+'_Stone',stone,.68),
        'StoneDark':material(asset+'_StoneDark',(.29,.31,.32),.84),
        'Metal':material(asset+'_Metal',metal,.36,.64),
        'MetalLight':material(asset+'_MetalLight',(.46,.49,.50),.36,.70),
        'Glass':material(asset+'_Glass',glass,.24,.55),
        'GlassDark':material(asset+'_GlassDark',tuple(c*.69 for c in glass),.28,.46),
        'GlassWarm':material(asset+'_GlassWarm',(.26,.18,.086),.32,.28,.20),
        'Roof':material(asset+'_Roof',(.22,.235,.24),.86),
        'Wood':material(asset+'_Wood',(.34,.22,.105),.67),
        'Green':material(asset+'_Green',(.075,.145,.071),.95),
    }

def presentation():
    MATS['Paving']=material('_Presentation_Paving',(.38,.405,.43),.85)
    ground=MeshBatch();box(ground,(0,0,-.26),(1800,1800,.42))
    ob=ground.object('_StudioGround',['_Presentation'],['Paving'],parent=None);ob['export_asset']=False
    world=bpy.data.worlds.new('OfficeSet_StudioSky');world.use_nodes=True
    world.node_tree.nodes['Background'].inputs[0].default_value=(.50,.61,.74,1)
    world.node_tree.nodes['Background'].inputs[1].default_value=.55;scene.world=world
    light('Key',(-120,-180,210),480000,100,(1,.91,.80))
    light('Fill',(150,-50,140),310000,120,(.70,.84,1))
    light('Rim',(10,130,185),420000,110,(1,.96,.90))
    data=bpy.data.lights.new('Sun','SUN');data.energy=1.7;data.angle=.13
    sun=bpy.data.objects.new('Sun',data);COLS['_Presentation'].objects.link(sun);sun.rotation_euler=(.51,-.38,-.52)
    cams={
        'front_corner':camera('Camera_FrontCorner',(165,-215,105),(0,0,72)),
        'rear_corner':camera('Camera_RearCorner',(-185,210,107),(0,0,72)),
        'front_elevation':camera('Camera_FrontElevation',(0,-350,74),(0,0,74),174),
    }
    scene.render.engine='CYCLES';scene.cycles.samples=32;scene.cycles.use_denoising=True
    scene.cycles.max_bounces=5;scene.cycles.device='CPU'
    try:
        cp=bpy.context.preferences.addons['cycles'].preferences;cp.compute_device_type='OPTIX';cp.get_devices()
        if any(d.type=='OPTIX' for d in cp.devices):
            for d in cp.devices:d.use=d.type=='OPTIX'
            scene.cycles.device='GPU'
    except Exception:pass
    scene.view_settings.view_transform='AgX';scene.view_settings.exposure=.25
    scene.render.resolution_x=1400;scene.render.resolution_y=1700;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG';scene.camera=cams['front_corner']
    return cams

def build(letter):
    setup(letter)
    folder,asset,height=CONFIG[letter]
    target=BASE/folder;export=target/'UnityExport';renders=target/'BlockoutRenders'
    export.mkdir(parents=True,exist_ok=True);renders.mkdir(parents=True,exist_ok=True)
    spec=importlib.util.spec_from_file_location('office_design_'+letter,str(target/'design.py'))
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    details=module.build(SimpleNamespace(MeshBatch=MeshBatch,box=box,loft=loft,finish=finish,panel_grid=panel_grid)) or {}
    merge_batches()
    meshes=[o for o in ROOT.children_recursive if o.type=='MESH']
    # Bake XY scale in vertices so the larger game footprint preserves metre units and root scale 1.
    for ob in meshes:
        for v in ob.data.vertices:v.co.x*=XY_SCALE;v.co.y*=XY_SCALE
        for uv in ob.data.uv_layers:
            # Existing UV0 is a metre-based tile projection. A proportional XY
            # adjustment preserves the non-stretched architectural tile layout.
            for p in ob.data.polygons:
                axis=max(range(3),key=lambda k:abs(p.normal[k]));axes=[k for k in range(3) if k!=axis]
                for li in p.loop_indices:
                    co=ob.data.vertices[ob.data.loops[li].vertex_index].co
                    uv.data[li].uv=(co[axes[0]]/4,co[axes[1]]/4)
        ob.data.update()
    bpy.context.view_layer.update()
    points=[o.matrix_world@Vector(c) for o in meshes for c in o.bound_box]
    lower=[min(v[i] for v in points) for i in range(3)];upper=[max(v[i] for v in points) for i in range(3)]
    dimensions=[round(upper[i]-lower[i],4) for i in range(3)]
    bad=[(o.name,p.index,p.area) for o in meshes for p in o.data.polygons if len(p.vertices)!=3 or p.area<=1e-10]
    assert not bad,bad[:10]
    assert abs(lower[2])<.001 and abs(upper[2]-height)<.02,(lower,upper)
    assert dimensions[0]<88.1 and dimensions[1]<66.0,dimensions
    manifest={'asset':asset,'reference_type':letter,'stage':'First proportional exterior model / review',
        'building_dimensions_xyz_m':dimensions,'height_relative_to_office01':round(height/148,4),
        'recipe_xy_scale_baked':XY_SCALE,'units':'metres','front':'Blender -Y',
        'meshes':len(meshes),'triangles':sum(len(o.data.polygons) for o in meshes),
        'materials':sorted({m.name for o in meshes for m in o.data.materials}),
        'features':details.get('features',[]),'source_helpers':[str(HAND_HELPERS),str(OFFICE_HELPERS)],
        'scope':'Exterior modeling, initial materials and review renders. No Unity scene or gameplay changes.',
        'pending':['Final texture and joint detailing','Company signage','Unity materials/colliders/LOD and runtime validation']}
    (target/'model_manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
    cams=presentation()
    # Store reference axes as lightweight named points, excluded from mesh bounds.
    for suffix,point in [('FrontAnchor',(0,lower[1],0)),('EntranceAnchor',(0,lower[1]+1,.32))]:
        ob=bpy.data.objects.new(letter+'_'+suffix,None);COLS['Entrance'].objects.link(ob);ob.parent=ROOT;ob.location=point
    bpy.ops.wm.save_as_mainfile(filepath=str(target/(asset+'_Blockout.blend')))
    bpy.ops.object.select_all(action='DESELECT');ROOT.select_set(True)
    for ob in ROOT.children_recursive:ob.select_set(True)
    bpy.context.view_layer.objects.active=ROOT
    bpy.ops.export_scene.fbx(filepath=str(export/(asset+'_Blockout.fbx')),use_selection=True,
        global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',axis_forward='-Z',axis_up='Y',
        object_types={'MESH','EMPTY'},use_mesh_modifiers=True,mesh_smooth_type='FACE',use_tspace=True,
        add_leaf_bones=False,bake_anim=False,path_mode='RELATIVE')
    bpy.ops.export_scene.gltf(filepath=str(export/(asset+'_Blockout.glb')),export_format='GLB',use_selection=True,export_yup=True,export_apply=True)
    if not args.skip_render:
        for name in ['front_corner','rear_corner','front_elevation']:
            scene.camera=cams[name];scene.render.filepath=str(renders/(name+'.png'));bpy.ops.render.render(write_still=True)
    scene.camera=cams['front_corner']
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D':
                area.spaces.active.region_3d.view_distance=220;area.spaces.active.region_3d.view_location=(0,0,72)
    bpy.ops.wm.save_as_mainfile(filepath=str(target/(asset+'_Blockout.blend')))
    print('OFFICE_SET_ASSET_COMPLETE '+json.dumps(manifest,ensure_ascii=False))
    return manifest

results=[build(letter.strip().upper()) for letter in args.types.split(',')]
(HERE/'build_result.json').write_text(json.dumps(results,ensure_ascii=False,indent=2),encoding='utf-8')
print('OFFICE_SET_COMPLETE '+str(len(results)))
