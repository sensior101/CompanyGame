"""Assemble Unity gait or explicitly supplied Blender frames into GIF previews."""
from pathlib import Path
from PIL import Image, ImageDraw
import argparse
import sys

def encode_sequence(folder, output, fps=24, expected=None):
    """Encode existing render frames; does not regenerate or retouch the images."""
    paths=sorted(Path(folder).glob('frame_*.png'))
    if not paths or (expected is not None and len(paths)!=expected):
        raise RuntimeError(f'Unexpected render count: {len(paths)}; expected {expected}')
    frames=[]
    for path in paths:
        with Image.open(path) as source:
            frame=source.convert('RGB');frame.thumbnail((480,540),Image.Resampling.LANCZOS);frames.append(frame)
    # A palette sampled across the clip prevents colors appearing late from
    # being quantized against only the first static frame.
    sample=Image.new('RGB',(frames[0].width*4,frames[0].height))
    for i in range(4):sample.paste(frames[round(i*(len(frames)-1)/3)],(frames[0].width*i,0))
    palette=sample.quantize(colors=256)
    indexed=[frame.quantize(palette=palette,dither=Image.Dither.NONE) for frame in frames]
    durations=[10*(round((i+1)*100/fps)-round(i*100/fps)) for i in range(len(frames))]
    indexed[0].save(output,save_all=True,append_images=indexed[1:],duration=durations,loop=0,disposal=2,optimize=False)
    print('GIF_SEQUENCE',len(frames),sum(durations),'ms',output)

if '--frames' in sys.argv:
    parser=argparse.ArgumentParser()
    parser.add_argument('--frames',required=True);parser.add_argument('--output',required=True)
    parser.add_argument('--fps',type=int,default=24);parser.add_argument('--expected',type=int)
    options=parser.parse_args()
    encode_sequence(options.frames,options.output,options.fps,options.expected)
    sys.exit(0)

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
