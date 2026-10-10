"""Build one of the eight individually authored buildings in Blender.
Example: blender --background --python build_blocks.py -- 4
"""
import runpy,sys
from pathlib import Path
BASE=Path(__file__).resolve().parent
DESIGNS={1:'01_concrete_core.py',2:'02_ivory_oak.py',3:'03_brick_loft.py',4:'04_rounded_ribbon.py',5:'05_charcoal_terrace.py',6:'06_sandstone_garden.py',7:'07_ivory_portals.py',8:'08_stepped_masonry.py'}
args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
if len(args)!=1 or not args[0].isdigit() or int(args[0]) not in DESIGNS:
    raise SystemExit('Choose one design: blender --background --python build_blocks.py -- 1 (through 8)')
runpy.run_path(str(BASE/'Designs'/DESIGNS[int(args[0])]),run_name='__main__')
