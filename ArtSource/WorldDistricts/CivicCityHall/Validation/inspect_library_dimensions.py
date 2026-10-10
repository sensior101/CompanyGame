"""Read-only Blender import measurement of the library assets used by Unity."""
import bpy
import json
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[4]
HERE = Path(__file__).resolve().parent


def bounds(objects):
    points = [o.matrix_world @ v.co for o in objects for v in o.data.vertices]
    if not points:
        return None
    low = [min(v[i] for v in points) for i in range(3)]
    high = [max(v[i] for v in points) for i in range(3)]
    return {'min_xyz': low, 'max_xyz': high,
            'width_depth_height_m': [high[i]-low[i] for i in range(3)],
            'mesh_count': len(objects)}


report = {}
for folder, filename in [('CivicLibrary', 'CivicLibrary.fbx'),
                         ('CivicLibraryV4', 'Library_V4.fbx')]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    path = ROOT / 'CompanyGame/Assets/Art/WorldDistricts' / folder / filename
    bpy.ops.import_scene.fbx(filepath=str(path))
    bpy.context.view_layer.update()
    objects = [o for o in bpy.data.objects if o.type == 'MESH']
    entry = {'asset': str(path), 'all_meshes': bounds(objects), 'groups': {}}
    if folder == 'CivicLibrary':
        groups = sorted({o.parent.name if o.parent else 'None' for o in objects})
        for group in groups:
            entry['groups'][group] = bounds([o for o in objects if o.parent and o.parent.name == group])
        entry['site_objects'] = {
            o.name: bounds([o]) for o in objects
            if 'Site' in o.name or any(m and m.name == 'CL_Pavement' for m in o.data.materials)
        }
    else:
        visible = [o for o in objects if not o.name.startswith(('COLLIDER_MESH_', 'COL_'))]
        entry['visible_meshes'] = bounds(visible)
        entry['floor_objects'] = {o.name: bounds([o]) for o in visible if 'Floor' in o.name}
    report[folder] = entry

output = HERE / 'library_measured_bounds.json'
output.write_text(json.dumps(report, indent=2), encoding='utf-8')
print('LIBRARY_READONLY_BOUNDS', json.dumps(report))
