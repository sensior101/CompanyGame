"""Refresh the whole-parcel PNG only; never save or change exported assets."""
import bpy,ast,sys
from pathlib import Path
from mathutils import Vector
sys.dont_write_bytecode=True
OUT=Path(__file__).resolve().parent
helper=OUT.parent.parent/'CommercialBlocks/LowPoly_20261104/KoreanPalette/SignageReady/FrontOnly/build_front_only.py'
tree=ast.parse(helper.read_text(encoding='utf8'))
exec(compile(ast.Module(body=[n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name=='render'],type_ignores=[]),str(helper),'exec'))
bpy.ops.wm.open_mainfile(filepath=str(OUT/'Models/RE_01.blend'))
camera=bpy.context.scene.camera;camera.data.type='ORTHO';camera.data.ortho_scale=78
bpy.context.view_layer.update();render(OUT/'Previews/RE_01_Parcel.png')
