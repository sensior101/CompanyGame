"""Build the matching boy without changing the approved girl's outputs.

Usage: blender --background --python build_male_player.py [-- --no-render]
The shared generator preserves the face, cap, jacket, palette and joint pivots.
"""
from pathlib import Path

MALE = True
source = Path(__file__).with_name('build_female_player.py')
exec(compile(source.read_text(encoding='utf-8'), str(source), 'exec'))
