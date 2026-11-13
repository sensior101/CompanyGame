"""Four material treatments for the 45 commercial furniture assets.
Reuses HouseholdLowPoly texture/material builders and geometry signatures.
Writes Blender authoring files, packed textures and previews only.
"""
import bpy
import hashlib
import importlib.util
import json
import sys
from pathlib import Path
from mathutils import Vector

sys.dont_write_bytecode = True
BASE = Path(__file__).resolve().parent
OUT = BASE / 'MaterialVariants'
for name in ('Textures', 'Previews', 'QA'):
    (OUT / name).mkdir(parents=True, exist_ok=True)
spec = importlib.util.spec_from_file_location('commercial', BASE / 'build_commercial.py')
commercial = importlib.util.module_from_spec(spec)
spec.loader.exec_module(commercial)
h = commercial.h
STYLES = {
    'WhiteWood': '화이트앤우드',
    'WhiteBlack': '화이트앤블랙',
    'GlossBlack': '유광 블랙 메탈',
    'MarbleGold': '대리석앤골드',
}
ENTRIES = json.loads((BASE / 'QA/catalog.json').read_text(encoding='utf8'))
GPU = False

def surface(style, obj, entry):
    role = obj['Role']
    name = obj.name.lower()
    identifier = entry['id']
    category = entry['category']
    if obj.get('ImageSurface'):
        return ('ImageWhite', 'FFFFFF', 0, .75, None)
    if category == 'Plants' and name.startswith(('facetedleaf', 'leafstalk', 'cactus')):
        return ('LeafAccent' if role == 'Accent' else 'Leaf', '81945D' if role == 'Accent' else '536F48', 0, .68, None)
    if category == 'Plants' and name.startswith('stem'):
        return ('Stem', '736747', 0, .82, None)
    if name.startswith('soil'):
        return ('Soil', '312B24', 0, 1, None)
    if name.startswith('writingsurface'):
        return ('ChalkSurface', '242A28', 0, .92, None) if identifier == 'Blackboard' else ('WhiteboardSurface', 'FAFAF8', 0, .23, None)
    if identifier == 'WallClock' and name.startswith(('hour', 'minute')):
        return ('ClockInk', '171B1D', 0, .45, None)
    if identifier == 'WallClock' and name.startswith('clockface'):
        return ('ClockFace', 'F7F5EF', 0, .35, None)
    if name.startswith(('tube', 'diffuser')):
        return ('Diffuser', 'FFF8EB', 0, .32, None)
    if role in ('Glass', 'Rubber'):
        return h.style_surface('WhiteWood', role, obj.name, entry)

    textile = category == 'Rugs' or (category == 'Seating' and (
        role in ('Fabric', 'Accent') or
        identifier in ('CornerSofa', 'Armchair') and role == 'Body'))
    textile = textile or identifier in ('LowPartition', 'HighPartition', 'CubicleDesk') and role == 'Fabric'
    if textile:
        accent = role == 'Accent' or any(word in name for word in ('border', 'check', 'stripe'))
        colors = {
            'WhiteWood': ('EEEAE1', 'C8AD87'),
            'WhiteBlack': ('EEEDE9', '202329'),
            'GlossBlack': ('171B20', '30353B'),
            'MarbleGold': ('EEE7D7', 'BE9D58'),
        }
        color = colors[style][int(accent)]
        leather = style == 'GlossBlack' and category == 'Seating'
        return ('BlackLeather' if leather else 'UpholsteryAccent' if accent else 'Upholstery', color, 0, .28 if leather else .82, None)

    trim = role == 'Metal' or any(word in name for word in (
        'leg', 'foot', 'frame', 'rail', 'brace', 'stem', 'post', 'upright',
        'topcap', 'handle', 'pull', 'pedestal', 'socket', 'tray', 'trim',
        'shelfedge', 'base', 'hourmark'))
    trim = trim or role == 'Dark'
    top = any(word in name for word in ('tabletop', 'worktop', 'returntop')) or name.split('.')[0] == 'top'
    if style == 'WhiteWood':
        return h.style_surface('WhiteWood', role, obj.name, entry)
    if style == 'WhiteBlack':
        black = trim or top or role == 'Accent'
        if category == 'Tables' and top:
            black = False
        return ('BlackSatin' if black else 'PorcelainWhite', '1B1E23' if black else 'F4F3EF', .45 if role == 'Metal' else 0, .30 if black else .38, None)
    if style == 'GlossBlack':
        return ('PolishedBlackMetal', '292E35' if trim else '20252B', .94, .16 if trim else .20, None)
    if trim and not top:
        return ('PolishedGold', 'D9AE58', 1, .20, None)
    return ('CalacattaMarble', 'F2EFE5', 0, .23, 'Calacatta')

def material(style, treatment, cache):
    mat = h.style_material(style, treatment, OUT / 'Textures', cache)
    if mat.get('CommercialFinish'):
        return mat
    mat['CommercialFinish'] = True
    label, color, metallic, roughness, texture = treatment
    nodes = mat.node_tree.nodes
    links = mat.node_tree.links
    shader = nodes['Principled BSDF']
    if label in ('PolishedBlackMetal', 'PolishedGold', 'BlackLeather', 'CalacattaMarble'):
        shader.inputs['Coat Weight'].default_value = .28 if label != 'BlackLeather' else .18
        shader.inputs['Coat Roughness'].default_value = .14
    if texture:
        image = next(node for node in nodes if node.type == 'TEX_IMAGE')
        image.projection = 'BOX'
        image.projection_blend = .12
        coordinates = nodes.new('ShaderNodeTexCoord')
        mapping = nodes.new('ShaderNodeMapping')
        mapping.inputs['Scale'].default_value = (1.4, 1.4, 1.4) if texture == 'Calacatta' else (1, .5, 1)
        links.new(coordinates.outputs['Generated'], mapping.inputs['Vector'])
        links.new(mapping.outputs['Vector'], image.inputs['Vector'])
        bump = nodes.new('ShaderNodeBump')
        bump.inputs['Strength'].default_value = .08 if texture == 'Calacatta' else .16
        bump.inputs['Distance'].default_value = .0006 if texture == 'Calacatta' else .0012
        links.new(image.outputs['Color'], bump.inputs['Height'])
        links.new(bump.outputs['Normal'], shader.inputs['Normal'])
        image.image.pack()
    if label.startswith('Upholstery') or label == 'BlackLeather':
        noise = nodes.new('ShaderNodeTexNoise')
        noise.inputs['Scale'].default_value = 165
        bump = nodes.new('ShaderNodeBump')
        bump.inputs['Strength'].default_value = .13
        bump.inputs['Distance'].default_value = .0008
        links.new(noise.outputs['Fac'], bump.inputs['Height'])
        links.new(bump.outputs['Normal'], shader.inputs['Normal'])
    return mat

def apply_style(collection, entry, style, cache):
    before = h.material_geometry_signature(collection)
    for obj in collection.objects:
        if obj.type == 'MESH':
            assert len(obj.data.materials) == 1, obj.name
            obj.data.materials[0] = material(style, surface(style, obj, entry), cache)
        if obj.type == 'EMPTY':
            obj['MaterialStyle'] = STYLES[style]
            obj['Palette'] = style
    assert h.material_geometry_signature(collection) == before, entry['id']

def save(path):
    bpy.ops.file.pack_all()
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(path), compress=True)

def configure_render():
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 24
    scene.cycles.use_denoising = True
    if GPU:
        preferences = bpy.context.preferences.addons['cycles'].preferences
        preferences.compute_device_type = 'OPTIX'
        preferences.get_devices()
        for device in preferences.devices:
            device.use = device.type == 'OPTIX'
    scene.cycles.device = 'GPU' if GPU else 'CPU'

def reflection_lights(target=(0, 0, 0), large=False):
    span = 10 if large else 5
    for position, energy, size, height in (
        ((target[0]+span, target[1]+2, 7), 2000, 2, 7),
        ((target[0]-span, target[1]-3, 6), 1500, 2, 6),
    ):
        bpy.ops.object.light_add(type='AREA', location=position)
        obj = bpy.context.object
        obj.rotation_euler = (Vector(target)-obj.location).to_track_quat('-Z', 'Y').to_euler()
        obj.data.energy = energy
        obj.data.shape = 'RECTANGLE'
        obj.data.size = size
        obj.data.size_y = height

def build_variants():
    h.make_style_textures(OUT / 'Textures')
    protected = list((BASE / 'Models').glob('*.blend')) + [BASE / 'CommercialFurniture_RoomScale.blend']
    baseline = {str(path.relative_to(BASE)): hashlib.sha256(path.read_bytes()).hexdigest() for path in protected}
    (OUT / 'QA/SourceBaseline.json').write_text(json.dumps(baseline, indent=2), encoding='utf8')
    records = []
    for entry in ENTRIES:
        identifier = entry['id']
        bpy.ops.wm.open_mainfile(filepath=str(BASE / 'Models' / f'{identifier}.blend'), load_ui=False, use_scripts=False)
        collection = bpy.data.collections[identifier+'_ASSET']
        signature = h.material_geometry_signature(collection)
        cache = {}
        for style in STYLES:
            folder = OUT / style / 'Models'
            folder.mkdir(parents=True, exist_ok=True)
            apply_style(collection, entry, style, cache)
            if style == 'WhiteWood':
                reflection_lights((0, 0, .5))
            configure_render()
            path = folder / f'{identifier}_{style}.blend'
            save(path)
            records.append(dict(id=identifier, label=entry['label'], style=style,
                                style_label=STYLES[style], blend=str(path.relative_to(OUT)),
                                geometry_signature=signature, dimensions=entry['dimensions'],
                                surfaces=sorted({obj.data.materials[0]['SurfaceFinish'] for obj in collection.objects if obj.type=='MESH'})))
        print('STYLED', identifier, flush=True)
    (OUT / 'QA/catalog.json').write_text(json.dumps(records, ensure_ascii=False, indent=2), encoding='utf8')
    assert all(hashlib.sha256((BASE/path).read_bytes()).hexdigest()==digest for path,digest in baseline.items())

def showrooms():
    for style in STYLES:
        bpy.ops.wm.open_mainfile(filepath=str(BASE / 'CommercialFurniture_RoomScale.blend'), load_ui=False, use_scripts=False)
        cache = {}
        for entry in ENTRIES:
            apply_style(bpy.data.collections[entry['id']+'_ASSET'], entry, style, cache)
        reflection_lights((0, 0, 0), True)
        configure_render()
        scene = bpy.context.scene
        scene.render.resolution_x = 1600
        scene.render.resolution_y = 1060
        scene.render.filepath = str(OUT / 'Previews' / f'{style}_Room.png')
        save(OUT / style / f'CommercialFurniture_{style}.blend')
        bpy.ops.render.render(write_still=True)
        print('SHOWROOM', style, flush=True)

def comparisons():
    groups = {
        'Seating': ('BarStool', 'OfficeChair', 'Armchair', 'CornerSofa'),
        'Work': ('CubicleDesk', 'Counter01', 'Counter03', 'GlassCounter'),
        'Decor': ('FloorLamp', 'PictureFrame', 'Plant04', 'Rug05'),
    }
    for group, identifiers in groups.items():
        bpy.ops.wm.read_factory_settings(use_empty=True)
        ink = bpy.data.materials.new('Ink')
        ink.use_nodes = True
        ink.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (.018, .022, .025, 1)
        for row, style in enumerate(STYLES):
            for column, identifier in enumerate(identifiers):
                path = OUT / style / 'Models' / f'{identifier}_{style}.blend'
                with bpy.data.libraries.load(str(path), link=False) as (src, dst):
                    dst.collections = [identifier+'_ASSET']
                collection = dst.collections[0]
                bpy.context.scene.collection.children.link(collection)
                root = next(obj for obj in collection.objects if obj.type == 'EMPTY')
                root.location = (column*3.1, -row*3.5, 0)
                bpy.ops.object.text_add(location=(column*3.1-1, -row*3.5-1.5, .01))
                text = bpy.context.object
                text.data.body = f'{style} / {identifier}'
                text.data.size = .14
                text.data.materials.append(ink)
        bpy.ops.mesh.primitive_plane_add(size=200, location=(0,0,-.03))
        mat = bpy.data.materials.new('Ground')
        mat.use_nodes = True
        mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (.23,.26,.27,1)
        mat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = .85
        bpy.context.object.data.materials.append(mat)
        target = (4.65, -5.2, .3)
        commercial.camera_scene((5.1,-18,17), target, 18, (1800,1550))
        reflection_lights(target, True)
        configure_render()
        bpy.context.scene.render.filepath = str(OUT / 'Previews' / f'Compare_{group}.png')
        bpy.ops.render.render(write_still=True)
        print('COMPARISON', group, flush=True)

def validate():
    records = json.loads((OUT / 'QA/catalog.json').read_text(encoding='utf8'))
    assert len(records) == 180
    checks = []
    for record in records:
        bpy.ops.wm.open_mainfile(filepath=str(OUT/record['blend']), load_ui=False, use_scripts=False)
        collection = bpy.data.collections[record['id']+'_ASSET']
        assert h.material_geometry_signature(collection) == record['geometry_signature'], record['blend']
        for obj in collection.objects:
            if obj.type != 'MESH':
                continue
            mat = obj.data.materials[0]
            assert mat.get('Style') == record['style']
            shader = mat.node_tree.nodes['Principled BSDF']
            if obj.get('ImageSurface'):
                assert tuple(shader.inputs['Base Color'].default_value) == (1,1,1,1)
                assert obj.data.uv_layers.active.name == 'ImageUV'
            if mat['SurfaceFinish']=='PolishedBlackMetal':
                assert shader.inputs['Metallic'].default_value > .9
                assert shader.inputs['Roughness'].default_value < .21
            for node in mat.node_tree.nodes:
                if node.type == 'TEX_IMAGE':
                    assert node.image and node.image.packed_file, record['blend']
        checks.append(record['blend'])
    baseline = json.loads((OUT/'QA/SourceBaseline.json').read_text(encoding='utf8'))
    changed = [path for path,digest in baseline.items() if hashlib.sha256((BASE/path).read_bytes()).hexdigest()!=digest]
    assert not changed, changed
    (OUT/'QA/validation.json').write_text(json.dumps(dict(validated_files=len(checks),
        geometry_uv_transforms_preserved=True, source_files_checked=len(baseline), source_files_changed=changed,
        packed_textures=True, white_image_surfaces=True, glossy_black_shader_checked=True), indent=2), encoding='utf8')
    print('VALIDATED', len(checks), flush=True)

if __name__ == '__main__':
    try:
        preferences = bpy.context.preferences.addons['cycles'].preferences
        preferences.compute_device_type = 'OPTIX'
        preferences.get_devices()
        for device in preferences.devices:
            device.use = device.type == 'OPTIX'
        GPU = any(device.type == 'OPTIX' for device in preferences.devices)
    except Exception as error:
        print('GPU_UNAVAILABLE', str(error), flush=True)
    print('RENDER_DEVICE', 'OPTIX' if GPU else 'CPU', flush=True)
    if '--render-only' not in sys.argv and '--validate-only' not in sys.argv:
        build_variants()
    if '--validate-only' not in sys.argv:
        showrooms()
        comparisons()
    validate()
