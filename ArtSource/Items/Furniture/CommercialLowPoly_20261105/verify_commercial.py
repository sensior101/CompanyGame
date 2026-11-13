"""Reopen delivered Blender files and validate geometry and image placeholders."""
import bpy
import bmesh
import json
from pathlib import Path
from mathutils import Vector

OUT=Path(__file__).resolve().parent
catalog=json.loads((OUT/'QA/catalog.json').read_text(encoding='utf8'))
assert len(catalog)==45
results=[]
for entry in catalog:
    bpy.ops.wm.open_mainfile(filepath=str(OUT/'Models'/f"{entry['id']}.blend"))
    collection=bpy.data.collections[entry['id']+'_ASSET']
    meshes=[o for o in collection.objects if o.type=='MESH']
    points=[o.matrix_world @ v.co for o in meshes for v in o.data.vertices]
    low=[min(p[i] for p in points) for i in range(3)]
    high=[max(p[i] for p in points) for i in range(3)]
    triangles=0
    bad_edges=0
    degenerate=0
    for obj in meshes:
        bm=bmesh.new();bm.from_mesh(obj.data)
        if not obj.get('ImageSurface'):
            bad_edges+=sum(not e.is_manifold for e in bm.edges)
        degenerate+=sum(f.calc_area()<1e-10 for f in bm.faces)
        bm.free()
        obj.data.calc_loop_triangles();triangles+=len(obj.data.loop_triangles)
        assert obj.data.materials, obj.name
        assert obj.data.uv_layers, obj.name
    dimensions=[high[i]-low[i] for i in range(3)]
    assert max(abs(dimensions[i]-entry['dimensions'][i]) for i in range(3))<.001,entry['id']
    assert abs(low[2])<.001,entry['id']
    assert triangles<12000,entry['id']
    assert bad_edges==0 and degenerate==0,entry['id']
    if entry['placement']=='Wall':assert abs(high[1])<.001,entry['id']
    if entry['id'] in ('PictureFrame','Poster'):
        panel=next(o for o in meshes if o.get('ImageSurface'))
        assert tuple(panel.data.materials[0].node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value)==(1,1,1,1)
        assert panel.data.uv_layers.active.name=='ImageUV'
    if entry['id']=='CubicleDesk':
        assert all(any(word in o.name for word in ('Tabletop','Leg','Underframe','Panel','TopCap')) for o in meshes)
    results.append(dict(id=entry['id'],dimensions=dimensions,triangles=triangles,nonmanifold_edges=bad_edges,degenerate_faces=degenerate))
room=OUT.parents[2]/'WorldDistricts/ShopInteriorConcept_20261105/ShopInterior_Empty.blend'
bpy.ops.wm.open_mainfile(filepath=str(room))
shell=[]
for obj in bpy.context.scene.objects:
    if obj.type=='MESH':shell.append(dict(name=obj.name,dimensions=list(obj.dimensions),location=list(obj.location)))
report=dict(asset_count=len(results),assets=results,room_source=str(room),room_objects=shell)
preview=OUT/'CommercialFurniture_RoomScale.blend'
if preview.exists():
    bpy.ops.wm.open_mainfile(filepath=str(preview))
    for entry in catalog:
        collection=bpy.data.collections[entry['id']+'_ASSET']
        points=[o.matrix_world @ v.co for o in collection.objects if o.type=='MESH' for v in o.data.vertices]
        assert all(-13.001<=p.x<=13.001 and -8.201<=p.y<=8.201 and -.001<=p.z<=3.301 for p in points),entry['id']
    report['room_preview_assets_inside_shell']=True
(OUT/'QA/validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
print('VALIDATION',len(results),'assets, nonmanifold',sum(r['nonmanifold_edges'] for r in results),'degenerate',sum(r['degenerate_faces'] for r in results),flush=True)
print('ROOM',json.dumps(shell),flush=True)
