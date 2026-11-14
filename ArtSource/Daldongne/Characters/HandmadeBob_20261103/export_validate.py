"""Validate the Blender bob and save a hair-only editable .blend; no Unity export."""
import bpy,bmesh,json
from pathlib import Path
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree
from mathutils import Vector
P=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(P/'LayeredBob_FitReview.blend'))
body=bpy.data.objects['FemaleBody'];ob=bpy.data.objects['SoftBob_Hair'];bpy.context.view_layer.update()
bt=BVHTree.FromPolygons([body.matrix_world@v.co for v in body.data.vertices],[p.vertices for p in body.data.polygons]);ht=BVHTree.FromPolygons([ob.matrix_world@v.co for v in ob.data.vertices],[p.vertices for p in ob.data.polygons]);hits=bt.overlap(ht)
print('INTERSECTION_LOCATIONS',[(list(ob.matrix_world@ob.data.polygons[j].center),list(body.matrix_world@body.data.polygons[i].center)) for i,j in hits],flush=True)
bm=bmesh.new();bm.from_mesh(ob.data);remaining=set(bm.verts);components=0
while remaining:
 components+=1;stack=[remaining.pop()]
 while stack:
  v=stack.pop()
  for e in v.link_edges:
   n=e.other_vert(v)
   if n in remaining:remaining.remove(n);stack.append(n)
report={'method':'One continuous sculpted hair surface with integral splits, soft normals and directional hair shading','hair_meshes':1,'connected_components':components,'vertices':len(bm.verts),'triangles':sum(len(f.verts)-2 for f in bm.faces),'boundary_edges':sum(e.is_boundary for e in bm.edges),'non_manifold_edges':sum(not e.is_manifold for e in bm.edges),'body_intersection_pairs':len(hits),'dimensions_m':list(ob.dimensions),'head_pivot':list(bpy.data.objects['Bob attachment - Head'].location),'uv_layer':ob.data.uv_layers.active.name,'material':'Ash brown - flowing hair','additional_paid_generation':False,'unity_imported':False,'exports_created':False};bm.free()
world_vertices=[ob.matrix_world@v.co for v in ob.data.vertices]
report['hair_top_world_z']=max(v.z for v in world_vertices)
report['player_top_world_z']=max((body.matrix_world@v.co).z for v in body.data.vertices)
report['crown_extra_height_m']=report['hair_top_world_z']-report['player_top_world_z']
tree=KDTree(len(world_vertices))
for i,v in enumerate(world_vertices):tree.insert(v,i)
tree.balance()
report['symmetry_max_error_m']=max(tree.find(Vector((-v.x,v.y,v.z)))[2] for v in world_vertices)
report['left_right_width_difference_m']=abs(max(v.x for v in world_vertices)+min(v.x for v in world_vertices))
(P/'validation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
bpy.ops.object.select_all(action='DESELECT');ob.select_set(True);bpy.context.view_layer.objects.active=ob
bpy.ops.wm.save_as_mainfile(filepath=str(P/'LayeredBob_FitReview.blend'))
# Keep only the hair rig attachment, hair, review lights and cameras.
for name in ['FemaleBody','FemaleRig']:
 o=bpy.data.objects.get(name)
 if o:bpy.data.objects.remove(o,do_unlink=True)
bpy.ops.wm.save_as_mainfile(filepath=str(P/'LayeredBob_Editable.blend'))
print('BOB_QA',json.dumps(report),flush=True)
assert report['connected_components']==1
assert report['non_manifold_edges']==0
assert report['boundary_edges']==0

assert report['symmetry_max_error_m']<.000002
assert report['body_intersection_pairs']==0
