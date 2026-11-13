"""Resizes the furniture .blend sources in place to furniture_sizes_20261107.json by baking the new size into mesh vertices
(no object scale is left behind and Unity keeps scale 1). Idempotent. Covers the base model and the four material variants of the
Household, Commercial and CommercialExtras sets, re-exports the Household FBX/GLB, and updates the manifests.
Run: blender -b --factory-startup --addons cycles --python ArtSource/Items/Furniture/apply_sizes.py
"""
import bpy
import hashlib
import json
import sys
from pathlib import Path
from mathutils import Matrix, Vector

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
TARGETS = json.loads((HERE / 'furniture_sizes_20261107.json').read_text(encoding='utf8'))['items']
HOUSE = HERE / 'HouseholdLowPoly'
LOW = HERE / 'CommercialLowPoly_20261105'
EXT = HERE / 'CommercialExtras_20261105'
STYLES = ('WhiteWood', 'Eclectic', 'MarbleGold', 'ChicBlack')
LOW_STYLES = ('WhiteWood', 'WhiteBlack', 'GlossBlack', 'MarbleGold')


def resize_open_file(identifier):
    """Returns the new (dimensions, min, max), or None when the file has no such asset."""
    collection = bpy.data.collections.get(identifier + '_ASSET')
    if not collection:
        return None
    meshes = [o for o in collection.objects if o.type == 'MESH']
    if not meshes:
        return None
    bpy.context.view_layer.update()
    points = [o.matrix_world @ v.co for o in meshes for v in o.data.vertices]
    low = [min(p[i] for p in points) for i in range(3)]
    high = [max(p[i] for p in points) for i in range(3)]
    size = [high[i] - low[i] for i in range(3)]
    target = TARGETS[identifier]
    factor = [(target[i] / size[i]) if target[i] and size[i] > 1e-5 else 1.0 for i in range(3)]
    if all(abs(f - 1) < 1e-4 for f in factor):
        return size, low, high
    scale = Matrix.Diagonal((*factor, 1.0))
    seen = set()
    for o in collection.objects:
        world = o.matrix_world.copy()
        moved = Matrix.Translation(scale @ world.translation)
        if o.type == 'MESH':
            # Parts may be rotated, so bake in world space; the object keeps a clean, unscaled pivot at the scaled position.
            if o.data.users > 1 or o.data.name in seen:
                o.data = o.data.copy()
            seen.add(o.data.name)
            bake = moved.inverted() @ scale @ world
            for v in o.data.vertices:
                v.co = bake @ v.co
            o.data.update()
        o.matrix_world = moved
    bpy.context.view_layer.update()
    points = [o.matrix_world @ v.co for o in meshes for v in o.data.vertices]
    low = [min(p[i] for p in points) for i in range(3)]
    high = [max(p[i] for p in points) for i in range(3)]
    return [high[i] - low[i] for i in range(3)], low, high


def geometry_hash(identifier):
    meshes = sorted((o for o in bpy.data.collections[identifier + '_ASSET'].objects if o.type == 'MESH'), key=lambda o: o.name)
    return hashlib.sha256(json.dumps([(o.name, [list(v.co) for v in o.data.vertices], [list(p.vertices) for p in o.data.polygons],
                                       list(o.location), list(o.rotation_euler)) for o in meshes]).encode()).hexdigest()


def select_asset(identifier):
    collection = bpy.data.collections[identifier + '_ASSET']
    bpy.ops.object.select_all(action='DESELECT')
    for o in collection.objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = bpy.data.objects[identifier]


def export_asset(path, embedded):
    bpy.ops.export_scene.fbx(filepath=str(path.with_suffix('.fbx')), use_selection=True, object_types={'MESH', 'EMPTY'},
                             axis_forward='-Z', axis_up='Y', apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
                             use_custom_props=True, add_leaf_bones=False, bake_anim=False,
                             path_mode='COPY' if embedded else 'AUTO', embed_textures=embedded)
    bpy.ops.export_scene.gltf(filepath=str(path.with_suffix('.glb')), use_selection=True, export_format='GLB')


def process(path, identifier, export=None, embedded=False):
    bpy.ops.wm.open_mainfile(filepath=str(path), load_ui=False, use_scripts=False)
    result = resize_open_file(identifier)
    if result is None:
        print('SIZE_SKIP no asset', path, flush=True)
        return None
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(path))
    if export:
        select_asset(identifier)
        export_asset(export, embedded)
    return result, geometry_hash(identifier)


def update_row(row, result, sha):
    (size, low, high) = result
    row.update(dimensions=list(size), bounds_min=list(low), bounds_max=list(high))
    if 'geometry_sha256' in row:
        row['geometry_sha256'] = sha


report = []
households = sorted(p.stem for p in (HOUSE / 'Models').glob('*.blend'))
catalog = json.loads((HOUSE / 'Catalog.json').read_text(encoding='utf8'))
for identifier in households:
    if identifier not in TARGETS:
        print('SIZE_NO_TARGET', identifier, flush=True)
        continue
    out = HOUSE / 'Exports' / identifier
    done = process(HOUSE / 'Models' / f'{identifier}.blend', identifier, out / f'{identifier}_Cream')
    if not done:
        continue
    result, sha = done
    report.append((identifier, [round(x, 3) for x in result[0]]))
    manifest = HOUSE / 'QA' / identifier / 'Manifest.json'
    row = json.loads(manifest.read_text(encoding='utf8'))
    update_row(row, result, sha)
    manifest.write_text(json.dumps(row, ensure_ascii=False, indent=2), encoding='utf8')
    for entry in catalog:
        if entry['id'] == identifier:
            update_row(entry, result, sha)
    for style in STYLES:
        folder = HOUSE / 'MaterialVariants' / style / identifier
        styled = process(folder / f'{identifier}_{style}.blend', identifier, folder / f'{identifier}_{style}', embedded=True)
        if styled:
            manifest = folder / 'Manifest.json'
            row = json.loads(manifest.read_text(encoding='utf8'))
            update_row(row, *styled)
            manifest.write_text(json.dumps(row, ensure_ascii=False, indent=2), encoding='utf8')
    print('SIZE_DONE', identifier, report[-1][1], flush=True)
(HOUSE / 'Catalog.json').write_text(json.dumps(catalog, ensure_ascii=False, indent=2), encoding='utf8')

# Sources changed on purpose: the variant builder's preservation check compares against these hashes.
baseline_path = HOUSE / 'MaterialVariants' / 'SourceBaseline.json'
baseline = json.loads(baseline_path.read_text(encoding='utf8'))
for key in baseline:
    baseline[key] = hashlib.sha256((HOUSE / key).read_bytes()).hexdigest()
baseline_path.write_text(json.dumps(baseline, indent=2), encoding='utf8')

for base, styles in ((LOW, LOW_STYLES), (EXT, LOW_STYLES)):
    rows_path = base / 'QA' / 'catalog.json'
    rows = json.loads(rows_path.read_text(encoding='utf8'))
    listing = rows if isinstance(rows, list) else rows.get('items', rows)
    for row in listing:
        identifier = row['id']
        if identifier not in TARGETS:
            print('SIZE_NO_TARGET', identifier, flush=True)
            continue
        done = process(base / 'Models' / f'{identifier}.blend', identifier)
        if not done:
            continue
        update_row(row, *done)
        report.append((identifier, [round(x, 3) for x in done[0][0]]))
        for style in styles:
            variant = base / 'MaterialVariants' / style / 'Models' / f'{identifier}_{style}.blend'
            if variant.exists():
                process(variant, identifier)
        print('SIZE_DONE', identifier, report[-1][1], flush=True)
    rows_path.write_text(json.dumps(rows, ensure_ascii=False, indent=2), encoding='utf8')

(HERE / 'furniture_sizes_applied.json').write_text(json.dumps(report, ensure_ascii=False, indent=1), encoding='utf8')
print('SIZE_ALL_DONE', len(report), flush=True)
