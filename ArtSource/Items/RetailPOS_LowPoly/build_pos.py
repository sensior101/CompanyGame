"""Low-poly convenience store POS. Blender 5.x, metres, Z up.
Run: blender -b --factory-startup --python build_pos.py
The operator faces -Y, the customer +Y. Screen_Main and Screen_Customer are separate flat quads with full 0-1 UVs,
so a RenderTexture or UI image can later replace their black material without touching the rest of the model.
"""
import bpy, math
from pathlib import Path

HERE = Path(__file__).resolve().parent
OUT = HERE.parents[2] / 'CompanyGame/Assets/Art/Items/RetailPOS_LowPoly'
OUT.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)

def material(name, hex_color, rough=.8):
    m = bpy.data.materials.new(name); m.use_nodes = True
    rgb = tuple(int(hex_color[i:i + 2], 16) / 255 for i in (0, 2, 4))
    bsdf = m.node_tree.nodes['Principled BSDF']
    bsdf.inputs['Base Color'].default_value = (*rgb, 1); bsdf.inputs['Roughness'].default_value = rough
    m.diffuse_color = (*rgb, 1)
    return m

BODY = material('LowPOS_Body', '2b2e33'); TRIM = material('LowPOS_Trim', '4a4f57')
KEY = material('LowPOS_Key', 'c9ccd1'); SCREEN = material('LowPOS_Screen', '000000', .4)
ROOT = bpy.data.objects.new('LowPolyPOS', None); bpy.context.scene.collection.objects.link(ROOT)

def node(name, loc=(0, 0, 0), rot=(0, 0, 0), parent=ROOT):
    o = bpy.data.objects.new(name, None); bpy.context.scene.collection.objects.link(o)
    o.parent = parent; o.location = loc; o.rotation_euler = rot
    return o

def box(name, size, loc, mat, parent=ROOT, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, 0))
    o = bpy.context.active_object; o.name = name; o.scale = size
    bpy.ops.object.transform_apply(scale=True)
    o.data.materials.append(mat); o.parent = parent; o.location = loc; o.rotation_euler = rot
    return o

def screen(name, w, h, parent, y):
    # Quad in the XZ plane facing -Y of its parent, UV 0-1 left-to-right, bottom-to-top.
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([(-w/2, y, -h/2), (w/2, y, -h/2), (w/2, y, h/2), (-w/2, y, h/2)], [], [(0, 1, 2, 3)])
    uv = mesh.uv_layers.new(name='UVMap')
    for loop, coord in zip(mesh.polygons[0].loop_indices, [(0, 0), (1, 0), (1, 1), (0, 1)]): uv.data[loop].uv = coord
    mesh.materials.append(SCREEN)
    o = bpy.data.objects.new(name, mesh); bpy.context.scene.collection.objects.link(o); o.parent = parent
    return o

# Cash drawer base.
box('CashDrawer', (.42, .40, .10), (0, 0, .05), BODY)
box('DrawerFront', (.38, .01, .05), (0, -.205, .05), TRIM)
# Main monitor on a stand, tilted back 15 degrees, screen 16:10 (0.34 x 0.2125 m).
box('MonitorStand', (.06, .05, .16), (0, .06, .18), TRIM)
monitor = node('MainMonitor', (0, .03, .36), (math.radians(-15), 0, 0))
box('MonitorBody', (.38, .04, .25), (0, 0, 0), BODY, monitor)
screen('Screen_Main', .34, .2125, monitor, -.0205)
# Customer display on a post at the back right, screen facing the customer (+Y), 16:9 (0.16 x 0.09 m).
box('CustomerPost', (.03, .03, .26), (.15, .17, .23), TRIM)
customer = node('CustomerDisplayHead', (.15, .17, .40), (0, 0, math.pi))
box('CustomerBody', (.19, .03, .12), (0, 0, 0), BODY, customer)
screen('Screen_Customer', .16, .09, customer, -.0155)
# Card terminal and keypad at the front right, barcode scanner at the front left.
box('CardTerminal', (.09, .14, .04), (.27, -.08, .02), BODY)
for r in range(3):
    for c in range(3): box(f'Key_{r}{c}', (.018, .018, .006), (.245 + c * .025, -.115 + r * .025, .043), KEY)
box('ScannerBase', (.08, .08, .03), (-.27, -.08, .015), BODY)
box('ScannerHandle', (.035, .035, .13), (-.27, -.08, .09), TRIM, rot=(math.radians(15), 0, 0))
box('ScannerHead', (.07, .05, .04), (-.27, -.065, .16), BODY)

bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=str(OUT / 'LowPolyPOS.fbx'), use_selection=True, object_types={'EMPTY', 'MESH'},
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', add_leaf_bones=False, bake_anim=False)

# Preview render from the customer's front-right, three-quarter.
cam = bpy.data.objects.new('Cam', bpy.data.cameras.new('Cam')); bpy.context.scene.collection.objects.link(cam)
cam.location = (.9, .8, .7); target = (0, 0, .22)
import mathutils
cam.rotation_euler = (mathutils.Vector(target) - cam.location).to_track_quat('-Z', 'Y').to_euler()
bpy.context.scene.camera = cam
sun = bpy.data.objects.new('Sun', bpy.data.lights.new('Sun', 'SUN')); sun.data.energy = 3; sun.rotation_euler = (.8, .2, .6)
bpy.context.scene.collection.objects.link(sun)
w = bpy.data.worlds.new('W'); w.use_nodes = True; w.node_tree.nodes['Background'].inputs[0].default_value = (.6, .62, .65, 1)
bpy.context.scene.world = w
s = bpy.context.scene; s.render.engine = 'BLENDER_EEVEE'; s.render.resolution_x = 800; s.render.resolution_y = 600
s.render.filepath = str(HERE / 'preview_customer.png'); bpy.ops.render.render(write_still=True)
cam.location = (-.6, -1.0, .75); cam.rotation_euler = (mathutils.Vector(target) - cam.location).to_track_quat('-Z', 'Y').to_euler()
s.render.filepath = str(HERE / 'preview_operator.png'); bpy.ops.render.render(write_still=True)
bpy.ops.wm.save_as_mainfile(filepath=str(HERE / 'LowPolyPOS.blend'))
print('POS_DONE', sum(len(o.data.polygons) for o in bpy.data.objects if o.type == 'MESH'))
