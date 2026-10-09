"""Portable tiled base-color textures used by the independently authored buildings."""
import bpy,numpy as np
from pathlib import Path
base=Path(__file__).resolve().parents[1]/'Exports/Textures'
n=1024;y,x=np.mgrid[0:n,0:n];rng=np.random.default_rng(971)
wood=.91+.038*np.sin(x*.032+np.sin(y*.009)*.8)+.018*np.sin(x*.37+np.sin(y*.015)*1.5)+rng.normal(0,.010,(n,n))
maps={'Oak':(wood,(.40,.235,.12))}
im=bpy.data.images.load(str(base/'brick.png'));raw=np.array(im.pixels[:]).reshape(n,n,4)[:,:,0]
maps['CreamBrick']=(raw,(.63,.565,.46));maps['CharcoalBrick']=(raw,(.24,.25,.245))
for name,(grain,color) in maps.items():
    pixels=np.ones((n,n,4),dtype=np.float32);pixels[:,:,:3]=grain[:,:,None]*np.array(color)
    image=bpy.data.images.new(name,n,n);image.pixels.foreach_set(pixels.ravel());image.filepath_raw=str(base/(name+'.png'));image.file_format='PNG';image.save()
print('MATERIALS_READY',flush=True)
