"""Trim render_icons.py output to the food, centre it on a square with a small margin and save 256 px Unity icons.
Run with the system Python (Pillow): python crop_icons.py <raw dir> <unity icon dir>"""
import sys
from pathlib import Path
from PIL import Image

SIZE, MARGIN = 256, .06
raw, out = Path(sys.argv[1]), Path(sys.argv[2])
out.mkdir(parents=True, exist_ok=True)
done = 0
for path in sorted(raw.glob('*.png')):
    image = Image.open(path).convert('RGBA')
    box = image.getchannel('A').point(lambda a: 255 if a > 8 else 0).getbbox()
    if not box: raise SystemExit(f'{path.name}: empty render')
    food = image.crop(box)
    side = round(max(food.size) / (1 - 2 * MARGIN))
    square = Image.new('RGBA', (side, side), (0, 0, 0, 0))
    square.alpha_composite(food, ((side - food.width) // 2, (side - food.height) // 2))
    square.resize((SIZE, SIZE), Image.LANCZOS).save(out / path.name, optimize=True)
    done += 1
print('CROPPED', done)
