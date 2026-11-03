"""Render transparent inventory icons for the library foods with the same camera as Renders/.
Run: Blender --background --factory-startup --addons cycles --python render_icons.py -- <raw out dir> [ids...]
With no ids, every non-convenience menu item is rendered (the store's ten keep their own icons).
Each <Id>.png is 512 px with a transparent background; crop_icons.py trims and scales them for Unity."""
import bpy, sys, json, importlib.util
from pathlib import Path

ROOT = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('food_builder', ROOT / 'build_crafted.py')
builder = importlib.util.module_from_spec(spec); spec.loader.exec_module(builder)
args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
out = Path(args[0]); out.mkdir(parents=True, exist_ok=True)
menu = json.loads((ROOT / 'menu.json').read_text(encoding='utf8'))
ids = args[1:] or [m['id'] for m in menu if m['category'] != 'Convenience']

bpy.ops.wm.open_mainfile(filepath=str(ROOT / 'CraftedFood_134.blend'))
for obj in list(bpy.data.collections['Studio'].objects): bpy.data.objects.remove(obj, do_unlink=True)
bpy.data.collections.remove(bpy.data.collections['Studio']); builder.setup_studio()
scene = bpy.context.scene
scene.render.film_transparent = True
# The floor still bounces light onto the food but is never seen, so the background stays empty.
bpy.data.objects['StudioFloor'].visible_camera = False
foods = [o for o in bpy.data.objects if o.name.startswith('Food_')]
for o in foods: o.hide_render = True
for i, food_id in enumerate(ids):
    obj = bpy.data.objects['Food_' + food_id]
    home = obj.location.copy(); obj.location = (0, 0, 0); obj.hide_render = False
    builder.render_item(obj, out / f'{food_id}.png')
    obj.hide_render = True; obj.location = home
    print(f'ICON {i + 1}/{len(ids)} {food_id}', flush=True)
print('ICONS_DONE', len(ids), flush=True)
