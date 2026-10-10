"""Render the four office .blend sources together in native Blender.

The comparison appends asset objects into a new presentation scene. Source
files remain untouched. Geometry, materials and metre scale are preserved;
only each root's display location and common rotation change in this scene.

Run with Blender --background --factory-startup --python this_file.py.
Optional -- --skip-render saves the comparison scene without rendering.
"""

import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector


HERE = Path(__file__).resolve().parent
BUILDINGS = HERE.parent
ASSETS = [
    ('A', '02_SlimGlass', 'Office02_SlimGlass', -180.0, 148.0, 'SLIM GLASS'),
    ('B', '03_UrbanTerrace', 'Office03_UrbanTerrace', -60.0, 144.0, 'URBAN TERRACE'),
    ('C', '04_MinimalDark', 'Office04_MinimalDark', 60.0, 146.0, 'MINIMAL DARK'),
    ('D', '05_ClassicStone', 'Office05_ClassicStone', 180.0, 148.0, 'CLASSIC STONE'),
]


def presentation_material(name, color, emission=False):
    material = bpy.data.materials.new(name)
    material.diffuse_color = (*color, 1)
    material.use_nodes = True
    tree = material.node_tree
    shader = tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value = (*color, 1)
    shader.inputs['Roughness'].default_value = .78
    if emission:
        tree.nodes.remove(shader)
        shader = tree.nodes.new('ShaderNodeEmission')
        shader.inputs['Color'].default_value = (*color, 1)
        shader.inputs['Strength'].default_value = 1.0
        tree.links.new(shader.outputs[0], tree.nodes.get('Material Output').inputs['Surface'])
    return material


def add_area(name, location, target, power, size, color):
    data = bpy.data.lights.new(name, 'AREA')
    data.energy = power
    data.shape = 'DISK'
    data.size = size
    data.color = color
    obj = bpy.data.objects.new(name, data)
    presentation.objects.link(obj)
    obj.location = location
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat('-Z', 'Y').to_euler()
    return obj


def add_caption(name, body, x, y, size, align='CENTER', material=None):
    """Camera-facing native text provides labels without image composition."""
    data = bpy.data.curves.new(name, 'FONT')
    data.body = body
    data.align_x = align
    data.align_y = 'CENTER'
    data.size = size
    data.space_character = 1.05
    if label_font:
        data.font = label_font
    obj = bpy.data.objects.new(name, data)
    presentation.objects.link(obj)
    obj.parent = camera
    obj.location = (x, y, -15.0)
    obj.rotation_euler = (0, 0, 0)
    data.materials.append(material or ink)
    # Keep graphic captions out of illumination/reflection while rendering them
    # as real text objects in the camera's view.
    obj.visible_shadow = False
    obj.visible_glossy = False
    obj.visible_diffuse = False
    return obj


# Fail before changing the in-memory scene when a source has not been built yet.
source_paths = [BUILDINGS / folder / (root_name + '_Blockout.blend')
                for _, folder, root_name, _, _, _ in ASSETS]
missing = [str(path) for path in source_paths if not path.is_file()]
if missing:
    raise FileNotFoundError('Build the four source models first: ' + ', '.join(missing))

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version = 0
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1.0
scene.unit_settings.length_unit = 'METERS'
presentation = bpy.data.collections.new('_Presentation')
scene.collection.children.link(presentation)

statistics = []
for spec, source_path in zip(ASSETS, source_paths):
    letter, folder, root_name, offset_x, expected_height, title = spec
    collection = bpy.data.collections.new(letter + '_' + title.replace(' ', '_'))
    scene.collection.children.link(collection)
    with bpy.data.libraries.load(str(source_path), link=False) as (available, requested):
        names = [name for name in available.objects
                 if name == root_name or name.startswith(letter + '_')]
        if root_name not in names:
            raise RuntimeError('Expected source root is missing: ' + root_name)
        requested.objects = names
    objects = [obj for obj in requested.objects if obj is not None]
    for obj in objects:
        if obj.type not in {'MESH', 'EMPTY'}:
            raise RuntimeError('Unexpected presentation object in asset append: ' + obj.name)
        collection.objects.link(obj)
    root = next(obj for obj in objects if obj.name == root_name)
    if root.parent is not None:
        raise RuntimeError('Asset root unexpectedly has a parent: ' + root_name)
    root.location = (offset_x, 0, 0)
    root.rotation_euler = (0, 0, math.radians(-25.0))
    root['comparison_source'] = str(source_path)
    root['comparison_same_scale'] = True
    bpy.context.view_layer.update()
    meshes = [obj for obj in objects if obj.type == 'MESH']
    if not meshes:
        raise RuntimeError('No model meshes found for ' + root_name)
    for obj in meshes:
        ancestor = obj.parent
        while ancestor is not None and ancestor != root:
            ancestor = ancestor.parent
        if ancestor != root:
            raise RuntimeError('Mesh is not attached to the asset root: ' + obj.name)
    points = [obj.matrix_world @ Vector(corner) for obj in meshes for corner in obj.bound_box]
    minimum = [min(point[i] for point in points) for i in range(3)]
    maximum = [max(point[i] for point in points) for i in range(3)]
    actual_height = maximum[2] - minimum[2]
    if abs(actual_height - expected_height) > .1:
        raise RuntimeError(f'{root_name}: unexpected height {actual_height:.4f}, expected {expected_height}')
    statistics.append({
        'type': letter,
        'source': str(source_path),
        'root': root_name,
        'meshes': len(meshes),
        'height_m': round(actual_height, 4),
        'comparison_bounds_min': [round(value, 4) for value in minimum],
        'comparison_bounds_max': [round(value, 4) for value in maximum],
    })

# Matte neutral studio ground. A large plane keeps a horizon line out of frame.
ground_material = presentation_material('Comparison_NeutralGround', (.70, .73, .76))
mesh = bpy.data.meshes.new('Comparison_StudioGround_Mesh')
mesh.from_pydata([(-1800, -1800, -.06), (1800, -1800, -.06),
                 (1800, 1800, -.06), (-1800, 1800, -.06)], [], [(0, 1, 2, 3)])
mesh.update()
ground = bpy.data.objects.new('_ComparisonStudioGround', mesh)
presentation.objects.link(ground)
ground.data.materials.append(ground_material)

world = bpy.data.worlds.new('Comparison_NeutralStudioWorld')
world.use_nodes = True
world.node_tree.nodes['Background'].inputs['Color'].default_value = (.64, .70, .78, 1)
world.node_tree.nodes['Background'].inputs['Strength'].default_value = .65
scene.world = world
add_area('_ComparisonKey', (-180, -250, 340), (0, 0, 72), 1800000, 320, (1, .96, .91))
add_area('_ComparisonFill', (220, -160, 230), (0, 0, 72), 1250000, 300, (.88, .94, 1))
add_area('_ComparisonRim', (0, 170, 300), (0, 0, 80), 1900000, 320, (1, 1, 1))
sun_data = bpy.data.lights.new('_ComparisonSun', 'SUN')
sun_data.energy = 1.6
sun_data.angle = .16
sun = bpy.data.objects.new('_ComparisonSun', sun_data)
presentation.objects.link(sun)
sun.rotation_euler = (.42, -.33, -.5)

camera_data = bpy.data.cameras.new('ComparisonCamera')
camera_data.type = 'ORTHO'
camera_data.ortho_scale = 520.0
camera_data.clip_start = .1
camera_data.clip_end = 4000
camera = bpy.data.objects.new('ComparisonCamera', camera_data)
presentation.objects.link(camera)
camera.location = (0, -660, 230)
camera.rotation_euler = (Vector((0, 0, 74)) - camera.location).to_track_quat('-Z', 'Y').to_euler()
scene.camera = camera

ink = presentation_material('Comparison_LabelInk', (.038, .055, .075), emission=True)
secondary_ink = presentation_material('Comparison_SecondaryInk', (.14, .18, .22), emission=True)
label_font = None
for font_path in [Path('C:/Windows/Fonts/arialbd.ttf'), Path('C:/Windows/Fonts/arial.ttf')]:
    if font_path.is_file():
        label_font = bpy.data.fonts.load(str(font_path))
        try:
            label_font.pack()
        except Exception:
            pass
        break
add_caption('_ComparisonTitle', 'COMPANYGAME  /  OFFICE SERIES', -242, 100, 6.2, align='LEFT')
add_caption('_ComparisonScaleNote', 'SAME SCALE  -  FIRST PROPORTIONAL MODELS', 242, 100, 3.3,
            align='RIGHT', material=secondary_ink)
for letter, folder, root_name, x, height, title in ASSETS:
    add_caption('_ComparisonLabel_' + letter, letter + '  ' + title, x, -92, 5.4)
    add_caption('_ComparisonHeight_' + letter, f'{height:.0f} m', x, -102, 3.6,
                material=secondary_ink)

scene.render.engine = 'CYCLES'
scene.cycles.samples = 48
scene.cycles.use_denoising = True
scene.cycles.max_bounces = 6
scene.cycles.diffuse_bounces = 2
scene.cycles.glossy_bounces = 3
scene.cycles.device = 'CPU'
try:
    cycles_preferences = bpy.context.preferences.addons['cycles'].preferences
    cycles_preferences.compute_device_type = 'OPTIX'
    cycles_preferences.get_devices()
    available_gpu = [device for device in cycles_preferences.devices if device.type == 'OPTIX']
    if available_gpu:
        for device in cycles_preferences.devices:
            device.use = device.type == 'OPTIX'
        scene.cycles.device = 'GPU'
except Exception:
    pass
scene.render.resolution_x = 4200
scene.render.resolution_y = 1800
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGB'
scene.render.film_transparent = False
scene.view_settings.view_transform = 'AgX'
scene.view_settings.exposure = .35
scene.render.filepath = str(HERE / 'Comparison_ABCD.png')
scene['comparison_units'] = 'metres'
scene['source_files_unchanged'] = True
scene['common_rotation_degrees'] = -25.0
scene['orthographic_width_m'] = 520.0

HERE.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=str(HERE / 'Comparison.blend'))
if '--skip-render' not in sys.argv:
    bpy.ops.render.render(write_still=True)
(HERE / 'comparison_result.json').write_text(json.dumps({
    'render': 'Comparison_ABCD.png',
    'scene': 'Comparison.blend',
    'resolution_px': [4200, 1800],
    'orthographic_width_m': 520.0,
    'common_rotation_degrees': -25.0,
    'assets': statistics,
}, ensure_ascii=False, indent=2), encoding='utf-8')
print('OFFICE_COMPARISON_COMPLETE ' + json.dumps(statistics, ensure_ascii=False))
