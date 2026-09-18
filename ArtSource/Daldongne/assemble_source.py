"""Assemble the editable, deterministic Blender source without needing Blender locally."""
from pathlib import Path

folder = Path(__file__).resolve().parent
base = (folder / 'build_map.py').read_text(encoding='utf-8')
props = (folder / 'props.py').read_text(encoding='utf-8')
refine = (folder / 'refine_map.py').read_text(encoding='utf-8')
finish = (folder / 'finish_map.py').read_text(encoding='utf-8')
source = base.replace('# PROPS_SOURCE_INSERTION_POINT', props) + '\n' + refine + '\n' + finish
compile(source, 'DaldongneTown_Source.py', 'exec')
(folder / 'DaldongneTown_Source.py').write_text(source, encoding='utf-8')
print('Wrote deterministic Blender source:', len(source), 'characters')
