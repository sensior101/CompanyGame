"""Independent FBX/GLB round-trip measurements; does not launch Unity."""
import bpy,json
from pathlib import Path
from mathutils import Vector
HERE=Path(__file__).resolve().parent
expected=json.loads((HERE/'QA/BuildReport.json').read_text())
rows=[]
for path in sorted((HERE/'Exports').glob('*')):
    if path.suffix not in {'.fbx','.glb'}: continue
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if path.suffix=='.fbx': bpy.ops.import_scene.fbx(filepath=str(path))
    else: bpy.ops.import_scene.gltf(filepath=str(path))
    bpy.context.view_layer.update()
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
    pts=[o.matrix_world@Vector(c) for o in meshes for c in o.bound_box]
    lo=[min(p[i] for p in pts) for i in range(3)]
    hi=[max(p[i] for p in pts) for i in range(3)]
    dims=[hi[i]-lo[i] for i in range(3)]
    tri=sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in meshes)
    materials=sorted({m.name for o in meshes for m in o.data.materials if m})
    assert len(meshes)==expected['mesh_count'],path
    assert tri==expected['triangles'],(path,tri)
    assert all(abs(a-b)<.0001 for a,b in zip(dims,expected['dimensions_xyz'])),(path,dims)
    assert len(materials)==5,(path,materials)
    assert all(o.data.uv_layers for o in meshes),path
    assert all(len(p.vertices)==3 and p.area>1e-10 for o in meshes for p in o.data.polygons),path
    assert abs(lo[2])<.0001,(path,lo)
    rows.append({'file':path.name,'dimensions_xyz':dims,'triangles':tri,'materials':materials,'passed':True})
(HERE/'QA/RoundTripReport.json').write_text(json.dumps(rows,indent=2),encoding='utf8')
print('ROUND_TRIP_PASS',len(rows))
