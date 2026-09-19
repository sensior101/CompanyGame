"""Assemble deterministic Unity gait frames into portable GIF previews."""
from pathlib import Path
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
FPS = 24
for name in ('Walk', 'Run'):
    paths = sorted((HERE / 'MotionPreview' / name).glob('frame_*.png'))
    if len(paths) != 48:
        raise RuntimeError(f'{name}: expected 48 frames, found {len(paths)}')
    frames = [Image.open(path).convert('RGB') for path in paths]
    palette = frames[0].quantize(colors=128)
    # Use near-whole cycles (two walk / two run cycles) for a smooth GIF seam.
    loop_frames = 28 if name == 'Walk' else 22
    indexed = [frame.quantize(palette=palette, dither=Image.Dither.NONE) for frame in frames[:loop_frames]]
    durations = [10 * (round((i+1)*100/FPS)-round(i*100/FPS)) for i in range(loop_frames)]
    indexed[0].save(HERE / f'Unity_{name}Cycle.gif', save_all=True,
                    append_images=indexed[1:], duration=durations, loop=0, disposal=2)
    # Twelve sampled poses help inspect contact, knee flexion and body balance.
    sheet = Image.new('RGB', (960, 558), '#e5e5df')
    for cell, index in enumerate(range(0, 48, 4)):
        tile = frames[index].resize((240, 160))
        x, y = (cell % 4)*240, (cell // 4)*186
        sheet.paste(tile, (x, y+22))
        ImageDraw.Draw(sheet).text((x+8, y+6), f'{name} {index/FPS:.2f}s', fill='#202820')
    sheet.save(HERE / f'Unity_{name}ContactSheet.png')
    print(name, len(frames), sum(durations), 'ms')
