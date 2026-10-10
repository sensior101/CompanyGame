"""Shared read-only FBX/GLB round-trip checks, driven by each model manifest.

No arguments retain the original Office01 validation behaviour. Blender usage:
    blender --background --python-exit-code 1 --python verify_roundtrip.py
    blender --background --python-exit-code 1 --python verify_roundtrip.py -- \
        --model-dir <model-folder> --report <optional-report-path>
"""
import argparse
import bpy
import hashlib
import json
import math
import sys
from datetime import datetime, timezone
from pathlib import Path
from mathutils import Vector

HERE = Path(__file__).resolve().parent


def validate_export(path, manifest):
    """Import one export into a disposable scene; never save source or export."""
    path = Path(path)
    suffix = path.suffix.lower().lstrip('.')
    expected_dimensions = manifest['building_dimensions_xyz_m']
    expected_root = manifest['asset']
    before = hashlib.sha256(path.read_bytes()).hexdigest()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if suffix == 'fbx':
        bpy.ops.import_scene.fbx(filepath=str(path))
    else:
        bpy.ops.import_scene.gltf(filepath=str(path))
    bpy.context.view_layer.update()
    objects = list(bpy.context.scene.objects)
    meshes = [ob for ob in objects if ob.type == 'MESH']
    points = [ob.matrix_world @ Vector(corner) for ob in meshes for corner in ob.bound_box]
    if not points:
        raise ValueError(f'No exported mesh geometry: {path}')
    lower = [min(p[i] for p in points) for i in range(3)]
    upper = [max(p[i] for p in points) for i in range(3)]
    size = [upper[i] - lower[i] for i in range(3)]
    mesh_rows = []
    for ob in meshes:
        mesh = ob.data
        mesh.calc_loop_triangles()
        zero_area = 0
        invalid_vertices = sum(not all(math.isfinite(v) for v in vert.co) for vert in mesh.vertices)
        for tri in mesh.loop_triangles:
            a, b, c = [ob.matrix_world @ mesh.vertices[idx].co for idx in tri.vertices]
            if (b-a).cross(c-a).length * .5 <= 1e-10:
                zero_area += 1
        uv = mesh.uv_layers.active
        invalid_uvs = 0 if uv is None else sum(not all(math.isfinite(v) for v in item.uv) for item in uv.data)
        missing_materials = not len(mesh.materials) or any(m is None for m in mesh.materials)
        invalid_material_indices = sum(p.material_index >= len(mesh.materials) for p in mesh.polygons)
        mesh_rows.append({'name': ob.name, 'vertices': len(mesh.vertices), 'triangles': len(mesh.loop_triangles),
            'zero_area_triangles': zero_area, 'invalid_vertices': invalid_vertices,
            'uv_layers': len(mesh.uv_layers), 'uv_loop_count': len(uv.data) if uv else 0,
            'mesh_loop_count': len(mesh.loops), 'invalid_uvs': invalid_uvs,
            'materials': [m.name if m else None for m in mesh.materials],
            'missing_materials': bool(missing_materials), 'invalid_material_indices': invalid_material_indices})
    roots = [ob for ob in objects if ob.parent is None]
    contaminants = [ob.name for ob in objects if ob.type in {'CAMERA', 'LIGHT'} or ob.name.startswith('_Studio') or ob.name.startswith('_Presentation')]
    triangle_count = sum(row['triangles'] for row in mesh_rows)
    after = hashlib.sha256(path.read_bytes()).hexdigest()
    checks = {
        'dimensions_match_manifest': all(abs(size[i] - expected_dimensions[i]) < .005 for i in range(3)),
        'ground_at_zero': abs(lower[2]) < .005,
        'one_named_model_root': len(roots) == 1 and roots[0].name == expected_root,
        'no_presentation_contamination': not contaminants,
        'mesh_count_matches_manifest': len(meshes) == manifest['meshes'],
        'triangle_count_matches_manifest': triangle_count == manifest['triangles'],
        'all_meshes_have_uv0': all(row['uv_layers'] and row['uv_loop_count'] == row['mesh_loop_count'] for row in mesh_rows),
        'no_zero_area_triangles': not any(row['zero_area_triangles'] for row in mesh_rows),
        'all_vertices_and_uvs_finite': not any(row['invalid_vertices'] or row['invalid_uvs'] for row in mesh_rows),
        'no_missing_materials': not any(row['missing_materials'] or row['invalid_material_indices'] for row in mesh_rows),
        'file_unchanged_during_verification': before == after,
    }
    result = {'file': str(path), 'sha256': before, 'file_bytes': path.stat().st_size,
        'file_mtime_utc': datetime.fromtimestamp(path.stat().st_mtime, timezone.utc).isoformat(),
        'bounds_min_xyz_m': lower, 'bounds_max_xyz_m': upper, 'dimensions_xyz_m': size,
        'object_count': len(objects), 'mesh_count': len(meshes), 'triangle_count': triangle_count,
        'root_names': [ob.name for ob in roots],
        'contaminants': contaminants, 'checks': checks, 'passed': all(checks.values()), 'meshes': mesh_rows}
    print('EXPORT_CHECK ' + json.dumps({k:v for k,v in result.items() if k != 'meshes'}))
    return result


def validate_model(model_dir, report_path=None):
    """Validate both formats against one manifest; return and persist evidence.

    The same implementation serves Office01, the A-D set, and future buildings.
    Missing or malformed exports become failed evidence instead of silently
    skipping a format. Only report files and their folders are written.
    """
    model_dir = Path(model_dir).resolve()
    manifest_path = model_dir / 'model_manifest.json'
    manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
    if len(manifest['building_dimensions_xyz_m']) != 3:
        raise ValueError(f'Expected three metric dimensions in {manifest_path}')
    report = {
        'checked_at_utc': datetime.now(timezone.utc).isoformat(),
        'asset': manifest['asset'],
        'model_dir': str(model_dir),
        'manifest_sha256': hashlib.sha256(manifest_path.read_bytes()).hexdigest(),
        'validator': str(Path(__file__).resolve()),
        'expected_dimensions_xyz_m': manifest['building_dimensions_xyz_m'],
        'expected_mesh_count': manifest['meshes'],
        'expected_triangle_count': manifest['triangles'],
        'exports': [],
    }
    for suffix in ('fbx', 'glb'):
        path = model_dir / 'UnityExport' / (manifest['asset'] + '_Blockout.' + suffix)
        try:
            result = validate_export(path, manifest)
        except Exception as error:
            result = {'file': str(path), 'passed': False,
                      'error': f'{type(error).__name__}: {error}'}
            print('EXPORT_CHECK ' + json.dumps(result))
        report['exports'].append(result)
    report['passed'] = len(report['exports']) == 2 and all(
        result['passed'] for result in report['exports'])
    destination = Path(report_path) if report_path else model_dir / 'Validation' / 'export_roundtrip.json'
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print('ROUNDTRIP_' + ('PASS' if report['passed'] else 'FAIL') + ' ' + manifest['asset'])
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--model-dir', type=Path, default=HERE.parent)
    parser.add_argument('--report', type=Path)
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
    report = validate_model(args.model_dir, args.report)
    if not report['passed']:
        raise RuntimeError('Round-trip validation failed; see the JSON report.')


if __name__ == '__main__':
    main()
