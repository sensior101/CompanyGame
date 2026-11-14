"""Exports the approved rectangular shop shell and the commercial WhiteWood furniture used by the
real-estate office into CompanyGame/Assets/Art/Interiors/RealEstateOffice. Source .blend files are only
opened and never saved. Run:
blender -b --factory-startup --python ArtSource/WorldDistricts/RealEstateInterior_20261107/export_interior.py
"""
import bpy
import json
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
FURN = ROOT / 'ArtSource/Items/Furniture'
LOW = FURN / 'CommercialLowPoly_20261105/MaterialVariants/WhiteWood/Models'
EXT = FURN / 'CommercialExtras_20261105/MaterialVariants/WhiteWood/Models'
OUT = ROOT / 'CompanyGame/Assets/Art/Interiors/RealEstateOffice/Models'
OUT.mkdir(parents=True, exist_ok=True)
MODELS = {
    'ShopShell': ROOT / 'ArtSource/WorldDistricts/ShopShell_WindowReady_20261105/ShopShell_NoWindows.blend',
    **{n: LOW / f'{n}_WhiteWood.blend' for n in (
        'Counter05', 'GuestChair', 'WaitingBench', 'MeetingTable', 'HighPartition', 'LowPartition', 'Whiteboard',
        'Fluorescent', 'WallClock', 'PictureFrame', 'Poster', 'Plant02', 'Plant04', 'Plant05', 'Rug01', 'Rug06')},
    **{n: EXT / f'{n}_WhiteWood.blend' for n in ('WaterDispenser', 'UmbrellaStand', 'DeskTelephone')},
}
materials = {}
report = {}
for name, path in MODELS.items():
    bpy.ops.wm.open_mainfile(filepath=str(path))
    # Only the asset collection: the extras files also hold a presentation floor, camera and lights.
    objects = [o for o in bpy.data.objects if o.type in {'MESH', 'EMPTY'} and any(c.name.endswith('_ASSET') for c in o.users_collection)]
    if not objects:
        raise RuntimeError(f'No *_ASSET collection in {path}')
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    for o in objects:
        if o.type != 'MESH':
            continue
        for m in o.data.materials:
            if not m or m.name in materials:
                continue
            b = m.node_tree.nodes.get('Principled BSDF')
            color = list(b.inputs['Base Color'].default_value)
            materials[m.name] = dict(name=m.name, color=color[:3], metallic=b.inputs['Metallic'].default_value,
                                     roughness=b.inputs['Roughness'].default_value, emission=b.inputs['Emission Strength'].default_value)
    bpy.ops.export_scene.fbx(filepath=str(OUT / f'{name}.fbx'), use_selection=True, object_types={'MESH', 'EMPTY'},
                             axis_forward='-Z', axis_up='Y', apply_unit_scale=True, use_custom_props=True,
                             add_leaf_bones=False, bake_anim=False, path_mode='AUTO')
    report[name] = dict(source=str(path.relative_to(ROOT)), meshes=sum(o.type == 'MESH' for o in objects),
                        triangles=sum(len(p.vertices) - 2 for o in objects if o.type == 'MESH' for p in o.data.polygons),
                        imageSurface=[o.name for o in objects if o.get('ImageSurface')])
(OUT.parent / 'materials.json').write_text(json.dumps(dict(materials=list(materials.values())), indent=1), encoding='utf8')
(HERE / 'export_report.json').write_text(json.dumps(report, indent=1), encoding='utf8')
print('EXPORT_DONE', len(report), 'models', len(materials), 'materials')
