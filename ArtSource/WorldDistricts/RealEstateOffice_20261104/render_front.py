"""Refresh only the front PNG; asset/export files are never saved or modified."""
import bpy, ast, sys
from pathlib import Path
from mathutils import Vector
sys.dont_write_bytecode=True
OUT=Path(__file__).resolve().parent
helper=OUT.parent/'CommercialBlocks/LowPoly_20261104/KoreanPalette/SignageReady/FrontOnly/build_front_only.py'
tree=ast.parse(helper.read_text(encoding='utf8'))
exec(compile(ast.Module(body=[n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name=='render'],type_ignores=[]),str(helper),'exec'))
bpy.ops.wm.open_mainfile(filepath=str(OUT/'Models/RE_01.blend'))
for ob in bpy.data.collections['Presentation_ONLY'].objects:
    if ob.name.startswith('ParcelGuide_'):ob.hide_render=True
camera=bpy.context.scene.camera;camera.location=(0,-25,8.5)
camera.rotation_euler=(Vector((0,-.4,1.9))-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=13.6
bpy.context.view_layer.update();render(OUT/'Previews/RE_01_Front.png')
