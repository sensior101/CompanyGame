"""19 additional commercial assets; Blender authoring only, reusing shared recipes."""
import bpy, bmesh, sys, json, math, importlib.util, hashlib
from pathlib import Path
from mathutils import Vector
sys.dont_write_bytecode = True
OUT = Path(__file__).resolve().parent
BASE = OUT.parent / 'CommercialLowPoly_20261105'
def module(name, path):
    spec=importlib.util.spec_from_file_location(name,path)
    obj=importlib.util.module_from_spec(spec);spec.loader.exec_module(obj);return obj
c=module('commercial',BASE/'build_commercial.py');h=c.h
r=module('extras_recipes',OUT/'recipes.py');r.h=h
for name in ('Models','Previews','QA'):(OUT/name).mkdir(exist_ok=True)
ROOM=OUT.parents[2]/'WorldDistricts/ShopShell_WindowReady_20261105/ShopShell_NoWindows.blend'

def palette():
    colors=dict(Body='F2F0EA',Wood='C7AA80',Fabric='ECE8DF',Accent='D4BE9D',Dark='4C4B45',Metal='BBC0C1',White='FAF9F5',Red='CB342C')
    for role,color in colors.items():
        if role not in h.MATS['Cream']:
            mat=bpy.data.materials.new('WhiteWood_'+role);mat.use_nodes=True;h.MATS['Cream'][role]=mat
        mat=h.MATS['Cream'][role];mat.diffuse_color=h.rgba(color)
        p=mat.node_tree.nodes['Principled BSDF'];p.inputs['Base Color'].default_value=h.rgba(color)
        p.inputs['Roughness'].default_value=.30 if role=='Metal' else (.37 if role=='Red' else .76)
        p.inputs['Metallic'].default_value=.8 if role=='Metal' else 0

def studio(lo,hi,res=640):
    center=Vector([(a+b)/2 for a,b in zip(lo,hi)])
    c.camera_scene(center+Vector((3,-4,2.8)),center,1,(res,res))
    scene=bpy.context.scene;cam=scene.camera
    inv=cam.rotation_euler.to_matrix().transposed()
    pts=[inv@(Vector((x,y,z))-center) for x in (lo[0],hi[0]) for y in (lo[1],hi[1]) for z in (lo[2],hi[2])]
    cam.data.ortho_scale=max(max(p[i] for p in pts)-min(p[i] for p in pts) for i in (0,1))*1.24
    scene.cycles.samples=16
    try:
        pref=bpy.context.preferences.addons['cycles'].preferences;pref.compute_device_type='OPTIX';pref.get_devices()
        for d in pref.devices:d.use=d.type=='OPTIX'
        if any(d.type=='OPTIX' for d in pref.devices):scene.cycles.device='GPU'
    except Exception:pass
    bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,lo[2]-.006))
    floor=bpy.context.object;floor.name='STUDIO_Ground'
    mat=bpy.data.materials.new('StudioSage');mat.use_nodes=True
    mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=h.rgba('9FAAA6')
    mat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.88
    floor.data.materials.append(mat)
    # Comfortable initial material viewport on opening each asset.
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D':
                area.spaces.active.region_3d.view_perspective='CAMERA'
                area.spaces.active.shading.color_type='MATERIAL'

def build():
    rows=[]
    for entry in r.ENTRIES:
        h.init(entry);palette();entry['build']()
        row=h.prepare(entry)
        lo,hi=c.bounds(h.PARTS)
        if entry['placement']=='Wall':
            for obj in h.PARTS:obj.location.y-=hi[1]
        if entry['placement']=='Ceiling':
            for obj in h.PARTS:obj.location.z-=hi[2]
        lo,hi=c.bounds(h.PARTS)
        row.update(bounds_min=lo,bounds_max=hi,dimensions=[round(hi[i]-lo[i],5) for i in range(3)],palettes=['WhiteAndWood'],geometry_sha256=h.geometry_hash())
        h.root['AuthoringOnly']=True;h.root['Theme']='WhiteAndWood'
        h.root['MountOrigin']='rear Y=0, bottom Z=0' if entry['placement']=='Wall' else ('ceiling Z=0, hangs down' if entry['placement']=='Ceiling' else 'bottom Z=0')
        studio(lo,hi)
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Models'/f"{entry['id']}.blend"),compress=True)
        bpy.context.scene.render.filepath=str(OUT/'Previews'/f"{entry['id']}.png")
        bpy.ops.render.render(write_still=True)
        rows.append(row);print('ASSET_COMPLETE',entry['id'],row['dimensions'],flush=True)
    (OUT/'QA/catalog.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2),encoding='utf8')

def load(identifier,folder=OUT):
    with bpy.data.libraries.load(str(folder/'Models'/f'{identifier}.blend'),link=False) as (src,dst):dst.collections=[identifier+'_ASSET']
    col=dst.collections[0];bpy.context.scene.collection.children.link(col)
    return next(o for o in col.objects if o.type=='EMPTY')

def showroom():
    bpy.ops.wm.open_mainfile(filepath=str(ROOM));bpy.context.preferences.filepaths.save_version=0
    # Preserve source: presentation visibility changes exist only in the new file.
    for obj in bpy.context.scene.objects:
        if obj.type=='LIGHT':obj.hide_render=True
        if any(word in obj.name.lower() for word in ('ceiling','front','right')):obj.hide_render=True
    positions={
      'CommercialFridge':(-10,6.5,0),'CommercialFreezer':(-7.5,6.5,0),'CommercialSink':(-4.5,6.5,0),'CommercialOven':(-1.5,6.5,0),
      'WaterDispenser':(2,6.5,0),'PedalTrashBin':(3.6,6.5,0),'ServingCart':(-7,3,0),
      'WallMenuBoard':(-3.8,8.19,1.65),'RoundGroupTable':(-3,1.5,0),'LowFloorTable':(1,1.5,0),
      'HangingTableNumber':(-3,1.5,3.3),'Beanbag':(3.3,1.5,0),'SalonChair':(7,1.5,0),
      'UmbrellaStand':(-10,-4.5,0),'AFrameSign':(-8,-4.5,0),'TerraceTable':(5,-4,0),
      'RedPlasticChair':(5,-5,0),'PatioParasol':(7,-3.2,0),'DeskTelephone':(-3,-3.7,.74)}
    for entry in r.ENTRIES:load(entry['id']).location=positions[entry['id']]
    desk=load('PlainDesk',BASE);desk.name='REFERENCE_Existing74cmDesk';desk.location=(-3,-3.7,0)
    c.camera_scene((24,-31,28),(0,0,.7),35,(1800,1200))
    bpy.context.scene.cycles.samples=24
    bpy.context.scene.render.filepath=str(OUT/'Previews/RoomScale.png')
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'CommercialExtras_WhiteWood_RoomScale.blend'),compress=True)
    bpy.ops.render.render(write_still=True)

def verify():
    rows=json.loads((OUT/'QA/catalog.json').read_text(encoding='utf8'));assert len(rows)==19
    results=[]
    for row in rows:
        bpy.ops.wm.open_mainfile(filepath=str(OUT/'Models'/f"{row['id']}.blend"))
        col=bpy.data.collections[row['id']+'_ASSET'];meshes=[o for o in col.objects if o.type=='MESH']
        lo,hi=c.bounds(meshes);nonmanifold=degenerate=triangles=0
        for o in meshes:
            bm=bmesh.new();bm.from_mesh(o.data)
            if not o.get('ImageSurface'):nonmanifold+=sum(not e.is_manifold for e in bm.edges)
            degenerate+=sum(f.calc_area()<1e-10 for f in bm.faces);bm.free()
            o.data.calc_loop_triangles();triangles+=len(o.data.loop_triangles)
            assert o.data.materials and o.data.uv_layers,o.name
            assert all(math.isfinite(v) for vert in o.data.vertices for v in vert.co),o.name
        assert nonmanifold==0 and degenerate==0,(row['id'],nonmanifold,degenerate)
        assert triangles<12000 and triangles==row['triangles'],row['id']
        assert max(abs(hi[i]-lo[i]-row['dimensions'][i]) for i in range(3))<.001,row['id']
        assert abs(hi[2] if row['placement']=='Ceiling' else lo[2])<.001,row['id']
        if row['placement']=='Wall':assert abs(hi[1])<.001
        if row['id']=='DeskTelephone':assert hi[0]-lo[0]<=.30 and hi[1]-lo[1]<=.30 and hi[2]-lo[2]<=.20
        if row['id']=='RedPlasticChair':
            assert any(o.get('Role')=='Red' for o in meshes)
        if row['id'] in ('WallMenuBoard','AFrameSign'):
            panels=[o for o in meshes if o.get('ImageSurface')]
            assert panels,row['id']
            for panel in panels:
                assert panel.data.uv_layers
                color=panel.data.materials[0].node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value
                assert min(color[:3])>.9,(row['id'],list(color))
        results.append(dict(id=row['id'],triangles=triangles,dimensions=row['dimensions'],nonmanifold=nonmanifold,degenerate=degenerate,mount=row['placement']))
    bpy.ops.wm.open_mainfile(filepath=str(OUT/'CommercialExtras_WhiteWood_RoomScale.blend'))
    for row in rows:
        meshes=[o for o in bpy.data.collections[row['id']+'_ASSET'].objects if o.type=='MESH'];lo,hi=c.bounds(meshes)
        assert lo[0]>=-13 and hi[0]<=13 and lo[1]>=-8.2 and hi[1]<=8.201 and lo[2]>=-.001 and hi[2]<=3.301,(row['id'],lo,hi)
    report=dict(asset_count=19,assets=results,inside_room=True,source_room=str(ROOM),source_room_sha256=hashlib.sha256(ROOM.read_bytes()).hexdigest(),unity_imported=False)
    (OUT/'QA/validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
    print('VALIDATION_PASSED: 19 assets, dimensions / mounts / materials / UV / closed meshes / room fit',flush=True)

if __name__=='__main__':
    if '--verify-only' not in sys.argv:
        if '--presentation-only' not in sys.argv:build()
        showroom()
    verify()
