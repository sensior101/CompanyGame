"""Prepare the full-resolution FBX for Unity without decimation or mesh cuts.

Blender --background --factory-startup --python <this file>.
The earlier segmented GLB supplies rigging labels only, never geometry or color.
Reviewed face selections follow the garment seams of this exact high-resolution FBX.
Each triangle has one solid material. UV1 records rigging regions.
"""
from pathlib import Path
import bpy
import json
import hashlib
from mathutils import Vector
from mathutils.kdtree import KDTree
from collections import Counter

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
DEST = ROOT / 'CompanyGame/Assets/Art/Daldongne/Players/MeshyFemale'
DEST.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(HERE / 'MeshyFemale.glb'))
reference = []
for obj in bpy.context.scene.objects:
    if obj.type == 'MESH':
        region = int(obj.name.split('part')[-1])
        reference.extend((obj.matrix_world @ v.co * 15, region) for v in obj.data.vertices)
tree = KDTree(len(reference))
for i, (position, _) in enumerate(reference):
    tree.insert(position, i)
tree.balance()
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=str(HERE / 'MeshyFemaleHigh.fbx'))
obj = next(o for o in bpy.context.scene.objects if o.type == 'MESH')
obj.data.transform(obj.matrix_world)
obj.matrix_world.identity()
low = min(v.co.z for v in obj.data.vertices)
high = max(v.co.z for v in obj.data.vertices)
scale = 1.8 / (high - low)
for v in obj.data.vertices:
    v.co = Vector((v.co.x * scale, v.co.y * scale, (v.co.z - low) * scale))
obj.data.update()
labels = [reference[tree.find(v.co)[1]][1] for v in obj.data.vertices]
neighbors = [[] for _ in obj.data.vertices]
for edge in obj.data.edges:
    a, b = edge.vertices
    neighbors[a].append(b)
    neighbors[b].append(a)
# Remove isolated region speckles caused by differences between the two meshes.
for _ in range(8):
    labels = [Counter([labels[i], labels[i]] + [labels[j] for j in near]).most_common(1)[0][0]
              for i, near in enumerate(neighbors)]
palette = obj.data.uv_layers.new(name='UVMap')
regions = obj.data.uv_layers.new(name='Region')
obj.data.materials.clear()
obj.data.materials.append(bpy.data.materials.new('Skin'))
obj.data.materials.append(bpy.data.materials.new('Underwear'))
selection = json.loads((HERE / 'MeshyFemaleHigh.underwear-faces.json').read_text())
assert selection['triangles'] == len(obj.data.polygons), 'Garment selection topology mismatch'
assert selection['sha256'] == hashlib.sha256((HERE / 'MeshyFemaleHigh.fbx').read_bytes()).hexdigest(), 'Garment selection belongs to a different source'
underwear = set(selection['underwear'])
assert underwear and min(underwear) >= 0 and max(underwear) < len(obj.data.polygons)
for face in obj.data.polygons:
    face.use_smooth = True
    face.material_index = int(face.index in underwear)
    for loop in face.loop_indices:
        vertex = obj.data.loops[loop].vertex_index
        palette.data[loop].uv = (0, 0)
        regions.data[loop].uv = (labels[vertex], 0)
obj.data.normals_split_custom_set([(0, 0, 0)] * len(obj.data.loops))
if 'sharp_edge' in obj.data.attributes:
    obj.data.attributes.remove(obj.data.attributes['sharp_edge'])
obj.data.uv_layers.active_index = 0
obj.name = 'FemaleBody'
bpy.ops.object.select_all(action='DESELECT')
obj.select_set(True)
bpy.context.view_layer.objects.active = obj
bpy.ops.export_scene.fbx(filepath=str(DEST / 'FemaleBody.fbx'), use_selection=True,
                        object_types={'MESH'}, axis_forward='-Z', axis_up='Y',
                        add_leaf_bones=False, bake_anim=False, use_mesh_modifiers=False)
obj.data.calc_loop_triangles()
print('Full-resolution female:', len(obj.data.vertices), 'vertices,',
      len(obj.data.loop_triangles), 'triangles; height 1.8 m')
