"""Blender authoring recipe; run in a fresh background process, never a user's open scene.
Uses the same metre / isolated collection / separate-screen / FBX workflow as RetailPOS.
"""
import bpy
import math
import json
import sys
from pathlib import Path
from mathutils import Vector

HERE = Path(__file__).resolve().parent
EXPORT = HERE / 'Exports'
QA = HERE / 'QA'
for folder in (EXPORT / 'Textures', QA):
    folder.mkdir(parents=True, exist_ok=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1
scene.render.engine = 'CYCLES'
scene.cycles.samples = 32
scene.cycles.use_denoising = True
model = bpy.data.collections.new('LibraryKiosk_ASSET')
studio = bpy.data.collections.new('Presentation_ONLY')
scene.collection.children.link(model)
scene.collection.children.link(studio)

def into(obj, collection=model):
    for old in list(obj.users_collection):
        old.objects.unlink(obj)
    collection.objects.link(obj)
    return obj

def group(name, parent=None):
    obj = bpy.data.objects.new(name, None)
    model.objects.link(obj)
    obj.parent = parent
    return obj

root = group('LibraryKiosk')
root['units'] = 'metres'
root['FurnitureFunction'] = 'Display'
root['Front'] = 'Blender -Y; use FrontAnchor on import'
root['Screen'] = 'Screen_Main: independent full 0..1 image UV'
front = group('FrontAnchor', root)
front.location = (0, -1, 0)
body = group('Cabinet', root)
back = group('RearServiceDetails', root)
console = group('ControlConsole', root)
console.location = (0, -.230, 1.006)
console.rotation_euler.x = math.radians(14)
monitor = group('DisplayHousing', root)
monitor.location = (0, -.022, 1.375)
monitor.rotation_euler.x = math.radians(-14)

def material(name, color, metallic=0, rough=.4):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    shader = mat.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value = (*color, 1)
    shader.inputs['Metallic'].default_value = metallic
    shader.inputs['Roughness'].default_value = rough
    mat.diffuse_color = (*color, 1)
    return mat

cream = material('Kiosk_CreamEnamel', (.76, .733, .679), .12, .31)
oak = material('Kiosk_NaturalOak', (.57, .365, .19), 0, .44)
graphite = material('Kiosk_Graphite', (.052, .057, .064), .38, .32)
rubber = material('Kiosk_Rubber', (.012, .014, .018), 0, .62)
keys = material('Kiosk_Keycaps', (.035, .040, .046), .08, .39)
steel = material('Kiosk_Steel', (.29, .31, .33), .80, .29)
legend = material('Kiosk_Legend', (.73, .75, .74), .05, .5)
glass = material('Kiosk_Screen', (.007, .009, .012), .08, .23)

# A real texture, exported with the FBX and packed in the source .blend.
# Grain is generated in Blender image data so Unity does not depend on procedural nodes.
width, height = 256, 1024
pixels = []
for y in range(height):
    v = y / height
    for x in range(width):
        u = x / width
        wave = u + .0045 * math.sin(v * 12 + u * 15) + .002 * math.sin(v * 37)
        grain = .030 * math.sin(wave * 850) + .025 * math.sin(wave * 245 + math.sin(v * 8))
        grain += .018 * math.sin(wave * 1950 + v * 20) + .014 * math.sin(wave * 79)
        pore = max(0, math.sin(wave * 2600 + .7 * math.sin(v * 31))) ** 28 * .025
        pixels.extend((.69 + grain - pore, .517 + grain * .78 - pore, .339 + grain * .56 - pore, 1))
wood_image = bpy.data.images.new('Kiosk_OakGrain', width, height, alpha=False)
wood_image.pixels.foreach_set(pixels)
wood_image.filepath_raw = str(EXPORT / 'Textures/Kiosk_OakGrain.png')
wood_image.file_format = 'PNG'
wood_image.save()
tex = oak.node_tree.nodes.new('ShaderNodeTexImage')
tex.image = wood_image
oak.node_tree.links.new(tex.outputs['Color'], oak.node_tree.nodes['Principled BSDF'].inputs['Base Color'])

def finish(obj, mat, parent, bevel=0, segments=3):
    into(obj)
    obj.data.materials.append(mat)
    if bevel:
        modifier = obj.modifiers.new('Manufactured edge radius', 'BEVEL')
        modifier.width = bevel
        modifier.segments = segments
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        for polygon in obj.data.polygons:
            polygon.use_smooth = True
        normal = obj.modifiers.new('Weighted corner normals', 'WEIGHTED_NORMAL')
        normal.keep_sharp = True
        bpy.ops.object.modifier_apply(modifier=normal.name)
    obj.parent = parent
    return obj

def cube(name, loc, size, mat, parent=body, bevel=.004, segments=3):
    bpy.ops.mesh.primitive_cube_add(size=1)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    finish(obj, mat, parent, bevel, segments)
    obj.location = loc
    return obj

def cylinder(name, loc, radius, depth, mat, parent=back, axis='Y', vertices=12):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth)
    obj = bpy.context.object
    obj.name = name
    finish(obj, mat, parent)
    obj.location = loc
    if axis == 'Y':
        obj.rotation_euler.x = math.pi / 2
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    return obj

def profile(name, center_x, thickness, points, mat, parent=body):
    n = len(points)
    verts = [(x, y, z) for x in (center_x - thickness / 2, center_x + thickness / 2) for y, z in points]
    faces = [tuple(range(n - 1, -1, -1)), tuple(range(n, 2 * n))]
    faces += [(i, (i + 1) % n, (i + 1) % n + n, i + n) for i in range(n)]
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    model.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    finish(obj, mat, parent, .008, 3)
    return obj

def stroke(name, coords, radius, mat, parent):
    curve = bpy.data.curves.new(name, 'CURVE')
    curve.dimensions = '3D'
    curve.bevel_depth = radius
    curve.bevel_resolution = 1
    line = curve.splines.new('POLY')
    line.points.add(len(coords) - 1)
    for point, co in zip(line.points, coords):
        point.co = (*co, 1)
    obj = bpy.data.objects.new(name, curve)
    model.objects.link(obj)
    obj.parent = parent
    curve.materials.append(mat)
    return obj

# Plinth, cabinet and continuous oak side cheeks follow the supplied side profile.
cube('WeightedPlinth', (0, -.07, .042), (.760, .600, .080), graphite, bevel=.025, segments=4)
cube('RubberPlinthFoot', (0, -.07, .009), (.706, .545, .016), rubber, bevel=.010)
outline = [(-.130, .079), (.167, .079), (.167, 1.650), (.115, 1.716),
           (.075, 1.744), (-.107, 1.054), (-.378, .990), (-.378, .958), (-.130, .958)]
profile('CreamCabinetShell', 0, .570, outline, cream)
for x in (-.304, .304):
    profile('SolidOakSide', x, .030, outline, oak)
cube('FrontServicePanel', (0, -.141, .518), (.566, .025, .868), cream, bevel=.009)
cube('OakBookShelf', (0, -.259, .637), (.632, .294, .037), oak, bevel=.012, segments=4)
cube('ShelfUnderside', (0, -.207, .612), (.535, .161, .014), cream, bevel=.004)
cube('ReceiptBezel', (.172, -.157, .849), (.126, .012, .058), graphite, bevel=.004)
cube('ReceiptAperture', (.172, -.164, .853), (.105, .003, .032), rubber, bevel=.002)
cube('ReceiptLowerLip', (.172, -.168, .835), (.108, .006, .008), steel, bevel=.002)

# A tilted portrait screen with a dedicated renderer and full rectangular image UV.
cube('MonitorBack', (0, .005, 0), (.572, .075, .716), cream, monitor, .018, 5)
cube('ScreenSeal', (0, -.038, 0), (.543, .009, .684), rubber, monitor, .012, 4)
screen_mesh = bpy.data.meshes.new('Screen_Main')
screen_mesh.from_pydata([(-.255, -.044, -.323), (.255, -.044, -.323),
                        (.255, -.044, .323), (-.255, -.044, .323)], [], [(0, 1, 2, 3)])
screen_mesh.update()
uv = screen_mesh.uv_layers.new(name='DisplayUV')
for index, coord in enumerate(((0, 0), (1, 0), (1, 1), (0, 1))):
    uv.data[index].uv = coord
screen = bpy.data.objects.new('Screen_Main', screen_mesh)
model.objects.link(screen)
screen.parent = monitor
screen_mesh.materials.append(glass)
screen['Purpose'] = 'Replace material base map with kiosk screen image'

# Keyboard and RFID reader sit on the same inclined console, as in the reference.
cube('OakConsoleRim', (0, 0, -.011), (.632, .293, .031), oak, console, .014, 4)
cube('CreamConsoleTop', (0, -.002, .005), (.605, .273, .020), cream, console, .010, 4)
cube('KeyboardRecess', (-.091, .009, .018), (.369, .190, .009), graphite, console, .007)
keyboard = group('Keyboard', console)
keyboard.location = (-.091, .009, .023)
row_strings = ['1234567890-=', 'QWERTYUIOP[]', 'ASDFGHJKL;', 'ZXCVBNM,./']
for row, letters in enumerate(row_strings):
    y = .063 - row * .030
    offset = (-.159, -.151, -.137, -.121)[row]
    for col, letter in enumerate(letters):
        x = offset + col * .027
        cube('Keycap', (x, y, 0), (.023, .025, .008), keys, keyboard, .002, 2)
        curve = bpy.data.curves.new('KeyLegend', 'FONT')
        curve.body = letter
        curve.size = .008
        curve.align_x = 'CENTER'
        curve.align_y = 'CENTER'
        curve.resolution_u = 1
        obj = bpy.data.objects.new('KeyLegend', curve)
        model.objects.link(obj)
        obj.parent = keyboard
        obj.location = (x, y, .0043)
        curve.materials.append(legend)
cube('Spacebar', (-.007, -.064, 0), (.157, .024, .008), keys, keyboard, .002, 2)
for x in (-.158, -.128, .099, .129, .158):
    cube('ModifierKey', (x, -.064, 0), (.024, .024, .008), keys, keyboard, .002, 2)
cube('EnterKey', (.154, -.009, 0), (.032, .052, .008), keys, keyboard, .003, 2)
cube('RFIDReaderPad', (.224, .004, .019), (.111, .177, .010), graphite, console, .008)
glyph = group('ReaderGlyph', console)
glyph.location = (.224, -.026, .0247)
stroke('OpenBookLeft', [(0, -.014, 0), (-.029, -.006, 0), (-.024, .030, 0), (0, .021, 0), (0, -.014, 0)], .0016, legend, glyph)
stroke('OpenBookRight', [(0, -.014, 0), (.029, -.006, 0), (.024, .030, 0), (0, .021, 0)], .0016, legend, glyph)
for radius in (.012, .021, .030):
    points = [(radius * math.cos(a), .039 + radius * math.sin(a), 0)
              for a in [math.radians(32 + i * 116 / 12) for i in range(13)]]
    stroke('RFIDWave', points, .0015, legend, glyph)

# Rear service cover, hinges, inset ports and ventilation details.
cube('RearDoorSeam', (0, .170, .555), (.504, .006, .710), graphite, back, .007)
cube('RearServiceDoor', (0, .176, .555), (.494, .008, .700), cream, back, .006)
cube('DoorPullRecess', (0, .177, .926), (.095, .008, .020), rubber, back, .004)
cube('DoorPull', (0, .183, .921), (.077, .006, .009), graphite, back, .002)
for z in (.298, .805):
    cube('DoorHinge', (.251, .180, z), (.010, .013, .047), steel, back, .002)
for x in (-.230, .230):
    for z in (.125, 1.582):
        cylinder('RearFastener', (x, .173, z), .004, .003, steel, vertices=12)
for side in (-1, 1):
    for col in range(4):
        for row in range(6):
            cylinder('UpperVent', (side * .189 + (col - 1.5) * .012, .169, 1.516 - row * .014), .0025, .003, rubber)
for col in range(10):
    for row in range(3):
        cylinder('LowerVent', (-.190 + col * .013, .171, .163 + row * .012), .0026, .003, rubber)
cube('RearSocketPlate', (.195, .174, .178), (.099, .006, .050), steel, back, .002)
for x in (.174, .213):
    cylinder('PowerAndDataPort', (x, .179, .178), .012, .004, rubber, vertices=20)

# Wood UVs preserve vertical grain on tall cheeks and run across the shelf.
bpy.context.view_layer.update()
for obj in list(model.objects):
    if obj.type in {'CURVE', 'FONT'}:
        bpy.ops.object.select_all(action='DESELECT')
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.convert(target='MESH')
    if obj.type != 'MESH' or obj == screen:
        continue
    if oak in list(obj.data.materials):
        layer = obj.data.uv_layers.active or obj.data.uv_layers.new(name='WoodGrainUV')
        tall = 'Side' in obj.name
        for polygon in obj.data.polygons:
            for loop_index in polygon.loop_indices:
                co = obj.data.vertices[obj.data.loops[loop_index].vertex_index].co
                layer.data[loop_index].uv = ((co.y * 2 + .5, co.z / 1.75) if tall else (co.y * 2 + .5, co.x * 2 + .5))
    elif not obj.data.uv_layers:
        obj.data.uv_layers.new(name='UVMap')

# Keep a handful of manufactured assemblies and the independent screen, not hundreds of renderers.
for parent in (keyboard, glyph, console, monitor, body, back):
    parts = [obj for obj in model.objects if obj.type == 'MESH' and obj.parent == parent and obj != screen]
    if len(parts) > 1:
        bpy.ops.object.select_all(action='DESELECT')
        for obj in parts:
            obj.select_set(True)
        bpy.context.view_layer.objects.active = parts[0]
        bpy.ops.object.join()
        parts[0].name = parent.name + '_Mesh'

bpy.context.view_layer.update()
meshes = [obj for obj in model.objects if obj.type == 'MESH']
verts = [obj.matrix_world @ Vector(corner) for obj in meshes for corner in obj.bound_box]
minimum = [min(v[i] for v in verts) for i in range(3)]
maximum = [max(v[i] for v in verts) for i in range(3)]
triangles = sum(sum(len(face.vertices) - 2 for face in obj.data.polygons) for obj in meshes)
manifest = {'name': 'LibraryKiosk', 'units': 'metres', 'triangles': triangles, 'meshes': len(meshes),
            'dimensionsBlenderXYZ': [maximum[i] - minimum[i] for i in range(3)],
            'boundsMin': minimum, 'boundsMax': maximum, 'frontAnchor': 'FrontAnchor',
            'screenObject': 'Screen_Main', 'screenUV': 'full 0..1 rectangle',
            'materials': [{'name': mat.name, 'color': list(mat.diffuse_color),
                           'metallic': mat.node_tree.nodes['Principled BSDF'].inputs['Metallic'].default_value,
                           'roughness': mat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value,
                           'texture': 'Kiosk_OakGrain.png' if mat == oak else None}
                          for mat in (cream, oak, graphite, rubber, keys, steel, legend, glass)]}
(EXPORT / 'LibraryKiosk.json').write_text(json.dumps(manifest, indent=2), encoding='utf8')
bpy.ops.object.select_all(action='DESELECT')
for obj in model.objects:
    obj.select_set(True)
bpy.context.view_layer.objects.active = root
bpy.ops.export_scene.fbx(filepath=str(EXPORT / 'LibraryKiosk.fbx'), use_selection=True,
                        object_types={'MESH', 'EMPTY'}, axis_forward='-Z', axis_up='Y',
                        use_custom_props=True, apply_unit_scale=True, add_leaf_bones=False,
                        bake_anim=False, path_mode='RELATIVE')

floor = cube('Studio_floor', (0, 0, -.016), (200, 200, .03), material('Studio_Grey', (.60, .60, .59), 0, .8), None, 0)
into(floor, studio)
def area(name, location, energy, size):
    data = bpy.data.lights.new(name, 'AREA')
    data.energy = energy
    data.shape = 'DISK'
    data.size = size
    obj = bpy.data.objects.new(name, data)
    studio.objects.link(obj)
    obj.location = location
    obj.rotation_euler = (Vector((0, 0, .85)) - obj.location).to_track_quat('-Z', 'Y').to_euler()
area('Softbox_Key', (-2.5, -3.2, 4.0), 430, 3)
area('Softbox_Fill', (3.0, -1, 2.5), 240, 2.5)
area('Softbox_Rim', (1, 2.2, 3.6), 440, 2)
scene.world.color = (.35, .35, .35)
camera_data = bpy.data.cameras.new('PresentationCamera')
camera = bpy.data.objects.new('PresentationCamera', camera_data)
studio.objects.link(camera)
scene.camera = camera
camera_data.type = 'ORTHO'
scene.render.resolution_x = 1000
scene.render.resolution_y = 1100
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
scene.view_settings.view_transform = 'AgX'
def render(name, position, target=(0, -.06, .86), scale=2.02):
    camera.location = position
    camera.rotation_euler = (Vector(target) - camera.location).to_track_quat('-Z', 'Y').to_euler()
    camera_data.ortho_scale = scale
    scene.render.filepath = str(QA / name)
    bpy.ops.render.render(write_still=True)
if '--skip-render' not in sys.argv:
    render('LibraryKiosk_Hero.png', (2.7, -4.2, 2.65))
    render('LibraryKiosk_Rear.png', (-2.6, 4.2, 2.35))
camera.location = (2.7, -4.2, 2.65)
camera.rotation_euler = (Vector((0, -.06, .86)) - camera.location).to_track_quat('-Z', 'Y').to_euler()
wood_image.pack()
wood_image.filepath = '//Exports/Textures/Kiosk_OakGrain.png'
bpy.ops.object.select_all(action='DESELECT')
root.select_set(True)
bpy.context.view_layer.objects.active = root
bpy.ops.wm.save_as_mainfile(filepath=str(HERE / 'LibraryKiosk.blend'))
print('LIBRARY_KIOSK_COMPLETE', json.dumps(manifest), flush=True)
