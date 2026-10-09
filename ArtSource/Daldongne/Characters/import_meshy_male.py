"""Prepare the supplied male FBX for the existing Unity player rig.

Blender --background --factory-startup --python <this file>.
Retains the original topology. Reviewed shorts face IDs select solid materials;
UV2 stores anatomical regions only and never controls the visible color.
"""
from pathlib import Path
import bpy
import hashlib
import json
import numpy as np
from mathutils import Vector

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
DEST = ROOT / 'CompanyGame/Assets/Art/Daldongne/Players/MeshyMale'


def load_source():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(HERE / 'MeshyMaleHigh.fbx'))
    obj = next(o for o in bpy.context.scene.objects if o.type == 'MESH')
    obj.data.transform(obj.matrix_world)
    obj.matrix_world.identity()
    vertices = obj.data.vertices
    low = min(v.co.z for v in vertices)
    scale = 1.8 / (max(v.co.z for v in vertices) - low)
    # The original origin is at the body centre; only ground height is adjusted.
    for vertex in vertices:
        vertex.co = Vector((vertex.co.x * scale, vertex.co.y * scale,
                            (vertex.co.z - low) * scale))
    obj.data.update()
    return obj


def prepare():
    obj = load_source()
    mesh = obj.data
    selection = json.loads((HERE / 'MeshyMaleHigh.shorts-faces.json').read_text())
    assert selection['triangles'] == len(mesh.polygons), 'Shorts selection topology mismatch'
    assert selection['sha256'] == hashlib.sha256((HERE / 'MeshyMaleHigh.fbx').read_bytes()).hexdigest(), 'Shorts selection source mismatch'
    shorts = set(selection['shorts'])
    assert shorts and min(shorts) >= 0 and max(shorts) < len(mesh.polygons)
    # Below the armpits the surface consists of a torso/legs component and two
    # arm components. Follow connectivity so inner forearm vertices never inherit
    # torso weights merely because their X coordinate is close to the body.
    points = np.array([v.co[:] for v in mesh.vertices])
    edge_array = np.array([e.vertices[:] for e in mesh.edges], dtype=np.int32)
    adjacent = [[] for _ in mesh.vertices]
    for a, b in edge_array:
        adjacent[a].append(b)
        adjacent[b].append(a)
    unseen = set(np.flatnonzero(points[:, 2] < 1.14))
    components = []
    while unseen:
        first = unseen.pop()
        component, pending = [first], [first]
        while pending:
            for neighbor in adjacent[pending.pop()]:
                if neighbor in unseen:
                    unseen.remove(neighbor)
                    component.append(neighbor)
                    pending.append(neighbor)
        components.append(component)
    components.sort(key=len, reverse=True)
    assert len(components) == 3 and all(len(c) > 10000 for c in components), 'Arm surface topology changed'
    arm_influence = np.zeros(len(points))
    for arm in components[1:]:
        arm_influence[arm] = 1
    shoulder_band = (points[:, 2] >= 1.14) & (points[:, 2] < 1.30)
    arm_influence[shoulder_band & (np.abs(points[:, 0]) > .105)] = 1
    a, b = edge_array[:, 0], edge_array[:, 1]
    degree = np.bincount(np.r_[a, b], minlength=len(points))
    # Blend only across the shoulder surface; hand and forearm influence stays
    # fixed. This avoids discontinuities between the upper arm and chest.
    for _ in range(100):
        average = (np.bincount(a, weights=arm_influence[b], minlength=len(points))
                   + np.bincount(b, weights=arm_influence[a], minlength=len(points))) / np.maximum(degree, 1)
        arm_influence[shoulder_band] = average[shoulder_band]
    mesh.materials.clear()
    mesh.materials.append(bpy.data.materials.new('Skin'))
    mesh.materials.append(bpy.data.materials.new('Shorts'))
    surface = mesh.uv_layers.new(name='UVMap')
    regions = mesh.uv_layers.new(name='Region')
    for face in mesh.polygons:
        face.use_smooth = True
        face.material_index = int(face.index in shorts)
        for loop in face.loop_indices:
            v = mesh.vertices[mesh.loops[loop].vertex_index].co
            if v.z > 1.30:
                region = 1
            elif v.z < .87:
                region = 2 if v.x < 0 else 7
            else:
                region = 5
            surface.data[loop].uv = (0, 0)
            regions.data[loop].uv = (region, float(arm_influence[mesh.loops[loop].vertex_index]))
    mesh.uv_layers.active_index = 0
    obj.name = 'MaleBody'
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    DEST.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=str(DEST / 'MaleBody.fbx'), use_selection=True,
                            object_types={'MESH'}, axis_forward='-Z', axis_up='Y',
                            add_leaf_bones=False, bake_anim=False, use_mesh_modifiers=False)
    print('Male source prepared:', len(mesh.vertices), 'vertices,', len(mesh.polygons),
          'triangles;', len(shorts), 'shorts faces; height 1.8 m')


if __name__ == '__main__':
    prepare()
