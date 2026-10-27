"""Reference-driven food meshes. Run with Blender --background --python this_file -- [--sample|--build|--render].
Reuses the established mesh3d geometry functions; never writes Unity Assets or gameplay data.
"""
import ast, bpy, bmesh, math, random, json, sys, hashlib, struct, zlib
import numpy as np
from pathlib import Path
from mathutils import Vector, Euler
from mathutils.bvhtree import BVHTree
from math import sin, cos, pi, sqrt

HERE=Path(__file__).resolve().parent
MENU=json.loads((HERE/'menu.json').read_text(encoding='utf8'))
for folder in ('Models/FBX','Models/GLB','Renders','QA','Textures'): (HERE/folder).mkdir(parents=True,exist_ok=True)
# Import only existing pure geometry helpers, avoiding the legacy software renderer's Pillow dependency.
geometry_source=HERE/'Source/mesh3d.py'
if not geometry_source.exists():geometry_source=HERE.parent/'mesh3d.py'
source=ast.parse(geometry_source.read_text(encoding='utf8'))
geo={'math':math,'PI':pi}
exec(compile(ast.Module(body=[n for n in source.body if isinstance(n,ast.FunctionDef) and n.name in {'lathe_geo','prism_geo','box_geo','rrect','wob','star'}],type_ignores=[]),str(HERE.parent/'mesh3d.py'),'exec'),geo)

COLORS={
'ceramic':'292b2a','rim':'41423d','whiteplate':'e3d4b9','metal':'8c928e','wood':'986336','woodedge':'704728',
'cream':'f4e6c9','rice':'eee6cd','dough':'d09a4d','toast':'a5652c','crust':'8a4827','fried':'d59231','friedlight':'e6ad51',
'red':'b94326','redsauce':'aa321e','tomato':'c94e31','orange':'e88928','yellow':'efb937','egg':'eadba0','green':'417b32','lightgreen':'7eab48','darkgreen':'234c2c',
'meat':'8c4c37','pork':'c78b6e','fat':'ead7b4','brown':'6e3e26','darkbrown':'482c20','chocolate':'573322','black':'292721','nori':'293b2f',
'pink':'d96674','salmon':'dd8353','tuna':'b65358','purple':'8870a1','blueberry':'4c5279','glass':'c8d7d3','ice':'d4dfd6','coffee':'483025',
'blue':'2154ad','label':'ece0c6','silver':'919997','paper':'d1b98a','packpink':'c96487','flavor':'d96674','flavordark':'a34350'
}
COLORS.update(glassrim='cbd7d3',crumb='b77335',baked='ba6d30',tomatoflesh='e7865a',lettuce='90aa4c',berry='c94140',ricepaper='eee6d1',sausage='493b38',sausagecut='62514d',tortilla='dfc796',wrapper='dcc498',broth='ba9659',milktea='ba9976',teagold='bca250',teaamber='9c5f28',teagreen='91a157',lemonade='d6c584',pinkade='ce8d80',grapeade='b0bb79',waterglass='b2c7c6',waterliquid='9dbaba')
FLAVORS={'strawberry':('da8290','b14b60'),'mango':('ecb339','c78223'),'vanilla':('efe3bc','d7c391'),'chocolate':('896049','543426'),'matcha':('8da454','55713c'),'orange':('efa34d','d6782c'),'blueberry':('9b80b5','61517e'),'coffee':('b9906d','79563b'),'sweetpotato':('d7b772','a68558'),'kiwi':('a1b854','698835'),'cookies':('d8d1bd','55433b'),'yogurt':('eeead9','d0c8b2')}
# Dish-specific colors keep shared egg, fish and pancake materials unchanged.
COLORS.update(steamedegg='f5cc4e',pajeon='dda448',pajeonedge='ad692e',
              sushisalmon='ed895a',sushituna='c94e59',sushishrimp='f1bb91',
              shrimptail='d86836',eggwhite='f6edda',yolk='f4b529',
              spinach='426638',sprout='e9d5a0',carrot='e88b32',beef='75452c')

def linear(v):return v/12.92 if v<.04045 else ((v+.055)/1.055)**2.4

def png_array(path,array):
    h,w,_=array.shape
    def chunk(kind,data):return struct.pack('>I',len(data))+kind+data+struct.pack('>I',zlib.crc32(kind+data)&0xffffffff)
    raw=b''.join(b'\0'+row.tobytes() for row in array)
    path.write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>2I5B',w,h,8,2,0,0,0))+chunk(b'IDAT',zlib.compress(raw,7))+chunk(b'IEND',b''))

def surface_maps(key,color):
    """Portable albedo/normal maps; no render-only procedural shader dependencies."""
    size=256;seed=sum((i+1)*ord(c) for i,c in enumerate(key));rng=np.random.default_rng(seed)
    def noise(cells):
        grid=rng.random((cells,cells));t=np.arange(size)*cells/size;ii=np.floor(t).astype(int);f=t-ii;f=f*f*(3-2*f)
        a=grid[ii[:,None]%cells,ii[None,:]%cells];b=grid[(ii[:,None]+1)%cells,ii[None,:]%cells]
        c=grid[ii[:,None]%cells,(ii[None,:]+1)%cells];d=grid[(ii[:,None]+1)%cells,(ii[None,:]+1)%cells]
        return (a*(1-f[:,None])+b*f[:,None])*(1-f[None,:])+(c*(1-f[:,None])+d*f[:,None])*f[None,:]
    broad=noise(5);medium=noise(19);fine=noise(83);yy,xx=np.mgrid[0:size,0:size]/size
    baked=key in ['dough','toast','fried','friedlight','crust','crumb','baked','paper']
    flesh=key in ['meat','pork','fat','salmon','tuna','brown','darkbrown']
    leaves=key in ['green','lightgreen','darkgreen','nori','lettuce']
    amplitude=.13 if baked else .065 if flesh or leaves else .020
    pattern=(broad-.5)*.20+(medium-.5)*.28+(fine-.5)*.52
    if key=='tortilla':
        scorch=np.clip((noise(11)-.60)*3,0,.85);pattern-=scorch*.30;amplitude=.10
    if baked:
        pores=np.maximum(0,(fine-.65))*2.2;pattern-=pores*.28
        pattern+=.07*np.sin(xx*28+medium*4)*np.sin(yy*31+broad*4)
    if flesh:pattern+=.10*np.sin(xx*94+np.sin(yy*9)*3+broad*5)
    if leaves:pattern+=.10*np.cos(xx*62+yy*20+medium*3)
    if key in ['wood','woodedge']:pattern=.22*np.sin(xx*95+medium*5)+.15*(fine-.5)
    rgb=np.array([int(color[i:i+2],16)/255 for i in (0,2,4)])
    arr=np.clip(rgb[None,None,:]*(1+pattern[:,:,None]*amplitude*3),0,1)
    if key=='tortilla':arr=arr*(1-scorch[:,:,None]*.50)+np.array([.32,.15,.055])[None,None,:]*scorch[:,:,None]*.50
    if key=='pajeon':
        # Broad, uneven contact browning is baked into the portable albedo.
        toast=np.clip((noise(13)-.43)*2.5,0,.82)
        toast*=.65+.35*noise(35)
        arr=arr*(1-toast[:,:,None]) + np.array([.48,.24,.075])[None,None,:]*toast[:,:,None]
        pattern+=(fine-.5)*.18+toast*.10
    if key in ['sushisalmon','sushituna']:
        curve=yy*6.2+xx*.92+.11*np.sin(xx*7+yy*3)+.12*np.sin(yy*12)
        band=np.abs(np.sin(curve*pi))
        vein=np.clip((.16-band)*9,0,1) if key=='sushisalmon' else np.clip((.075-band)*8,0,.28)
        vein*=.80+.20*medium
        veincolor=np.array([.98,.81,.64]) if key=='sushisalmon' else np.array([.83,.43,.43])
        arr=arr*(1-vein[:,:,None]) + veincolor[None,None,:]*vein[:,:,None]
        pattern=(fine-.5)*.10+np.sin(curve*pi*8)*.015
    if key=='sushishrimp':
        band=np.clip((np.cos(yy*12*pi+.38*np.sin(xx*9))-.56)*1.3,0,.65)
        edge=np.clip(np.abs(xx-.5)*2-.46,0,1)
        coral=np.maximum(band,edge*.72)
        arr=arr*(1-coral[:,:,None])+np.array([.87,.39,.20])[None,None,:]*coral[:,:,None]
        pattern=(fine-.5)*.1+band*.07
    albedo=HERE/'Textures'/('V2_'+key+'_'+color+'_BaseColor.png');normal=HERE/'Textures'/('V2_'+key+'_Normal.png')
    png_array(albedo,(arr*255+.5).astype(np.uint8))
    h=pattern*(.8 if baked else .35 if flesh or leaves else .08)
    dx=(np.roll(h,-1,1)-np.roll(h,1,1))*5;dy=(np.roll(h,-1,0)-np.roll(h,1,0))*5
    nv=np.stack((-dx,dy,np.ones_like(dx)),axis=2);nv/=np.linalg.norm(nv,axis=2,keepdims=True)
    png_array(normal,((nv*.5+.5)*255+.5).astype(np.uint8))
    return albedo,normal

UPDATED_MATERIALS=set()

def material(key,hexcolor=None):
    color=hexcolor or COLORS[key];name='FoodV2_'+key+'_'+color
    if name in UPDATED_MATERIALS:return bpy.data.materials[name]
    UPDATED_MATERIALS.add(name)
    m=bpy.data.materials.get(name) or bpy.data.materials.new(name);m.use_nodes=True
    rgb=tuple(linear(int(color[i:i+2],16)/255) for i in (0,2,4))
    m.diffuse_color=(*rgb,1);m.node_tree.nodes.clear();s=m.node_tree.nodes.new('ShaderNodeBsdfPrincipled');out=m.node_tree.nodes.new('ShaderNodeOutputMaterial');m.node_tree.links.new(s.outputs[0],out.inputs['Surface']);s.inputs['Base Color'].default_value=(*rgb,1)
    s.inputs['Roughness'].default_value=.88;s.inputs['Metallic'].default_value=0;s.inputs['Specular IOR Level'].default_value=.12
    if key not in ['glass','glassrim','ricepaper','ice','studio','silver','label','blue','packpink','whiteplate','ceramic','rim','metal','cream','rice','egg']:
        albedo,normal=surface_maps(key,color)
        tex=m.node_tree.nodes.new('ShaderNodeTexImage');tex.image=bpy.data.images.load(str(albedo),check_existing=False);tex.image.colorspace_settings.name='sRGB'
        m.node_tree.links.new(tex.outputs['Color'],s.inputs['Base Color'])
        nt=m.node_tree.nodes.new('ShaderNodeTexImage');nt.image=bpy.data.images.load(str(normal),check_existing=False);nt.image.colorspace_settings.name='Non-Color'
        nm=m.node_tree.nodes.new('ShaderNodeNormalMap');nm.inputs['Strength'].default_value=.20;m.node_tree.links.new(nt.outputs['Color'],nm.inputs['Color']);m.node_tree.links.new(nm.outputs['Normal'],s.inputs['Normal'])
    if key=='glass':s.inputs['Alpha'].default_value=.10;m.diffuse_color=(*rgb,.10)
    if key in ['teagold','teaamber','teagreen']:s.inputs['Alpha'].default_value=.78;m.diffuse_color=(*rgb,.78)
    if key=='waterglass':s.inputs['Alpha'].default_value=.32;m.diffuse_color=(*rgb,.32)
    if key=='waterliquid':s.inputs['Alpha'].default_value=.25;m.diffuse_color=(*rgb,.25)
    if key=='ice':s.inputs['Alpha'].default_value=.65;m.diffuse_color=(*rgb,.65)
    if key=='ricepaper':s.inputs['Alpha'].default_value=.27;m.diffuse_color=(*rgb,.27)
    return m

class Geo:
    def __init__(self):self.v=[];self.f=[];self.m=[];self.s=[];self.rng=random.Random(17)
    def add(self,verts,faces,mat,at=(0,0,0),scale=(1,1,1),rot=(0,0,0),smooth=False):
        start=len(self.v);r=Euler(rot,'XYZ').to_matrix();t=Vector(at)
        self.v.extend(tuple(r@Vector((x*scale[0],y*scale[1],z*scale[2]))+t) for x,y,z in verts)
        self.f.extend(tuple(start+i for i in f) for f in faces);self.m.extend([mat]*len(faces));self.s.extend([smooth]*len(faces))
    def lathe(self,mat,profile,at=(0,0,0),scale=(1,1,1),sides=24,smooth=False):
        v,f=geo['lathe_geo'](profile,sides,False,True)
        self.add([(x,-z,y) for x,y,z in v],f,mat,at,scale,smooth=smooth)
    def ell(self,mat,at,size,seg=12,rings=6,rot=(0,0,0),smooth=True):
        p=[(sin(pi*i/rings),-cos(pi*i/rings)) for i in range(rings+1)]
        v,f=geo['lathe_geo'](p,seg,False,False)
        self.add([(x,-z,y) for x,y,z in v],f,mat,at,size,rot,smooth)
    def dome(self,mat,at,r,h):
        self.lathe(mat,[(0,0)]+[(r*cos(pi*i/16),h*sin(pi*i/16)) for i in range(9)],at,sides=24,smooth=True)
    def box(self,mat,at,size,rot=(0,0,0),bevel=.06):
        # Rounded rectangular extrusion with narrow bevel rings, all real mesh geometry.
        sx,sy,sz=size;b=min(bevel,min(size)*.25)
        poly=geo['rrect'](sx,sy,b,2);n=len(poly);v=[]
        for inset,z in ((b,-sz/2),(0,-sz/2+b),(0,sz/2-b),(b,sz/2)):
            v += [(x*(sx-2*inset)/sx,y*(sy-2*inset)/sy,z) for x,y in poly]
        f=[tuple(reversed(range(n))),tuple(range(n*3,n*4))]
        for j in range(3):
            for i in range(n):f.append((j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i))
        self.add(v,f,mat,at,rot=rot)
    def prism(self,mat,poly,height,at=(0,0,0),rot=(0,0,0)):
        # Preserve the authored 2D polygon's Y direction when mapping the old Y-up helper to Z-up.
        v,f=geo['prism_geo'](poly,height,'y');self.add([(x,z,y) for x,y,z in v],f,mat,at,rot=rot)
    def tube(self,mat,points,r=.03,sides=6,smooth=True,radii=None):
        pts=[Vector(p) for p in points];v=[];f=[]
        for i,p in enumerate(pts):
            tangent=(pts[min(i+1,len(pts)-1)]-pts[max(0,i-1)]).normalized();axis=Vector((0,0,1))
            if abs(tangent.dot(axis))>.96:axis=Vector((0,1,0))
            u=tangent.cross(axis).normalized();w=tangent.cross(u).normalized();rr=radii[i] if radii is not None else r
            for k in range(sides):v.append(tuple(p+rr*(cos(k*2*pi/sides)*u+sin(k*2*pi/sides)*w)))
        for i in range(len(pts)-1):
            for k in range(sides):f.append((i*sides+k,i*sides+(k+1)%sides,(i+1)*sides+(k+1)%sides,(i+1)*sides+k))
        f.extend([tuple(reversed(range(sides))),tuple(range((len(pts)-1)*sides,len(pts)*sides))]);self.add(v,f,mat,smooth=smooth)
    def torus(self,mat,at,major,minor,seg=20,ring=6,scale=(1,1,1),rot=(0,0,0)):
        v=[((major+minor*cos(j*2*pi/ring))*cos(i*2*pi/seg),(major+minor*cos(j*2*pi/ring))*sin(i*2*pi/seg),minor*sin(j*2*pi/ring)) for i in range(seg) for j in range(ring)]
        f=[(i*ring+j,((i+1)%seg)*ring+j,((i+1)%seg)*ring+(j+1)%ring,i*ring+(j+1)%ring) for i in range(seg) for j in range(ring)]
        self.add(v,f,mat,at,scale,rot,True)
    def mark(self):return len(self.v)
    def organic(self,mat,at,size,seed=1,rough=.055,exponent=1.0,seg=20,rings=10,rot=(0,0,0)):
        v=[];f=[]
        def sp(x):return math.copysign(abs(x)**exponent,x)
        for j in range(rings+1):
            t=pi*j/rings
            for i in range(seg):
                a=2*pi*i/seg;w=1+rough*(.55*sin(3*a+seed)*sin(t)**2+.30*sin(7*a+2*t+seed*.7)+.15*sin(13*a-5*t))
                v.append((size[0]*sp(sin(t)*cos(a))*w,size[1]*sp(sin(t)*sin(a))*w,size[2]*sp(cos(t))*(1+rough*.25*sin(5*a+seed)*sin(t))))
        for j in range(rings):
            for i in range(seg):f.append((j*seg+i,j*seg+(i+1)%seg,(j+1)*seg+(i+1)%seg,(j+1)*seg+i))
        self.add(v,f,mat,at,rot=rot,smooth=True)
    def transform(self,start,at=(0,0,0),scale=(1,1,1),rot=(0,0,0)):
        r=Euler(rot,'XYZ').to_matrix();self.v[start:]=[tuple(r@Vector((x*scale[0],y*scale[1],z*scale[2]))+Vector(at)) for x,y,z in self.v[start:]]

def smooth_path(points,steps=6):
    p=[Vector(points[0])]+[Vector(x) for x in points]+[Vector(points[-1])];out=[]
    for i in range(1,len(p)-2):
        a,b,c,d=p[i-1:i+3]
        for j in range(steps):
            t=j/steps;out.append(tuple(.5*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t)))
    return out+[tuple(points[-1])]

def plate(g,mat='ceramic',r=1,oval=1):
    g.lathe(mat,[(0,.01),(.63*r,.01),(.67*r,.03),(.81*r,.065),(.98*r,.14),(r,.185),(.995*r,.204),(.974*r,.218),(.91*r,.187),(.78*r,.145),(0,.125)],scale=(1,oval,1),sides=36,smooth=True);return .16
def bowl(g,mat='ceramic',r=1,h=.66,handles=False):
    g.lathe(mat,[(0,.02),(.47*r,.02),(.51*r,.07),(.57*r,h*.16),(.73*r,h*.35),(.91*r,h*.75),(r,h),(r,h+.04),(.98*r,h+.065),(.943*r,h+.06),(.916*r,h),(.84*r,h*.61),(.66*r,h*.33),(0,h*.25)],sides=32,smooth=True)
    g.torus(mat,(0,0,h*.10),.50*r,.025,32,5)
    if handles:
        for s in [-1,1]:g.box(mat,(s*r,.0,h*.74),(.40,.38,.16),bevel=.06)
    return h*.84
def soup(g,color,y,r=.88):g.lathe(color,[(0,y-.04),(r,y-.04),(r,y),(r*.985,y+.010),(0,y+.008)],sides=32,smooth=True)
def leaf(g,at=(0,0,0),length=.5,width=.25,angle=0,mat='green'):
    v=[];f=[]
    for i in range(7):
        t=i/6;ww=sin(pi*t)*width/2*(1+.15*sin(t*8*pi))
        edge=.05*sin(t*pi)+.024*sin(t*7*pi)
        v += [(-ww,t*length,edge),(0,t*length,.06+.10*sin(t*pi)),(ww,t*length,edge)]
    for i in range(6):
        for j in range(2):f.append((i*3+j,i*3+j+1,(i+1)*3+j+1,(i+1)*3+j))
    g.add(v,f,mat,at,rot=(0,0,angle));g.tube('lightgreen',[(at[0],at[1],at[2]+.061),(at[0]-sin(angle)*length*.8,at[1]+cos(angle)*length*.8,at[2]+.13)],.009,4)

def lettuce(g,at=(0,0,0),length=.5,width=.5,angle=0,mat='lettuce',seed=0):
    v=[];f=[];n=10;m=8
    for j in range(n+1):
        t=j/n;ww=(sin(pi*t)**.45)*width/2
        for i in range(m+1):
            u=(i/m-.5)*2;x=u*ww*(1+.07*sin(t*7*pi+seed));z=.10*sin(t*pi)+.06*u*u*sin(t*11*pi+u*5+seed)+.06*t*t
            v.append((x,t*length,z))
    for j in range(n):
        for i in range(m):a=j*(m+1)+i;f.append((a,a+1,a+m+2,a+m+1))
    k=g.mark();g.add(v,f,mat,smooth=True)
    g.tube('lightgreen',[(0,j*length/9,.105*sin(j*pi/9)+.06*(j/9)**2+.005) for j in range(10)],.007,4)
    for t in [.25,.45,.65]:
        for s in [-1,1]:g.tube('lightgreen',[(0,length*t,.10*sin(t*pi)+.01),(s*width*.32,length*(t+.11),.08)],.003,3)
    g.transform(k,at,rot=(0,0,angle))
def greens(g,y,r=.75,n=7):
    for i in range(n):a=i*2*pi/n;lettuce(g,(cos(a)*r*.35,sin(a)*r*.35,y),.48,.41,a-pi/2,('green' if i%3 else 'lettuce'),i)
def surface_height(g,x,y,fallback):
    faces=[f for f,m in zip(g.f,g.m) if m not in {'ceramic','whiteplate','wood','woodedge','metal','rim','glass','glassrim','paper'}]
    if not faces:return fallback
    hit,normal,_,_=BVHTree.FromPolygons(g.v,faces,all_triangles=False).ray_cast(Vector((x,y,4)),Vector((0,0,-1)),5)
    return hit.z if hit is not None else fallback

def scallions(g,y,n=9,r=.48,seed=1):
    rng=random.Random(seed)
    for i in range(n):
        a=rng.random()*2*pi;rr=sqrt(rng.random())*r;x=rr*cos(a);yy=rr*sin(a);z=surface_height(g,x,yy,y)+.014
        g.torus('lightgreen',(x,yy,z),.039,.011,10,4,rot=(rng.uniform(-.13,.13),rng.uniform(-.13,.13),0))
def grains(g,at=(0,0,.2),r=.7,n=80,color='rice',height=.20,seed=17):
    rng=random.Random(seed)
    g.ell(color,at,(r,r,height),seg=16,rings=6)
    for i in range(n):
        a=rng.random()*2*pi;rr=sqrt(rng.random())*r*.95;x=rr*cos(a);yy=rr*sin(a);z=height*sqrt(max(0,1-(rr/r)**2))
        g.ell(color,(at[0]+x,at[1]+yy,at[2]+z),(.06,.025,.024),seg=6,rings=4,rot=(0,0,a*3))
def mushroom(g,at=(0,0,0),s=.2):
    k=g.mark();poly=[(-.28*s,-.72*s),(.29*s,-.72*s),(.32*s,-.12*s),(.80*s,-.10*s)]
    poly += [(cos(i*pi/12)*s*.96,sin(i*pi/12)*s*.72) for i in range(13)]
    poly += [(-.80*s,-.10*s),(-.31*s,-.13*s)]
    g.prism('brown',poly,s*.25)
    g.prism('cream',[(x*.91,yy*.90) for x,yy in poly],s*.06,(0,0,s*.25))
    for i in range(7):a=.15+i*(pi-.30)/6;g.tube('woodedge',[(.16*s*cos(a),-.08*s,s*.312),(.68*s*cos(a),.50*s*sin(a),s*.312)],s*.013,3)
    g.transform(k,at,rot=(.10,-.12,.5))

def baked_piece(g,at,poly,height=.22,mat='dough',side='crust',seed=1,cracks=False):
    # Closed irregular crust and a gently domed crumb surface, with real uneven edges.
    pts=[]
    for i,p in enumerate(poly):
        q=poly[(i+1)%len(poly)]
        for j in range(5):t=j/5;pts.append((p[0]*(1-t)+q[0]*t,p[1]*(1-t)+q[1]*t))
    n=len(pts);v=[];f=[];mats=[];cx=sum(x for x,y in pts)/n;cy=sum(y for x,y in pts)/n
    for scale,z in [(.90,.01),(1,.045),(1,height*.70),(.95,height)]:
        for i,(x,y) in enumerate(pts):
            jitter=1+.018*sin(i*2*pi/n*5+seed)+.007*sin(i*2*pi/n*11);v.append((cx+(x-cx)*scale*jitter,cy+(y-cy)*scale*jitter,z+.004*sin(i*2*pi/n*5+seed)))
    f.append(tuple(reversed(range(n))));mats.append(side)
    for j in range(3):
        for i in range(n):f.append((j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i));mats.append(side if j==0 else mat)
    for ring in range(1,5):
        t=1-ring/5
        for i,(x,y) in enumerate(pts):v.append((cx+(x-cx)*t,cy+(y-cy)*t,height+.025*sin(pi*t)+.003*sin(i*2*pi/n*5+seed+t*2)))
        old=(ring+2)*n;new=(ring+3)*n
        for i in range(n):f.append((old+i,old+(i+1)%n,new+(i+1)%n,new+i));mats.append(mat)
    v.append((cx,cy,height+.018));center=len(v)-1
    for i in range(n):f.append((7*n+i,7*n+(i+1)%n,center));mats.append(mat)
    start=len(g.f);g.add(v,f,mat,at,smooth=True);g.m[start:]=mats
    if cracks:
        rng=random.Random(seed)
        for i in range(5):
            px,py=pts[(i*7+seed)%n];tt=rng.uniform(.35,.75);pts2=[]
            for j in range(8):t=.18+(tt-.18)*j/7;pts2.append((at[0]+cx+(px-cx)*t+.006*sin(j*2),at[1]+cy+(py-cy)*t,at[2]+height+.035*sin(pi*t)+.014))
            g.tube(side,pts2,.0045,3)

def crumbs(g,at,size,n=65,seed=1):
    rng=random.Random(seed)
    for i in range(n):
        a=rng.random()*2*pi;r=sqrt(rng.random())*.98;x=size[0]*r*cos(a);yy=size[1]*r*sin(a);z=size[2]*sqrt(max(0,1-r*r));ss=rng.uniform(.025,.050)
        g.organic('friedlight' if i%4 else 'fried',(at[0]+x,at[1]+yy,at[2]+z),(ss,ss*.72,ss*.56),i,rough=.15,seg=6,rings=3)

def shredded(g,at,r=.40,n=70,seed=11):
    rng=random.Random(seed)
    g.organic('lettuce',(at[0],at[1],at[2]-.03),(r*.81,r*.75,.095),seed,rough=.12,seg=16,rings=7)
    for i in range(n):
        a=rng.random()*2*pi;rr=r*sqrt(rng.random());x=at[0]+rr*cos(a);y=at[1]+rr*sin(a)
        z=at[2]+.055*sqrt(max(0,1-(rr/r)**2))
        pts=[(x+.07*cos(a)*sin(j*pi/6),y+(j/6-.5)*.23,z+.023*sin(j*pi/6)) for j in range(7)]
        g.tube('lettuce' if i%3 else 'cream',pts,.010,4)

def veg_slice(g,at,r=.18,kind='cucumber',rot=(0,0,0)):
    k=g.mark();outer='green' if kind=='cucumber' else 'tomato';inner='lettuce' if kind=='cucumber' else 'tomatoflesh'
    g.lathe(outer,[(0,0),(r,0),(r,.035),(0,.035)],sides=20,smooth=True)
    g.lathe(inner,[(0,.036),(r*.85,.036),(r*.85,.040),(0,.040)],sides=20,smooth=True)
    for i in range(4 if kind=='tomato' else 5):
        a=i*2*pi/(4 if kind=='tomato' else 5)
        g.ell('red' if kind=='tomato' else 'cream',(.48*r*cos(a),.48*r*sin(a),.043),(.31*r,.15*r,.004),seg=8,rings=3,rot=(0,0,a))
        for j in range(2):g.ell('yellow' if kind=='tomato' else 'rice',((.35+j*.19)*r*cos(a),(.35+j*.19)*r*sin(a),.048),(.022*r,.055*r,.003),seg=5,rings=3,rot=(0,0,a))
    g.transform(k,at,rot=rot)

def scatter_surface(g,mat,n=35,seed=4,r=.78,size=.013,exclude=()):
    blocked={'ceramic','whiteplate','wood','woodedge','metal','rim','glass','glassrim','paper'}|set(exclude)
    faces=[f for f,m in zip(g.f,g.m) if m not in blocked]
    if not faces:return
    tree=BVHTree.FromPolygons(g.v,faces,all_triangles=False);rng=random.Random(seed)
    for i in range(n):
        a=rng.random()*2*pi;rr=r*sqrt(rng.random());x=rr*cos(a);y=rr*sin(a)
        loc,normal,_,_=tree.ray_cast(Vector((x,y,4)),Vector((0,0,-1)),5)
        if loc is None or normal.z<.3:continue
        rot=normal.to_track_quat('Z','Y').to_euler();loc+=normal*size*.19
        g.organic(mat,loc,(size,size*.55,size*.30),seed=i,rough=.1,seg=5,rings=3,rot=rot)

def finish_food(g,item):
    if item['category'] in ['Korean','Western','Japanese','Chinese','Asian']:
        if item['builder'] not in ['sushi','sandwich','burger','gimbap','sundae','wrap','naan','fries']:
            scatter_surface(g,'green',38,13,size=.011,exclude=('yellow','cream'))
    if item['builder']=='pasta':scatter_surface(g,'cream',35,16,size=.014,exclude=('green','lightgreen'))
def tofu(g,at,sz=.23):g.box('cream',at,(sz,sz*.8,sz*.8),rot=(.1,.07,.2),bevel=.025)

def folded_meat(g,at,length=.32,width=.22,angle=0,color='red'):
    """Thin cooked slice with irregular edge and a soft fold, instead of a rectangular block."""
    v=[];f=[]
    for side in [-1,1]:
        for i in range(9):
            t=i/8;w=width*(.48+.07*sin(t*13))
            for j in range(3):
                x=(j-1)*w;v.append((x,(t-.5)*length,.045*sin(t*2.3*pi)+.025*cos(j*pi/2)+side*.017))
    for s in range(2):
        for i in range(8):
            for j in range(2):a=s*27+i*3+j;f.append((a,a+1,a+4,a+3))
    for i in range(8):
        for j in [0,2]:a=i*3+j;f.append((a,a+3,a+30,a+27))
    for i in [0,24]:
        for j in range(2):a=i+j;f.append((a,a+1,a+28,a+27))
    g.add(v,f,color,at,rot=(.12,.13,angle),smooth=True)
def halfegg(g,at,scale=.32):
    g.ell('cream',at,(scale,scale*.72,scale*.36),seg=16,rings=6)
    g.ell('yellow',(at[0],at[1],at[2]+scale*.30),(scale*.48,scale*.45,scale*.13),seg=12,rings=5)
def friedegg(g,at=(0,0,.4),scale=.5):
    g.prism('cream',geo['wob'](18,scale,.13,4),.05,at)
    g.ell('yellow',(at[0]-.035,at[1],at[2]+.06),(scale*.43,scale*.43,scale*.24),seg=16,rings=6)
def noodles(g,y,color='cream',r=.72,n=16,thick=.032,seed=24):
    rng=random.Random(seed)
    for i in range(n):
        rr=r*(.36+.39*rng.random());phase=rng.random()*2*pi;turns=.75+rng.random()*.9;cx=rng.uniform(-.17,.17);cy=rng.uniform(-.17,.17)
        pts=[]
        for j in range(27):
            t=j/26;a=phase+t*turns*2*pi;rad=rr*(.80+.18*sin(t*5*pi+i));z=y+.026*(i%5)+.035*sin(t*4*pi+phase)
            pts.append((cx+rad*cos(a),cy+rad*sin(a),z))
        g.tube(color,pts,thick,6)
def shrimp(g,at=(0,0,0),s=.45,fried=False,rot=0):
    k=g.mark();c='fried' if fried else 'salmon'
    pts=[(.30*cos(-.4+i*.20),.30*sin(-.4+i*.20),.08) for i in range(12)]
    g.tube(c,pts,.1,8,radii=[.115*(1-i*.035) for i in range(12)])
    for i in range(7):a=-.4+i*.29;g.tube('friedlight' if fried else 'cream',[(.20*cos(a),.20*sin(a),.10),(.3*cos(a),.3*sin(a),.188),(.39*cos(a),.39*sin(a),.10)],.016,4)
    for a in [0,.5,-.5]:g.ell('red',(-.18,.30+a*.06,.08),(.16,.055,.024),seg=8,rings=4,rot=(0,0,a))
    if fried:
        rng=random.Random(3)
        for i in range(35):a=rng.uniform(-.4,1.7);rr=rng.uniform(.21,.37);g.ell('friedlight',(rr*cos(a),rr*sin(a),.14),(.04,.038,.036),seg=5,rings=3)
    g.transform(k,at,(s,s,s),(0,0,rot))
def sesame(g,y,r=.6,n=22):
    rng=random.Random(92)
    for i in range(n):
        a=rng.random()*2*pi;rr=rng.random()*r;x=rr*cos(a);yy=rr*sin(a);z=surface_height(g,x,yy,y)+.005
        g.ell('cream',(x,yy,z),(.022,.010,.007),seg=5,rings=3,rot=(0,0,a))
def saucezig(g,at,width=.7,length=1.1,color='cream',r=.025):
    pts=[]
    for i in range(51):
        x=at[0]+sin(i/50*9*pi)*width/2;yy=at[1]+(i/50-.5)*length;pts.append((x,yy,surface_height(g,x,yy,at[2])+r*.65))
    g.tube(color,pts,r,6)
def fruitberry(g,at,s=.17,kind='strawberry'):
    if kind=='strawberry':
        k=g.mark()
        profile=[(0,0),(.60*s,.08*s),(.94*s,.48*s),(.84*s,1.05*s),(.56*s,1.55*s),(.13*s,1.92*s),(0,1.96*s)]
        g.lathe('berry',profile,sides=18,smooth=True)
        for j in range(4):
            z=s*(.30+j*.34)
            for i in range(9):
                a=i*2*pi/9+j*.38
                for q in range(len(profile)-1):
                    if profile[q][1]<=z<=profile[q+1][1]:
                        t=(z-profile[q][1])/(profile[q+1][1]-profile[q][1]);r=profile[q][0]*(1-t)+profile[q+1][0]*t;break
                g.ell('yellow',((r+.003)*cos(a),(r+.003)*sin(a),z),(.007,.007,.014),seg=5,rings=3)
        for i in range(3):leaf(g,(0,0,.01),s*.65,s*.3,i*2*pi/3)
        g.transform(k,at)
    else:
        g.organic('blueberry',at,(s,s,s*.85),seed=3,rough=.04,seg=14,rings=7)
        poly=geo['star'](5,s*.24,s*.12)
        g.prism('darkbrown',poly,.008,(at[0],at[1],at[2]+s*.80))
def swirl(g,at=(0,0,0),r=.4,height=.6,mat='cream',turns=2.7):
    # Fluted piping cross section; conical interior closes the swirl without round sausage coils.
    g.lathe(mat,[(0,0)]+[(r*.84*(1-i/20)**.8,height*i/20) for i in range(21)],at,sides=20,smooth=True)
    steps=max(30,int(turns*20));sides=12;v=[];f=[]
    for i in range(steps+1):
        t=i/steps;a=t*turns*2*pi;rr=r*(1-t)**.82;rad=max(.002,r*.45*(1-t)**.62)
        center=Vector((at[0]+rr*cos(a),at[1]+rr*sin(a),at[2]+height*t));u=Vector((cos(a),sin(a),0));w=Vector((0,0,1))
        for j in range(sides):
            b=j*2*pi/sides;ridge=1 if j%2==0 else .66;v.append(tuple(center+rad*ridge*(u*cos(b)+w*sin(b))))
    for i in range(steps):
        for j in range(sides):f.append((i*sides+j,i*sides+(j+1)%sides,(i+1)*sides+(j+1)%sides,(i+1)*sides+j))
    f.extend([tuple(reversed(range(sides))),tuple(range(steps*sides,(steps+1)*sides))])
    g.add(v,f,mat,smooth=True)
def citrus(g,at=(0,0,0),s=.28,color='yellow',rot=(0,0,0)):
    k=g.mark();g.lathe(color,[(s,0),(s,.035)],sides=16)
    g.lathe('cream',[(s*.87,.036),(s*.87,.05)],sides=16)
    for i in range(8):
        a=i*2*pi/8;poly=[(0,0)]+[(cos(a+.055+j*(2*pi/8-.11)/4)*s*.77,sin(a+.055+j*(2*pi/8-.11)/4)*s*.77) for j in range(5)]
        g.prism(color,poly,.018,(0,0,.05))
    g.transform(k,at,rot=rot)

def glass(g,liquid='flavor',ice=False,straw=True):
    g.lathe(liquid,[(0,.09),(.44,.09),(.52,1.36),(.52,1.43),(0,1.43)],sides=32,smooth=True)
    g.lathe('glass',[(.435,.045),(.475,.045),(.565,1.51),(.563,1.545),(.542,1.545),(.446,.12)],sides=32,smooth=True)
    g.torus('glassrim',(0,0,1.535),.553,.012,32,6)
    g.lathe('glassrim',[(.425,.025),(.46,.025),(.474,.053),(.474,.067),(.434,.067)],sides=32,smooth=True)
    if ice:
        for i,(x,y,a) in enumerate([(-.23,-.13,.3),(.17,-.19,-.5),(.16,.20,.7),(-.16,.22,1),(.03,.03,.1)]):
            g.organic('ice',(x,y,1.47),(.115,.105,.10),seed=i,rough=.03,exponent=.28,seg=12,rings=7,rot=(.17,.15,a))
    if straw:
        g.tube('darkgreen',[(.25,.15,1.30),(.40,.23,2.03)],.026,10)
        g.torus('green',(.40,.23,2.03),.026,.0045,10,4,rot=(0,.20,0))

def mug(g,color='cream',liquid='coffee',clear=False):
    g.lathe(color,[(0,0),(.42,0),(.44,.06),(.53,.92),(.52,1.0),(.47,1.01),(.45,.91),(.39,.12),(0,.12)],sides=32,smooth=True)
    pts=[(.47+.35*sin(a),0,.55+.33*cos(a)) for a in [i*pi/24 for i in range(25)]]
    g.tube(color,pts,.065,10)
    g.lathe(liquid,[(0,.125),(.38,.125),(.447,.90),(.454,.922),(0,.924)],sides=32,smooth=True)
    if color=='glass':
        g.torus('glassrim',(0,0,1.002),.495,.009,32,5)
        g.torus('glassrim',(0,0,.037),.42,.010,32,5)

def build_latte(g,style):
    glass(g,'flavor',True)
    # A curved milk ribbon and embedded puree pieces; identical geometry for all flavors.
    v=[];f=[]
    for i in range(49):
        a=i*2*pi/48
        for z in [.53+.14*sin(a*3)+.04*sin(a*7),1.17+.12*sin(a*2+.5)]:
            r=.44+.09*(z-.07)/1.28+.001;v.append((r*cos(a),r*sin(a),z))
    for i in range(48):f.append((i*2,i*2+1,i*2+3,i*2+2))
    g.add(v,f,'cream',smooth=True)
    for i in range(34):
        a=i*2.399;z=.20+(i%9)*.128;r=.44+.09*(z-.07)/1.28
        g.ell('flavordark',(r*cos(a),r*sin(a),z),(.026,.004,.036+(i%3)*.017),seg=6,rings=4,rot=(0,0,a-pi/2))

def build_softserve(g,style):
    g.lathe('dough',[(0,.03),(.13,.20),(.43,1.02),(.46,1.06),(.47,1.15),(.39,1.18)],sides=20)
    for j in range(12):
        for s in [-1,1]:
            pts=[]
            for i in range(14):t=i/13;z=.20+t*.82;rr=.13+t*.30;a=j*2*pi/12+s*t*2.3;pts.append((rr*cos(a),rr*sin(a),z))
            g.tube('toast',pts,.012,4)
    g.torus('dough',(0,0,1.10),.445,.04,24,5)
    g.ell('flavor',(0,0,1.22),(.43,.43,.19),seg=20,rings=6)
    swirl(g,(0,0,1.24),.36,1.03,'flavor',3.4)

def build_bingsu(g,style):
    y=bowl(g,r=.86,h=.56);rng=random.Random(78)
    v=[];f=[];n=40;m=13
    for j in range(m+1):
        t=j/m;rr=.79*t
        for i in range(n):
            a=i*2*pi/n;r=rr*(1+.025*sin(a*9+j));z=y+.56*sqrt(max(0,1-t*t))+.012*sin(a*17+j*5)*sin(pi*t)
            v.append((r*cos(a),r*sin(a),z))
    for j in range(m):
        for i in range(n):f.append((j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i))
    g.add(v,f,'rice',smooth=True)
    v=[];f=[]
    for j in range(9):
        t=j/8
        for i in range(n):
            a=i*2*pi/n;r=(.66+.04*sin(a*7)+.025*sin(a*13))*t;z=y+.56*sqrt(max(0,1-(r/.79)**2))+.015
            v.append((r*cos(a),r*sin(a),z))
    for j in range(8):
        for i in range(n):f.append((j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i))
    g.add(v,f,'flavor',smooth=True)
    for i in range(150):
        a=rng.random()*2*pi;rr=rng.uniform(.64,.785);z=y+.56*sqrt(max(0,1-(rr/.79)**2))
        g.organic('rice',(rr*cos(a),rr*sin(a),z),(.022,.018,.008),i,rough=.12,exponent=.4,seg=5,rings=3,rot=(.5*cos(a),.5*sin(a),a))
    for i in range(28):
        a=i*2.399;rr=.22+.42*((i*13%23)/23);z=y+.56*sqrt(max(0,1-(rr/.79)**2))+.068
        ss=.060+.025*(i%4)/3;g.organic('flavordark' if i%3 else 'flavor',(rr*cos(a),rr*sin(a),z),(ss,ss*.9,ss*.83),i,rough=.035,exponent=.28,seg=12,rings=7,rot=(.07,.10,a))
    swirl(g,(0,0,y+.64),.21,.38,'cream',2.4)
    for i in range(2):leaf(g,(0,.015,y+.925),.19,.085,i*1.5)

def build_pasta(g,style):
    y=bowl(g,handles=True,h=.57)
    soup(g,'flavordark',y)
    noodles(g,y+.04,'flavor',n=23,thick=.036)
    for x,yy in [(-.36,-.1),(.34,.10),(.08,.39)]:mushroom(g,(x,yy,y+.12),.19)
    for i in range(4):leaf(g,(-.06,0,y+.31),.28,.13,i*1.5)
    sesame(g,y+.22,.66,16)

def build_drink(g,style):
    if style=='hotcoffee':
        mug(g)
        for i in range(24):a=i*2*pi/24;g.torus('dough',(.42*cos(a),.42*sin(a),.915),.018,.008,6,4)
    else:
        col='coffee' if style=='icedcoffee' else ('milktea' if style=='milktea' else 'teaamber')
        glass(g,col,True)
        if style=='milktea':
            for i in range(16):a=i*2.4;rr=.4*(i%5)/5;g.ell('darkbrown',(rr*cos(a),rr*sin(a),.15+(i%3)*.04),(.065,.065,.065),seg=8,rings=5)
        if style=='icedtea':citrus(g,(-.18,0,1.56),.3,'orange',(.6,0,0));leaf(g,(0,.05,1.56),.35,.16)

def build_smoothie(g,style):
    glass(g,'flavor');g.ell('flavor',(0,0,1.43),(.50,.50,.17),seg=20,rings=6)
    swirl(g,(0,0,1.52),.30,.30,'flavor',1.9)
    if style in ['strawberry','blueberry']:
        for i in range(3):fruitberry(g,(-.18+i*.13,-.05,1.70),.12,style)
    elif style=='mango':
        for i in range(4):g.box('yellow',(-.20+(i%2)*.23,-.08+(i//2)*.2,1.72),(.20,.20,.21),rot=(0,.1,i),bevel=.02)
    elif style=='kiwi':
        for x in [-.13,.13]:
            k=g.mark();g.lathe('brown',[(.22,0),(.22,.035)],sides=20)
            g.lathe('lightgreen',[(.201,.036),(.201,.053)],sides=20);g.ell('cream',(0,0,.056),(.052,.069,.007),seg=12,rings=3)
            for i in range(15):a=i*2*pi/15;g.ell('black',(.112*cos(a),.112*sin(a),.059),(.009,.014,.004),seg=5,rings=3,rot=(0,0,a))
            g.transform(k,(x,0,1.71),rot=(pi/3,0,0))
    elif style=='cookies':
        g.ell('darkbrown',(-.12,.02,1.78),(.19,.055,.23),seg=16,rings=6)
        for i in range(10):a=i*2.4;g.box('chocolate',(.36*cos(a),.36*sin(a),1.54),(.045,.04,.04),rot=(0,0,a),bevel=.006)
    else:swirl(g,(0,0,1.61),.21,.28);leaf(g,(0,0,1.86),.23,.10)

def build_tea(g,style):
    plate(g,'wood',.77);k=g.mark();mug(g,'glass',{'yuja':'teagold','chamomile':'teagold','earlgrey':'teaamber','green':'teagreen','black':'teaamber'}[style]);g.transform(k,(0,0,.13))
    if style=='yuja':
        for a in [0,2.3,4.5]:citrus(g,(.19*cos(a),.19*sin(a),1.055),.23)
    elif style=='chamomile':
        for a in [0,2.1,4.3]:
            x=.25*cos(a);y=.25*sin(a)
            for b in range(6):g.ell('cream',(x+.05*cos(b*pi/3),y+.05*sin(b*pi/3),1.06),(.045,.025,.012),seg=6,rings=3,rot=(0,0,b*pi/3))
            g.ell('yellow',(x,y,1.065),(.024,.024,.015),seg=8,rings=3)
    elif style=='earlgrey':
        g.box('paper',(.10,.06,1.055),(.21,.18,.035),rot=(0,0,.2),bevel=.008)
        g.tube('cream',[(.12,.06,1.08),(.44,-.16,1.17),(.55,-.20,.85)],.008,4)
        g.box('label',(.55,-.2,.8),(.13,.02,.18),bevel=.006)
    elif style=='green':leaf(g,(0,0,1.06),.25,.12)

def build_ade(g,style):
    col={'lemon':'lemonade','grapefruit':'pinkade','grape':'grapeade'}[style]
    glass(g,col,True)
    # Fruit sits at the rim or within the top of the drink, never pasted onto the outside wall.
    if style=='grape':
        for i in range(5):
            a=i*2.4;g.organic('lightgreen',(.21*cos(a),.20*sin(a),1.47),(.082,.075,.09),i,rough=.03,seg=14,rings=7)
    else:
        citrus(g,(-.29,-.09,1.47),.245,'yellow' if style=='lemon' else 'pink',(pi/3,0,-.20))
        citrus(g,(.15,.04,1.445),.18,'yellow' if style=='lemon' else 'pink',(.14,0,.20))
    leaf(g,(-.06,.01,1.51),.26,.13)

def build_stew(g,style):
    h=.61 if style!='egg' else .55;y=bowl(g,h=h,handles=style in ['kimchi','miso','chicken','green','tomyum'])
    col={'kimchi':'red','miso':'broth','chicken':'cream','pork':'cream','egg':'steamedegg','mushroom':'cream','mala':'redsauce','mapo':'red','tomyum':'red','green':'lightgreen'}[style]
    soup(g,col,y);rng=random.Random(44)
    if style=='egg':
        g.organic('steamedegg',(0,0,y-.03),(.84,.84,.27),seed=11,rough=.06,seg=28,rings=12)
        for i in range(28):
            a=i*2.4;rr=.71*sqrt(i/28);zz=y-.03+.27*sqrt(max(0,1-(rr/.84)**2));g.organic('steamedegg',(rr*cos(a),rr*sin(a),zz-.020),(.09,.085,.045),seed=i,rough=.10,seg=12,rings=6)
        scallions(g,y+.20,20,.58);return
    if style=='chicken':
        g.organic('fat',(0,.06,y+.08),(.33,.46,.24),seed=10,rough=.035,seg=24,rings=11)
        for s in [-1,1]:
            pts=smooth_path([(s*.23,-.10,y+.12),(s*.35,-.29,y+.12),(s*.32,-.49,y+.26)],6)
            g.tube('fat',pts,sides=12,radii=[.15*(1-i/(len(pts)-1)*.65) for i in range(len(pts))])
            g.ell('cream',(s*.32,-.50,y+.27),(.07,.075,.075),seg=12,rings=6)
            g.tube('fat',smooth_path([(s*.28,.16,y+.11),(s*.42,.24,y+.05),(s*.40,.02,y+.02)],6),.045,8)
        g.tube('cream',[(0,.35,y+.28),(0,.10,y+.325),(0,-.17,y+.29)],.008,4)
        root=[(.43,.29,y+.06),(.52,.05,y+.10),(.49,-.25,y+.10),(.55,-.46,y+.05)]
        g.tube('dough',smooth_path(root,5),.044,8,radii=None)
        for i in range(3):g.tube('dough',[(.52,-.25-i*.04,y+.09),(.61+i*.03,-.43-i*.03,y+.045)],.011,5)
        for a in [.2,2.6,4.0]:g.organic('darkbrown',(.68*cos(a),.62*sin(a),y+.027),(.10,.13,.053),seed=7,rough=.1,seg=12,rings=6)
        scallions(g,y+.34,7,.21);return
    if style in ['kimchi','miso','mapo','mala']:
        count=14 if style=='mapo' else 5
        for i in range(count):
            a=i*2.4;rr=.68*sqrt((i+.7)/count);tofu(g,(rr*cos(a),rr*sin(a),y+.074),.24 if style!='mapo' else .21)
    if style in ['miso','mushroom','mala','tomyum','green']:
        for i in range(6 if style=='mushroom' else 4):
            a=i*2.4;rr=.34+.27*((i*3%5)/5);mushroom(g,(rr*cos(a),rr*sin(a),y+.018),.23)
    if style in ['miso','green']:
        for a in [.8,2.9,5.0]:veg_slice(g,(.58*cos(a),.58*sin(a),y+.04),.18,'cucumber',(.12,0,a))
    if style in ['pork','kimchi','mala','green']:
        for i in range(12 if style=='pork' else 6):
            a=i*2.4;rr=.18+.49*((i*3%7)/7)
            if style=='green':g.organic('fat',(rr*cos(a),rr*sin(a),y+.045),(.11,.13,.10),i,rough=.10,exponent=.50,seg=12,rings=7)
            else:folded_meat(g,(rr*cos(a),rr*sin(a),y+.057),.35,.25,a,'pork')
    if style=='kimchi':
        for i in range(7):
            a=i*2.4;rr=.57*sqrt((i+.5)/7);k=g.mark()
            lettuce(g,(0,0,0),.39,.30,0,'tomato',i)
            for j in [-1,0,1]:g.tube('orange',[(j*.035,.02,.02),(j*.05,.18,.09),(j*.025,.33,.04)],.014,5)
            g.transform(k,(rr*cos(a),rr*sin(a),y+.03),rot=(.18,-.06,a))
    if style in ['tomyum','mala']:
        for a in [.2,2.3,4.4]:shrimp(g,(.38*cos(a),.38*sin(a),y+.07),1.13 if style=='tomyum' else .82,rot=a)
    if style=='mala':
        for a in [.5,2.7,4.8]:
            lettuce(g,(.47*cos(a),.47*sin(a),y+.04),.44,.28,a,'green',2)
            g.tube('cream',[(.43*cos(a),.43*sin(a),y+.07),(.67*cos(a),.67*sin(a),y+.13)],.026,7)
    if style in ['mala','mapo']:
        for i in range(42):
            a=i*2.4;rr=.78*sqrt(rng.random());g.organic('meat',(rr*cos(a),rr*sin(a),y+.025),(.025,.031,.021),i,rough=.20,seg=6,rings=4)
    if style=='green':
        for i in range(5):a=i*2.4;leaf(g,(.48*cos(a),.48*sin(a),y+.13),.27,.14,a)
    if style in ['tomyum','mala','green']:
        for i in range(3):a=i*2.4;g.tube('redsauce',[(.63*cos(a),.63*sin(a),y+.07),(.48*cos(a),.48*sin(a),y+.12),(.34*cos(a),.34*sin(a),y+.09)],.024,6)
    scallions(g,y+.16,11,.60)
    scatter_surface(g,'brown' if col=='cream' else 'redsauce',32,22,r=.79,size=.007)

def build_noodles(g,style):
    dry=style in ['soba','mie'];mat='metal' if style in ['cold','spicycold','pho'] else ('whiteplate' if style=='black' else 'ceramic')
    y=plate(g,mat) if dry else bowl(g,mat,h=.62)
    color={'spicycold':'red','black':'cream','mie':'toast','cold':'wood','soba':'wood'}.get(style,'cream')
    if not dry and style!='black':soup(g,'redsauce' if style in ['seafood','spicycold'] else 'broth',y)
    if style=='soba':
        for i in range(12):g.box('paper',(-.74+i*.135,0,y+.02),(.07,1.40,.02),bevel=.005)
    noodles(g,y+.04,color,r=.86 if style=='black' else .75,n=32 if style=='black' else 22,thick=.057 if style=='udon' else .028)
    if style in ['cold','spicycold','ramen']:
        halfegg(g,(.16,.20,y+.22))
        for i in range(6):g.box('lightgreen',(-.30+i*.085,-.23,y+.17),(.04,.38,.035),rot=(0,0,.3),bevel=.008)
    if style=='ramen':
        g.ell('pork',(-.39,-.13,y+.14),(.28,.30,.055),seg=16,rings=6)
        g.torus('fat',(-.39,-.13,y+.19),.16,.017,16,5)
        g.box('nori',(-.34,.53,y+.30),(.34,.035,.43),rot=(.30,0,.20),bevel=.01)
    if style in ['pho','mie']:
        for i in range(4):folded_meat(g,(-.40+i*.16,-.13,y+.18),.32,.22,.2,'meat')
        if style=='mie':friedegg(g,(.18,.10,y+.22),.40)
        else:citrus(g,(.4,.26,y+.18),.28,'lightgreen',(.12,0,0));greens(g,y+.18,.65,4)
    if style=='seafood':
        for a in [0,2.2,4.2]:shrimp(g,(.36*cos(a),.36*sin(a),y+.13),.75,rot=a)
        for a in [1,3.8]:g.ell('black',(.5*cos(a),.5*sin(a),y+.18),(.14,.27,.14),seg=12,rings=6,rot=(0,.3,a))
    if style=='black':
        g.organic('darkbrown',(0,0,y+.13),(.65,.64,.087),seed=17,rough=.085,seg=28,rings=9)
        for i in range(32):a=i*2.4;rr=.59*sqrt((i+.5)/32);g.organic('darkbrown',(rr*cos(a),rr*sin(a),y+.17+.035*(1-rr)),(.065,.059,.025),seed=i,rough=.13,exponent=.45,seg=10,rings=5,rot=(0,0,a))
        for i in range(6):g.box('lightgreen',(-.18+i*.065,-.04,y+.30),(.035,.40,.03),rot=(0,0,-.4),bevel=.005)
    elif style=='soba':
        for i in range(7):g.box('nori',(-.22+i*.07,0,y+.22),(.025,.40,.012),rot=(0,0,i*.35),bevel=.004)
        k=g.mark();bowl(g,r=.34,h=.40);soup(g,'darkbrown',.33,.30);g.transform(k,(.88,.65,.02))
    elif style=='udon':
        g.ell('pink',(.35,-.2,y+.18),(.23,.16,.08),seg=14,rings=5);g.ell('cream',(.35,-.2,y+.23),(.17,.12,.035),seg=14,rings=5)
    scallions(g,y+.21,8,.40)

def build_risotto(g,style):
    y=bowl(g,h=.53);grains(g,(0,0,y-.08),.84,110,'cream',.20)
    for a in [0,2,4]:mushroom(g,(.48*cos(a),.48*sin(a),y+.10),.23)
    scallions(g,y+.24,12,.68)

def build_bibimbap(g,style):
    y=bowl(g,h=.68);g.box('wood',(0,0,.01),(1.90,1.90,.10),bevel=.16)
    rng=random.Random(136)
    def rice_z(x,yy):return y-.12+.18*sqrt(max(0,1-(x*x+yy*yy)/.84**2))
    g.organic('rice',(0,0,y-.12),(.84,.84,.18),seed=18,rough=.025,seg=32,rings=10)
    for j in range(210):
        a=rng.random()*2*pi;r=.815*sqrt(rng.random());x=r*cos(a);yy=r*sin(a)
        g.ell('rice',(x,yy,rice_z(x,yy)),(.040,.017,.015),seg=6,rings=4,rot=(0,rng.uniform(-.3,.3),rng.random()*pi))
    # Low, overlapping irregular piles: each ingredient has its own cut and silhouette.
    for q,mat in enumerate(['carrot','spinach','brown','sprout','beef']):
        a=.10+q*2*pi/5;cx=.52*cos(a);cy=.52*sin(a)
        count=49 if mat!='beef' else 52
        for j in range(count):
            u=rng.uniform(-.255,.255);v=rng.uniform(-.20,.18)
            x=cx+u*cos(a+pi/2)+v*cos(a);yy=cy+u*sin(a+pi/2)+v*sin(a)
            z=rice_z(x,yy)+.018+.065*(1-abs(u)/.30)*rng.random()
            angle=a+pi/2+rng.uniform(-.95,.95);length=rng.uniform(.16,.35)
            if mat=='beef':
                if j%3:
                    k=g.mark();folded_meat(g,(0,0,0),rng.uniform(.07,.13),rng.uniform(.035,.066),0,mat)
                    g.transform(k,(x,yy,z),scale=(1,1,.44),rot=(.07,-.08,angle))
                else:g.organic(mat,(x,yy,z),(.033,.023,.017),seed=j,rough=.26,exponent=.68,seg=7,rings=4,rot=(.12,-.1,angle))
                continue
            if mat=='spinach':
                k=g.mark();folded_meat(g,(0,0,0),length,.09,0,'spinach')
                g.transform(k,(x,yy,z),scale=(1,1,.55),rot=(0,.08,angle))
                continue
            bend=rng.uniform(-.07,.07);pts=[]
            for k in range(9):
                t=k/8;along=(t-.5)*length;across=bend*sin(t*pi)
                px=x+along*cos(angle)-across*sin(angle);py=yy+along*sin(angle)+across*cos(angle)
                pts.append((px,py,rice_z(px,py)+.015+(z-rice_z(x,yy))*sin(pi*t)+.014*sin(t*pi*2+j)))
            start=g.mark();g.tube(mat,pts,.013 if mat!='carrot' else .017,5)
            if mat=='carrot':
                g.v[start:]=[(px,py,pz*.65+z*.35) for px,py,pz in g.v[start:]]
            if mat=='sprout':g.ell('egg',pts[-1],(.028,.018,.014),seg=7,rings=4,rot=(0,0,angle))
    g.organic('redsauce',(.20,-.22,y+.047),(.19,.16,.037),seed=4,rough=.14,seg=20,rings=7)
    # Thin wavy egg white drapes over the topping bed; the yolk stays rounded.
    verts=[];faces=[];n=48;center=(-.035,.005,y+.123)
    for radius,dz in [(0,0),(.33,0),(.68,-.012),(1,-.035)]:
        for i in range(n):
            a=i*2*pi/n;rr=.37*radius*(1+.095*sin(3*a+.5)+.045*sin(7*a))
            verts.append((center[0]+rr*cos(a),center[1]+rr*.88*sin(a),center[2]+dz+.008*radius*sin(5*a)))
    for ring in range(3):
        for i in range(n):faces.append((ring*n+i,ring*n+(i+1)%n,(ring+1)*n+(i+1)%n,(ring+1)*n+i))
    top=len(verts);verts += [(x,yy,z-.014) for x,yy,z in verts]
    faces += [tuple(top+i for i in reversed(f)) for f in list(faces)]
    for i in range(n):a=3*n+i;b=3*n+(i+1)%n;faces.append((a,b,top+b,top+a))
    g.add(verts,faces,'eggwhite',smooth=True)
    g.organic('yolk',(-.07,-.005,y+.145),(.145,.139,.092),seed=3,rough=.025,seg=24,rings=12)
    scatter_surface(g,'cream',24,36,r=.76,size=.011,exclude=('eggwhite','yolk','rice'))

def build_porkplate(g,style):
    y=plate(g,'whiteplate' if style=='bossam' else 'ceramic');greens(g,y,.89,9)
    if style=='dongpo':
        for i,(x,yy) in enumerate([(-.35,-.34),(.30,-.34),(-.34,.26),(.31,.27)]):
            k=g.mark()
            for j in range(5):
                z=.05+j*.070;mat='fat' if j in [1,3] else 'pork'
                g.organic(mat,(0,0,z),(.235,.23,.054),seed=i,rough=.045,exponent=.30,seg=20,rings=8)
            g.organic('brown',(0,0,.38),(.243,.233,.060),i,rough=.04,exponent=.35,seg=20,rings=7)
            g.transform(k,(x,yy,y+.01),rot=(0,0,(i-1.5)*.10))
        for i in range(8):g.tube('cream',[(-.20+i*.05,-.10,y+.46),(.13+i*.03,.18,y+.46)],.008,4)
        return
    if style=='jokbal':
        g.organic('brown',(.13,.30,y+.19),(.41,.43,.28),seed=9,rough=.08,exponent=.78,seg=22,rings=10)
        for i in range(5):
            yy=-.48+i*.155;x=-.10+i*.025;k=g.mark()
            g.organic('brown',(0,0,.24),(.48,.073,.27),seed=11,rough=.055,seg=22,rings=10)
            g.organic('fat',(0,-.069,.245),(.416,.009,.215),seed=11,rough=.05,seg=22,rings=9)
            g.organic('pork',(.03,-.079,.255),(.355,.009,.164),seed=13,rough=.07,seg=20,rings=8)
            g.ell('cream',(-.18,-.09,.22),(.069,.006,.066),seg=12,rings=5)
            g.ell('brown',(-.18,-.097,.22),(.025,.003,.021),seg=10,rings=4)
            g.transform(k,(x,yy,y+.015),rot=(.16,0,-.16+i*.015))
        for s in [-1,1]:
            g.organic('brown',(s*.49,-.50,y+.13),(.16,.20,.13),seed=1,rough=.08,seg=14,rings=7)
        sesame(g,y+.51,.52,16);return
    for i in range(6):
        k=g.mark();width=.42+.025*sin(i)
        for j in range(5):
            z=.035+j*.045;mat='fat' if j in [1,3] else ('pork' if style=='bossam' else 'meat')
            g.organic(mat,(0,0,z),(width,.068,.038),seed=8,rough=.025,exponent=.43,seg=18,rings=7)
        g.organic('pork' if style=='bossam' else 'baked',(0,0,.244),(width,.07,.027),seed=8,rough=.03,exponent=.45,seg=18,rings=6)
        if style=='grilled':
            for x in [-.26,-.08,.13,.28]:g.tube('brown',[(x,-.059,.268),(x+.025,.04,.269)],.009,4)
        g.transform(k,(-.07+i*.027,-.43+i*.16,y+.055),rot=(.73,0,-.25))
    for x in [.45,.65]:g.organic('cream',(x,.51,y+.07),(.095,.13,.036),rough=.02,seg=12,rings=5)
    k=g.mark();bowl(g,r=.23,h=.15);soup(g,'red',.14,.20);g.transform(k,(-.61,.49,y))

def build_feet(g,style):
    y=plate(g);greens(g,y,.78,8)
    for i in range(8):
        k=g.mark();g.tube('red',[(0,.25,0),(0,.1,.09),(0,-.04,.08)],.075,7)
        for j in [-1,0,1]:g.tube('red',[(0,0,.08),(j*.1,-.13,.05),(j*.14,-.28,.01)],.041,6)
        a=i*2.399;r=.18+(i%3)*.22;g.transform(k,(r*cos(a),r*sin(a),y+.13+(i%2)*.06),rot=(.12,0,a))
    scallions(g,y+.28);sesame(g,y+.31,.60,20)

def build_ricecakes(g,style):
    y=plate(g);g.organic('redsauce',(0,0,y+.065),(.84,.82,.045),rough=.025,seg=32,rings=7)
    for i in range(17):
        a=i*2.399;r=.15+.55*sqrt(i/17);x=r*cos(a);yy=r*sin(a);k=g.mark()
        pts=[(-.20,0,.105),(-.14,.008,.11),(0,.015,.12),(.15,0,.117),(.20,-.012,.112)]
        g.tube('red',pts,sides=12,radii=[.065,.077,.081,.077,.065])
        g.transform(k,(x,yy,y+.016+(i%3)*.055),rot=(.025,.10,a))
    for a in [0,2.4,4.5]:folded_meat(g,(.47*cos(a),.47*sin(a),y+.115),.28,.20,a,'dough')
    scallions(g,y+.29,12,.58)

def build_gimbap(g,style):
    g.box('whiteplate',(0,0,.08),(2.1,1.0,.13),bevel=.15)
    for i in range(6):
        k=g.mark();g.lathe('nori',[(.28,0),(.286,.07),(.28,.18)],sides=28,smooth=True);g.lathe('rice',[(.244,.182),(.244,.199)],sides=28,smooth=True)
        for j in range(25):
            a=j*2.399;r=.14+.09*((j*7%23)/23);g.ell('rice',(r*cos(a),r*sin(a),.203),(.022,.012,.012),seg=6,rings=3,rot=(0,0,a))
        for q,(x,yy,col) in enumerate([(-.075,-.074,'yellow'),(.085,-.06,'orange'),(-.078,.087,'green'),(.092,.091,'brown'),(.015,.035,'pork')]):
            g.organic(col,(x,yy,.209),(.050,.048,.013),q,rough=.11,exponent=.55,seg=10,rings=5)
        g.transform(k,(-.81+i*.31,.14,.32),rot=(pi/2.9,0,0))

def build_sundae(g,style):
    y=plate(g,'whiteplate');rng=random.Random(89)
    for i in range(11):
        a=i*2.399;r=.20+.50*sqrt(i/11);k=g.mark()
        g.lathe('sausage',[(.20,0),(.219,.03),(.225,.09),(.212,.17),(.196,.183)],sides=22,smooth=True)
        g.organic('sausagecut',(0,0,.174),(.198,.195,.013),i,rough=.025,seg=20,rings=5)
        for j in range(20):
            b=j*2.4;rr=.171*sqrt((j+.5)/20)
            g.ell('pork' if j%4==0 else 'brown',(rr*cos(b),rr*sin(b),.188),(.021,.009,.004),seg=5,rings=3,rot=(0,0,b))
        g.transform(k,(r*cos(a),r*sin(a),y+.02+(i%3)*.018),rot=(.10,.10,a))

def build_pancake(g,style):
    y=plate(g);poly=[((.85+.020*sin(i*2.5))*cos(i*2*pi/40),(.85+.020*sin(i*2.5))*sin(i*2*pi/40)) for i in range(40)]
    baked_piece(g,(0,0,y),poly,.115,'pajeon' if style=='seafood' else 'egg','pajeonedge' if style=='seafood' else 'baked',34)
    if style=='seafood':
        for i in range(27):
            a=i*2.399;r=.71*sqrt(i/27);g.tube('green',[(r*cos(a)-.03,r*sin(a)-.18,y+.133),(r*cos(a)+.016,r*sin(a)+.16,y+.144)],.018,5)
        for a in [0,2,4]:shrimp(g,(.42*cos(a),.42*sin(a),y+.13),.76,rot=a)
        for a in [.8,2.9,5]:g.torus('cream',(.45*cos(a),.45*sin(a),y+.15),.11,.018,16,5)
        scatter_surface(g,'baked',34,23,size=.022)
    else:
        g.organic('brown',(0,0,y+.136),(.80,.79,.055),seed=33,rough=.035,exponent=.85,seg=28,rings=8)
        saucezig(g,(0,0,y+.193),1.24,1.20,'cream',.020);scallions(g,y+.22,18,.69)
        for i in range(22):
            a=i*2.4;r=.65*sqrt(i/22);folded_meat(g,(r*cos(a),r*sin(a),y+.216),.14,.10,a,'paper')

def build_pizza(g,style):
    y=plate(g);r=.90
    for i in range(8):
        a=i*2*pi/8;poly=[(0,0)]+[(r*cos(a+.016+j*(pi/4-.032)/7),r*sin(a+.016+j*(pi/4-.032)/7)) for j in range(8)]
        g.prism('dough',poly,.10,(0,0,y));g.prism('redsauce',[(x*.92,yy*.92) for x,yy in poly],.023,(0,0,y+.10))
        g.prism('egg',[(x*.89,yy*.89) for x,yy in poly],.020,(0,0,y+.126))
        pts=[(r*.94*cos(a+.027+j*(pi/4-.054)/16),r*.94*sin(a+.027+j*(pi/4-.054)/16),y+.125+.009*sin(j*1.4+i)) for j in range(17)]
        g.tube('dough',pts,.073,10)
        aa=a+pi/8;veg_slice(g,(.58*cos(aa),.58*sin(aa),y+.15),.13,'tomato')
        g.torus('black',(.31*cos(aa+.3),.31*sin(aa+.3),y+.168),.060,.023,12,5)
        if i%2:leaf(g,(.24*cos(aa),.24*sin(aa),y+.17),.30,.14,aa)
    rng=random.Random(43)
    for i in range(25):
        a=rng.random()*2*pi;r=.75*sqrt(rng.random())
        g.organic('yellow' if i%3 else 'baked',(r*cos(a),r*sin(a),y+.149),(.022,.030,.006),i,rough=.15,seg=6,rings=3)

def build_steak(g,style):
    y=plate(g);g.box('wood',(0,0,.012),(2.02,1.94,.075),bevel=.12)
    g.organic('brown',(-.24,-.11,y+.17),(.51,.60,.155),seed=41,rough=.11,exponent=.53,seg=26,rings=10)
    g.organic('meat',(-.24,-.11,y+.245),(.46,.55,.09),seed=41,rough=.06,exponent=.68,seg=26,rings=9)
    for a in [.62,-.80]:
        for k in [-2,-1,0,1,2]:
            yy=k*.13;span=.40*sqrt(max(.2,1-(yy/.50)**2));pts=[]
            for i in range(15):
                xx=-span+i*2*span/14;px=xx*cos(a)-yy*sin(a);py=xx*sin(a)+yy*cos(a);z=y+.245+.09*sqrt(max(.1,1-(px/.49)**2-(py/.60)**2))
                pts.append((-.24+px,-.11+py,z+.003))
            g.tube('darkbrown',pts,.009,4)
    for i in range(2):
        k=g.mark();g.organic('brown',(0,0,.11),(.16,.39,.11),seed=3,rough=.07,exponent=.5,seg=18,rings=8)
        g.organic('pork',(.12,0,.12),(.009,.32,.07),seed=3,rough=.04,seg=16,rings=7)
        g.transform(k,(.40+i*.21,-.23+i*.10,y),rot=(0,.20,-.15))
    for i in range(2):
        g.organic('dough',(-.55+i*.34,.57,y+.11),(.16,.21,.11),i,rough=.06,seg=16,rings=7)
    mushroom(g,(.47,.49,y+.04),.22);greens(g,y,.90,3)

def build_burger(g,style):
    y=plate(g,r=.91)
    g.organic('dough',(0,0,y+.11),(.66,.65,.115),seed=24,rough=.025,exponent=.70,seg=28,rings=10)
    g.organic('cream',(0,0,y+.21),(.62,.61,.021),seed=24,rough=.03,seg=24,rings=6)
    greens(g,y+.22,.78,9)
    g.organic('meat',(0,0,y+.36),(.67,.65,.10),seed=33,rough=.085,exponent=.70,seg=32,rings=10)
    rng=random.Random(39)
    for i in range(70):
        a=i*2.399;r=.65+.015*sin(i*3);g.organic('brown',(r*cos(a),r*sin(a),y+.36+.045*sin(i)),(.032,.035,.025),i,rough=.16,seg=6,rings=3)
    for a in [.2,2.3,4.4]:veg_slice(g,(.24*cos(a),.24*sin(a),y+.44),.34,'tomato')
    if style=='cheese':
        v=[(-.69,-.63,y+.40),(.64,-.72,y+.39),(.72,.61,y+.42),(-.64,.71,y+.43),(0,0,y+.51)]
        g.add(v,[(0,1,4),(1,2,4),(2,3,4),(3,0,4)],'yellow',smooth=True)
    greens(g,y+.50,.73,7)
    g.lathe('dough',[(0,y+.56),(.63,y+.56),(.68,y+.60),(.685,y+.66),(.65,y+.78),(.52,y+.92),(.32,y+1.00),(0,y+1.035)],sides=32,smooth=True)
    g.torus('baked',(0,0,y+.595),.644,.023,32,5)
    for i in range(48):
        a=rng.random()*2*pi;r=.62*sqrt(rng.random());z=surface_height(g,r*cos(a),r*sin(a),y+1)
        g.ell('cream',(r*cos(a),r*sin(a),z+.007),(.030,.012,.008),seg=6,rings=3,rot=(0,0,a))

def build_hotdog(g,style):
    y=plate(g,r=.9,oval=1.15)
    if style=='bun':
        for x in [-.20,.20]:
            g.organic('dough',(x,0,y+.21),(.25,.75,.23),seed=6,rough=.04,seg=24,rings=12)
            g.organic('cream',(x*.73,-.005,y+.255),(.09,.64,.16),seed=6,rough=.03,seg=20,rings=8)
        g.organic('red',(0,0,y+.35),(.165,.81,.145),seed=4,rough=.025,seg=24,rings=12)
        saucezig(g,(0,0,y+.50),.23,1.29,'yellow',.024)
        for i in range(13):
            yy=-.51+i*.081;x=.055*sin(i*3);z=surface_height(g,x,yy,y+.46)
            g.organic('lightgreen',(x,yy,z+.012),(.024,.026,.014),i,rough=.08,exponent=.4,seg=7,rings=4)
    else:
        g.organic('fried',(0,0,y+.21),(.30,.69,.22),seed=8,rough=.08,exponent=.75,seg=24,rings=12)
        crumbs(g,(0,0,y+.21),(.30,.70,.22),220,73)
        g.tube('wood',[(0,.58,y+.19),(0,1.10,y+.19)],.026,7)
        saucezig(g,(0,0,y+.44),.38,1.04,'red',.022)
        for i in range(4):
            x=(i-1.5)*.047;g.tube('cream',[(x,-.51,y+.22),(x+.008,-.67,y+.15),(x-.013,-.83,y+.095)],.037,8)
        g.organic('cream',(0,-.76,y+.09),(.16,.15,.073),seed=4,rough=.035,seg=16,rings=6)

def build_sandwich(g,style):
    y=plate(g)
    for offset,a in [(-.36,-.22),(.37,.17)]:
        k=g.mark();tri=[(-.42,-.38),(.42,-.38),(-.42,.46)]
        baked_piece(g,(0,0,0),tri,.085,'cream','dough',5)
        for i in range(4):lettuce(g,(-.32+i*.17,-.35,.095),.31,.28,.05,'lettuce',i)
        g.prism('pork',tri,.035,(0,0,.18))
        for xx,yy in [(-.22,-.15),(-.06,-.23)]:veg_slice(g,(xx,yy,.225),.18,'tomato')
        g.prism('yellow',[(x*.94,yy*.94) for x,yy in tri],.024,(0,0,.28))
        baked_piece(g,(0,0,.32),tri,.085,'cream','dough',8)
        for i in range(12):
            u=(i*.73)%1;v=(i*.31)%1
            if u+v>1:u,v=1-u,1-v
            xx=-.42+u*.84;yy=-.38+v*.84
            g.ell('dough',(xx,yy,.428),(.009,.006,.002),seg=5,rings=3)
        g.tube('wood',[(-.12,-.10,.35),(-.12,-.10,.70)],.009,5)
        g.transform(k,(offset,0,y+.02),rot=(0,0,a))

def build_salad(g,style):
    y=bowl(g,'wood',h=.47);greens(g,y-.035,.96,17)
    for i in range(12):
        a=i*2.4;lettuce(g,(.25*cos(a),.25*sin(a),y+.01),.38,.30,a,'lettuce' if i%2 else 'green',i)
    for i in range(6):
        a=i*2.4;veg_slice(g,(.55*cos(a),.54*sin(a),y+.17),.18,'cucumber',(.08*cos(a),.08*sin(a),a))
    for a in [.2,2.4,4.5]:veg_slice(g,(.44*cos(a),.44*sin(a),y+.22),.19,'tomato')
    for i in range(7):
        a=i*2.4;g.torus('purple',(.63*cos(a),.6*sin(a),y+.20),.16,.013,18,5,rot=(.08,0,a))
    for i in range(10):
        a=i*2.4;g.organic('dough',(.70*cos(a),.60*sin(a),y+.20),(.057,.055,.052),i,rough=.10,exponent=.3,seg=10,rings=5,rot=(.1,.1,a))

def fries(g,y,at=(0,0),n=20):
    rng=random.Random(56)
    for i in range(n):x=at[0]+rng.uniform(-.42,.42);yy=at[1]+rng.uniform(-.4,.4);g.box('friedlight',(x,yy,y+.10+rng.random()*.13),(.095,.48+rng.random()*.22,.085),rot=(.1,rng.uniform(-.2,.2),rng.uniform(-1,1)),bevel=.015)
def build_fries(g,style):
    bowl(g,h=.40,r=.75)
    for i in range(6):a=i*pi/3;g.prism('paper',[(-.3,-.3),(.3,-.3),(.2,.3),(-.2,.3)],.008,(.50*cos(a),.5*sin(a),.35),rot=(.3,0,a))
    fries(g,.40,n=28)

def build_ribs(g,style):
    y=plate(g);shredded(g,(.08,.57,y+.04),.33,55)
    for i in range(7):
        x=-.57+i*.18;k=g.mark()
        g.organic('brown',(0,-.12,.17),(.118,.48,.145),i+5,rough=.11,exponent=.58,seg=18,rings=9,rot=(.08,0,0))
        g.tube('fat',[(0,-.41,.135),(.025,-.64,.11)],.047,8)
        g.organic('cream',(.025,-.64,.11),(.058,.045,.051),rough=.04,seg=10,rings=5)
        for j in range(4):
            yy=-.38+j*.17;g.tube('darkbrown',[(-.07,yy,.27),(0,yy+.035,.312),(.08,yy+.03,.27)],.007,4)
        g.transform(k,(x,0,y+.015),rot=(0,.06,.10*sin(i*.7)))
    for i in range(2):g.organic('dough',(-.62+i*.25,.49,y+.10),(.115,.22,.1),i,rough=.04,seg=14,rings=7)
    scallions(g,y+.37,15,.6)

def build_fried(g,style):
    y=bowl(g,'whiteplate',h=.62) if style=='tendon' else plate(g)
    if style=='tendon':grains(g,(0,0,y-.1),.82,95,height=.15)
    if style=='fish':
        fries(g,y,at=(.39,.15),n=18)
        for i,x in enumerate([-.45,-.12]):
            g.organic('fried',(x,-.13,y+.17),(.235,.64,.17),i+20,rough=.14,exponent=.80,seg=22,rings=10,rot=(0,0,.13))
            crumbs(g,(x,-.13,y+.17),(.235,.64,.17),110,i+26)
        k=g.mark();bowl(g,'whiteplate',r=.20,h=.16);soup(g,'cream',.146,.178);g.transform(k,(.53,-.42,y+.02))
    else:
        if style!='tendon':shredded(g,(0,.49,y+.04),.48,90)
        for j,x in enumerate([-.37,.055,.45]):
            k=g.mark()
            g.organic('fried',(0,0,.14),(.14,.56,.14),j+3,rough=.14,exponent=.85,seg=18,rings=9)
            crumbs(g,(0,0,.14),(.144,.57,.142),110,j+8)
            for ss in [-1,1]:
                g.organic('red',(ss*.048,.63,.16),(.051,.145,.027),j,rough=.06,seg=10,rings=5,rot=(.10,0,ss*.23))
                g.tube('salmon',[(ss*.023,.54,.18),(ss*.054,.72,.19)],.006,4)
            g.transform(k,(x,-.07,y+.05),rot=(.12,0,-.20))
    if style!='tendon':citrus(g,(-.56,-.52,y+.07),.22)

def build_cutlet(g,style):
    y=plate(g);shredded(g,(0,.48,y+.07),.45,44)
    for x in [.46,.65]:veg_slice(g,(x,.38,y+.04),.19,'cucumber',(.20,0,.15))
    veg_slice(g,(.70,.07,y+.03),.19,'tomato')
    for i in range(6):
        x=-.60+i*.24;ly=.42*sqrt(max(.2,1-(x/.81)**2));k=g.mark()
        g.organic('fried',(0,0,.16),(.108,ly,.16),seed=i+40,rough=.11,exponent=.42,seg=16,rings=8)
        g.organic('pork',(.109,-.015,.16),(.006,ly*.86,.105),seed=i+40,rough=.06,exponent=.56,seg=14,rings=7)
        if style=='cheese':g.organic('cream',(.117,-.015,.15),(.005,ly*.70,.059),seed=i+40,rough=.04,exponent=.75,seg=14,rings=6)
        crumbs(g,(0,0,.165),(.108,ly,.155),48,i+30)
        g.transform(k,(x,-.13,y+.01),rot=(0,-.06,.04*(i-2)))
    if style=='cheese':
        for i in range(4):
            x=-.19+i*.125;g.tube('cream',[(x,-.31,y+.22),(x+.02,-.50,y+.12),(x-.025,-.57,y+.07)],.038,8)
        g.organic('cream',(0,-.53,y+.048),(.27,.18,.034),rough=.03,seg=20,rings=6)
    if style=='yuringi':
        scallions(g,y+.35,24,.58);sesame(g,y+.34,.5,20)
        for i in range(7):a=i*2.4;g.torus('red',(.42*cos(a),-.1+.31*sin(a),y+.35),.035,.010,10,4)

def build_stirfry(g,style):
    y=plate(g);rng=random.Random(31)
    if style in ['pork','mala']:greens(g,y,.87,6)
    for i in range(25 if style!='shrimp' else 16):
        a=i*2.399+rng.uniform(-.12,.12);r=.70*sqrt((i+.5)/25);x=r*cos(a);yy=r*sin(a);z=y+.12+.15*(1-r)
        if style=='shrimp':shrimp(g,(x,yy,z),.82,rot=a);continue
        col={'pork':'red','sweetpork':'fried','flatpork':'friedlight','mala':'meat','chicken':'red'}[style]
        if style=='pork':folded_meat(g,(x,yy,z),.38,.28,a,col)
        elif style=='flatpork':
            g.organic(col,(x,yy,z),(.24,.16,.065),i,rough=.16,exponent=.68,seg=14,rings=6,rot=(.15,.04,a))
            crumbs(g,(x,yy,z),(.23,.15,.064),12,i)
        else:
            size=(.13,.18,.12) if style=='sweetpork' else (.14,.12,.115)
            g.organic(col,(x,yy,z),size,i+8,rough=.22,exponent=.70,seg=12,rings=7,rot=(.2,.1,a))
            if style in ['sweetpork','chicken']:crumbs(g,(x,yy,z),size,9,i)
        if i%5==0:
            g.organic('orange',(x,yy,z+.10),(.045,.10,.026),i,rough=.06,exponent=.3,seg=10,rings=5,rot=(0,.1,a))
    for i,a in enumerate([.2,2.3,4.3]):
        k=g.mark();g.tube('cream',[(.17*cos(t),.11*sin(t),0) for t in [j*pi/14 for j in range(15)]],.028,6);g.transform(k,(.51*cos(a),.51*sin(a),y+.31),rot=(.08,.17,a))
    if style in ['mala','chicken']:
        for i in range(9):
            a=i*2.4;g.tube('redsauce',[(.63*cos(a),.60*sin(a),y+.25),(.57*cos(a),.44*sin(a),y+.34),(.46*cos(a),.35*sin(a),y+.33)],.028,6,radii=[.01,.034,.022])
    if style=='mala':
        for a in [0,2,4]:shrimp(g,(.5*cos(a),.5*sin(a),y+.23),.75,rot=a);mushroom(g,(.3*cos(a),.3*sin(a),y+.20),.24)
    scallions(g,y+.39,16,.58);sesame(g,y+.36,.57,18)

def build_sushi(g,style):
    if style in ['salmon','tuna','shrimp','egg']:
        g.box('ceramic',(0,0,.05),(1.83,1.40,.11),bevel=.14)
        rng=random.Random(91)
        for piece,x in enumerate([-.38,.34]):
            cy=.015 if piece==0 else -.055
            g.organic('rice',(x,cy,.276),(.229,.366,.164),seed=8+piece,rough=.035,exponent=.76,seg=26,rings=13)
            # Exposed grains follow the compact oval rice mound, including its sides.
            for j in range(190):
                a=j*2.39996;t=.22+1.97*(j+.5)/190
                nx=sin(t)*cos(a);ny=sin(t)*sin(a);nz=cos(t)
                px=x+.231*math.copysign(abs(nx)**.76,nx);py=cy+.368*math.copysign(abs(ny)**.76,ny);pz=.276+.165*math.copysign(abs(nz)**.76,nz)
                normal=Vector((nx/.229,ny/.366,nz/.164)).normalized()
                rot=normal.to_track_quat('Z','Y').to_euler();rot.z+=rng.uniform(-.8,.8)
                g.ell('rice',(px,py,pz),(.018,.037,.014),seg=7,rings=4,rot=rot)
            if style=='egg':
                g.organic('steamedegg',(x,cy,.464),(.254,.393,.075),seed=7+piece,rough=.018,exponent=.24,seg=36,rings=16)
                # A broad, thin seaweed belt conforms to the egg and rice.
                profile=[(-.220,.175),(-.250,.27),(-.261,.45),(-.260,.496)]
                for j in range(25):
                    px=-.251+j*.502/24;pz=.464+.075*max(0,1-(abs(px)/.254)**(2/.24))**(.24/2)+.007
                    profile.append((px,pz))
                profile += [(.260,.496),(.261,.45),(.250,.27),(.220,.175)]
                vv=[(x+px,cy+dy+.01*sin(px*11),pz) for px,pz in profile for dy in [-.068,.068]]
                ff=[(i*2,i*2+1,i*2+3,i*2+2) for i in range(len(profile)-1)]
                g.add(vv,ff,'nori',smooth=True)
                continue
            # One continuous, closed slice with tapered ends and edges that fold down.
            # Integrated color texture provides marbling instead of raised white rods.
            v=[];f=[];rows=32;cols=20
            for side in [1,-1]:
                for j in range(rows+1):
                    t=j/rows*2-1;yy=t*.435
                    width=.260*max(.00005,1-abs(t)**6)**.48
                    if style=='shrimp':width*=1-.24*(t+1)/2
                    for i in range(cols+1):
                        u=i/cols*2-1
                        xx=u*width*(1+.015*sin(t*19+piece)+.008*sin(t*37))+.007*sin(t*4+piece)
                        z=.466-.085*abs(u)**2.2*(1-.88*abs(t)**4)-.052*abs(t)**5+.006*sin(t*7+u*2)
                        if style=='shrimp':z+=.012*cos((t+1)*6*pi)*(1-u*u)-.012*math.exp(-u*u*35)
                        thick=.022*(.65+.35*(1-u*u))
                        v.append((x+xx,cy+yy,z+side*thick/2))
            layer=(rows+1)*(cols+1)
            for side in range(2):
                for j in range(rows):
                    for i in range(cols):
                        a=side*layer+j*(cols+1)+i;face=(a,a+1,a+cols+2,a+cols+1)
                        f.append(face if side==0 else tuple(reversed(face)))
            edge=list(range(cols+1))+[j*(cols+1)+cols for j in range(1,rows+1)]+[rows*(cols+1)+i for i in range(cols-1,-1,-1)]+[j*(cols+1) for j in range(rows-1,0,-1)]
            for a,b in zip(edge,edge[1:]+edge[:1]):f.append((a,b,b+layer,a+layer))
            g.add(v,f,'sushi'+style,smooth=True)
            if style=='shrimp':
                # Thin tail fan with a natural split, attached to the tapered body.
                for side in [-1,1]:
                    k=g.mark()
                    folded_meat(g,(0,0,0),.235,.17,0,'shrimptail')
                    g.transform(k,(x+side*.064,cy+.445,.402),scale=(1,1,.42),rot=(.04,0,-side*.33))
                    g.tube('salmon',[(x+side*.025,cy+.37,.414),(x+side*.08,cy+.475,.415),(x+side*.105,cy+.535,.405)],.004,4)
        g.organic('lightgreen',(.73,-.48,.157),(.095,.079,.055),seed=26,rough=.18,seg=12,rings=6)
        for i in range(4):
            k=g.mark();folded_meat(g,(0,0,0),.19,.14,0,'pink')
            g.transform(k,(.72,.24+i*.073,.144+i*.009),scale=(1,1,.50),rot=(0,.08,-.45+i*.45))
        return
    raise ValueError('Unknown sushi style: '+style)

def build_takoyaki(g,style):
    g.box('wood',(0,0,.08),(1.7,1.30,.14),bevel=.12)
    for x in [-.48,0,.48]:
        for yy in [-.30,.30]:
            g.ell('dough',(x,yy,.31),(.225,.225,.21),seg=14,rings=7);g.ell('brown',(x,yy,.445),(.17,.17,.085),seg=12,rings=5)
            for offset in [-.085,0,.085]:
                pts=[]
                for j in range(13):t=(j/12-.5)*.33;zz=.445+.085*sqrt(max(.03,1-(offset/.19)**2-(t/.19)**2))+.009;pts.append((x+offset,yy+t,zz))
                g.tube('cream',pts,.015,5)
            for i in range(3):a=i*2.4;g.box('paper',(x+.10*cos(a),yy+.10*sin(a),.54),(.095,.06,.009),rot=(.17,.1,a),bevel=.001)

def build_riceplate(g,style):
    y=plate(g,'whiteplate' if style!='nasi' else 'ceramic')
    if style=='omelet':
        grains(g,(0,0,y+.07),.69,95,'orange',.22)
        g.organic('yellow',(0,0,y+.22),(.78,.51,.22),seed=3,rough=.035,seg=30,rings=12)
        saucezig(g,(0,0,y+.44),.74,.36,'red',.025);greens(g,y,.88,3)
    elif style=='curry':
        grains(g,(.35,.20,y+.08),.58,105,height=.15)
        g.organic('brown',(-.23,-.17,y+.04),(.64,.65,.09),seed=37,rough=.045,seg=28,rings=9)
        for i in range(15):
            a=i*2.4;r=.44*sqrt(i/15);g.organic(['dough','orange','meat'][i%3],(-.23+r*cos(a),-.17+r*sin(a),y+.145),(.095,.092,.065),i,rough=.13,exponent=.4,seg=12,rings=6,rot=(.1,.1,a))
    else:
        grains(g,(0,0,y+.08),.76,165,'toast',.25);friedegg(g,(.06,.10,y+.35),.40)
        for i in range(23):
            a=i*2.4;g.organic('orange' if i%2 else 'green',(.70*cos(a),.69*sin(a),y+.14),(.037,.035,.032),i,rough=.06,exponent=.38,seg=7,rings=4)
        for a in [0,.6,1.2]:veg_slice(g,(.72*cos(a),.72*sin(a),y+.12),.19,'cucumber')

def build_eelrice(g,style):
    g.box('ceramic',(0,0,.17),(1.82,1.46,.34),bevel=.10);g.box('red',(0,0,.345),(1.68,1.32,.025),bevel=.07)
    g.organic('rice',(0,0,.38),(.76,.57,.09),rough=.02,exponent=.27,seg=24,rings=8)
    rng=random.Random(16)
    for i in range(150):
        g.ell('rice',(rng.uniform(-.73,.73),rng.uniform(-.52,.52),.47),(.036,.017,.014),seg=6,rings=3,rot=(0,0,rng.random()*pi))
    for j in range(3):
        yy=-.39+j*.37;g.organic('brown',(0,yy,.515),(.70,.155,.077),j+5,rough=.07,exponent=.43,seg=26,rings=9)
        for i in range(13):
            x=-.62+i*.103;pts=[]
            for k in range(9):
                t=k/8;py=yy-.14+t*.28;z=.515+.078*sqrt(max(.05,1-((py-yy)/.165)**2-(x/.78)**4));pts.append((x+.006*sin(t*9+i),py,z+.004))
            g.tube('meat' if i%3 else 'darkbrown',pts,.009,4)
    scallions(g,.61,18,.45);sesame(g,.59,.63)

def build_yangjangpi(g,style):
    y=plate(g,'whiteplate')
    noodles(g,y+.04,'cream',r=.61,n=18,thick=.018)
    rng=random.Random(73)
    for q,mat in enumerate(['orange','green','cream','brown','yellow','pork']):
        a=q*2*pi/6
        for j in range(22):
            off=(j%5-2)*.035;start=.28+rng.uniform(-.035,.04);end=.83+rng.uniform(-.04,.03);z=y+.08+(j//5)*.013
            pts=[]
            for t in [i/8 for i in range(9)]:
                rr=start+(end-start)*t;aa=a+off+.015*sin(t*5+j);pts.append((rr*cos(aa),rr*sin(aa),z+.024*sin(pi*t)))
            g.tube(mat,pts,.018,5)
    for i in range(5):a=i*2*pi/5;shrimp(g,(.13*cos(a),.13*sin(a),y+.18),.65,rot=a)

def build_dimsum(g,style):
    y=bowl(g,'wood',h=.35);g.torus('paper',(0,0,.39),.98,.025,32,5)
    for j in range(12):
        x=-.72+j*1.44/11;g.box('paper',(x,0,.245),(.035,2*sqrt(max(0,.77**2-x*x)),.02),bevel=.003)
    for q,(x,yy) in enumerate([(-.36,-.34),(.36,-.34),(-.36,.34),(.36,.34)]):
        v=[];f=[];n=48
        for j in range(9):
            t=j/8
            for i in range(n):
                a=2*pi*i/n;rr=.21+.045*sin(t*pi)+(.009+.027*t)*cos(a*12+t*1.5);z=.04+.34*t+.007*t**5*sin(a*12)
                v.append((x+rr*cos(a),yy+rr*sin(a),y+z))
        for j in range(8):
            for i in range(n):f.append((j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i))
        f.append(tuple(reversed(range(n))));g.add(v,f,'wrapper',smooth=True)
        g.organic('pork',(x,yy,y+.36),(.175,.175,.045),q,rough=.15,seg=16,rings=6)
        for i in range(14):
            a=i*2.4;r=.15*sqrt(i/14);g.organic('cream' if i%3 else 'salmon',(x+r*cos(a),yy+r*sin(a),y+.397),(.022,.023,.019),i,rough=.12,seg=6,rings=4)
        for i in range(7):
            a=i*2.4;g.ell('orange',(x+.065*cos(a),yy+.065*sin(a),y+.423),(.026,.026,.023),seg=8,rings=4)

def build_springroll(g,style):
    y=plate(g,'whiteplate');greens(g,y,.90,5)
    for q,x in enumerate([-.49,0,.49]):
        k=g.mark()
        g.organic('lettuce',(0,0,.20),(.17,.53,.15),q,rough=.08,exponent=.65,seg=18,rings=9)
        for j in range(3):shrimp(g,(0,-.34+j*.30,.30),.47,rot=.1)
        # Rice paper wraps around the filling; open end shows cut vegetables.
        v=[];f=[];n=32
        for j in range(9):
            yy=-.56+j*.14
            for i in range(n):
                a=2*pi*i/n;r=1+.012*sin(a*7+j);v.append((.206*cos(a)*r,yy,.20+.18*sin(a)*r))
        for j in range(8):
            for i in range(n):f.append((j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i))
        g.add(v,f,'ricepaper',smooth=True)
        for i in range(22):
            a=i*2.4;r=.13*sqrt(i/22);g.organic(['orange','cream','green'][i%3],(r*cos(a),-.567,.20+r*sin(a)),(.018,.019,.020),i,rough=.05,seg=7,rings=4)
        g.transform(k,(x,-.03,y),rot=(0,0,-.07+q*.04))

def build_crabcurry(g,style):
    y=plate(g);g.organic('yellow',(0,0,y+.06),(.86,.84,.055),seed=12,rough=.045,seg=28,rings=8)
    for i in range(40):
        a=i*2.4;r=.72*sqrt(i/40);g.organic('egg',(r*cos(a),r*sin(a),y+.12),(.075,.060,.029),i,rough=.15,seg=8,rings=5)
    # Serrated carapace, jointed legs and recognisable divided claws.
    g.organic('red',(0,.06,y+.29),(.40,.30,.18),seed=6,rough=.06,seg=24,rings=11)
    for i in range(15):
        a=i*2*pi/15;g.tube('red',[(.34*cos(a),.26*sin(a)+.06,y+.30),(.43*cos(a),.32*sin(a)+.06,y+.27)],.025,6,radii=[.033,.003])
    for ss in [-1,1]:
        for i in range(4):
            pts=[(ss*.27,.23-i*.13,y+.28),(ss*.52,.37-i*.19,y+.22),(ss*.73,.39-i*.25,y+.12)]
            g.tube('orange',smooth_path(pts,4),.032,8,radii=[.055-(j/8)*.037 for j in range(9)])
            g.organic('red',pts[1],(.055,.055,.045),i,rough=.07,seg=10,rings=5)
        g.tube('orange',[(ss*.24,-.13,y+.28),(ss*.46,-.30,y+.24),(ss*.49,-.43,y+.22)],.060,8)
        g.organic('red',(ss*.49,-.48,y+.23),(.105,.13,.08),rough=.04,seg=14,rings=7)
        for side in [-1,1]:
            g.tube('red',[(ss*.49+side*.05,-.52,y+.25),(ss*.49+side*.085,-.64,y+.22),(ss*.49+side*.015,-.68,y+.23)],.027,6,radii=[.047,.035,.008])
        g.ell('black',(ss*.09,-.22,y+.35),(.018,.014,.018),seg=8,rings=4)
    scallions(g,y+.16,16,.72)

def build_tandoori(g,style):
    y=plate(g);greens(g,y,.86,8)
    for s in [-1,1]:
        k=g.mark()
        g.organic('red',(0,-.16,.24),(.265,.39,.225),seed=7,rough=.09,exponent=.85,seg=22,rings=11)
        g.tube('red',[(0,.04,.24),(0,.31,.20),(.015,.57,.17)],.11,12,radii=[.21,.13,.078])
        g.organic('brown',(.015,.61,.17),(.10,.075,.075),rough=.06,seg=12,rings=6)
        for i in range(5):
            yy=-.40+i*.14;pts=[]
            for j in range(9):
                xx=-.19+j*.0475;zz=.24+.22*sqrt(max(.1,1-(xx/.28)**2-((yy+.16)/.44)**2));pts.append((xx,yy,zz+.004))
            g.tube('darkbrown',pts,.010,4)
        g.transform(k,(s*.33,-.04,y),rot=(0,0,s*.22))
    citrus(g,(-.60,-.49,y+.06),.23)
    for i in range(2):g.torus('purple',(.58,.48,y+.08+i*.024),.18,.014,20,5)

def build_naan(g,style):
    y=bowl(g,'wood',h=.24)
    for q in range(2):
        k=g.mark();poly=[]
        for i in range(36):
            a=i*2*pi/36;r=1+.045*sin(a*5+q);poly.append((.56*cos(a)*r*(1-.22*sin(a)),.80*sin(a)*r))
        baked_piece(g,(0,0,0),poly,.10,'tortilla','dough',q+5)
        rng=random.Random(51+q)
        for i in range(22):
            a=rng.random()*2*pi;r=.68*sqrt(rng.random());x=.66*r*cos(a);yy=r*sin(a)
            zz=.108+.010*sin(pi*(1-r/.80));g.organic('tortilla',(x,yy,zz),(.060,.078,.019),i,rough=.15,seg=10,rings=5)
            g.organic('toast',(x-.012,yy,zz+.017),(.032,.047,.003),i,rough=.2,seg=9,rings=4)
        for i in range(25):
            a=i*2.4;r=.64*sqrt(i/25);g.ell('green',(.60*r*cos(a),r*sin(a),.126),(.009,.015,.002),seg=5,rings=3)
        g.transform(k,((q-.5)*.35,0,y+.015+q*.08),rot=(0,.06*(q-.5),(q-.5)*.44))

def build_wrap(g,style):
    y=plate(g)
    if style=='taco':
        for q in range(2):
            k=g.mark();v=[];f=[];n=32
            for j in range(9):
                yy=-.59+j*1.18/8
                for i in range(n+1):
                    a=pi*i/n;v.append((.39*cos(a),yy,.42*(1-sin(a))+.025*cos(yy*6)*sin(a)**2))
            for j in range(8):
                for i in range(n):a=j*(n+1)+i;f.append((a,a+1,a+n+2,a+n+1))
            g.add(v,f,'dough',smooth=True)
            for i in range(30):
                a=i*2.4;yy=-.49+(i%9)*.118;g.organic('meat',(.19*cos(a),yy,.27+.035*sin(i)),(.065,.074,.065),i,rough=.19,seg=9,rings=5)
            for i in range(8):lettuce(g,(-.12,-.48+i*.126,.30),.22,.20,1 if i%2 else -1,seed=i)
            for i in range(13):
                a=i*2.4;g.organic('tomato',(.18*cos(a),-.48+(i%8)*.13,.40),(.045,.045,.035),i,rough=.03,exponent=.35,seg=8,rings=4)
            g.transform(k,((q-.5)*.85,0,y+.03),rot=(0,0,-.08))
        return
    # Rolled kebab cylinder, distinct from the open taco shell.
    k=g.mark();v=[];f=[];n=40;m=12
    for j in range(m+1):
        t=j/m;yy=-.64+1.30*t;r=.38*(1-.10*t)
        for i in range(n):
            a=2*pi*i/n;v.append((r*cos(a),yy,.37+r*.90*sin(a)+.007*sin(i*1.8+j)))
    for j in range(m):
        for i in range(n):f.append((j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i))
    g.add(v,f,'tortilla',smooth=True)
    rng=random.Random(73)
    for i in range(24):
        a=rng.uniform(0,pi);yy=rng.uniform(-.50,.49);r=.38*(1-.10*((yy+.64)/1.30))
        pass  # Scorch color is in the tortilla albedo, not raised geometry.
    for i in range(33):
        a=i*2.4;r=.29*sqrt(i/33);c=['meat','green','orange','cream','lettuce'][i%5]
        g.organic(c,(r*cos(a),-.655,.37+r*.9*sin(a)),(.041,.075,.037),i,rough=.13,seg=10,rings=5,rot=(.1,0,a))
    for i in range(5):lettuce(g,(-.2+i*.10,-.67,.40),.23,.17,pi,seed=i)
    # Paper cuff follows the rear half, without covering the visible filling.
    vv=[];ff=[]
    for yy in [.20,.68]:
        for i in range(n):
            a=i*2*pi/n;vv.append((.37*cos(a),yy,.37+.343*sin(a)))
    for i in range(n):ff.append((i,(i+1)%n,(i+1)%n+n,i+n))
    g.add(vv,ff,'paper',smooth=True)
    # Tucked paper closes the rear of the wrap, including the back view.
    cap=[(.365*cos(i*2*pi/24),.687,.37+.339*sin(i*2*pi/24)) for i in range(24)]+[(.035,.668,.38)]
    g.add(cap,[(i,(i+1)%24,24) for i in range(24)],'paper',smooth=False)
    for a in [0,pi/2,pi,3*pi/2]:g.tube('dough',[(.035,.689,.38),(.27*cos(a),.689,.37+.26*sin(a))],.003,3)
    g.transform(k,(0,0,y+.02),rot=(0,.08,-.32))

def build_nachos(g,style):
    y=plate(g);rng=random.Random(81)
    for i in range(25):
        a=i*2.4;r=.70*sqrt(i/25);k=g.mark()
        poly=[(-.19,-.15),(.20,-.16),(.02,.23)]
        baked_piece(g,(0,0,0),poly,.024,'dough','baked',i)
        g.transform(k,(r*cos(a),r*sin(a),y+.045+(i%3)*.045),rot=(.10,.16,a))
    # Irregular cheese coating leaves the triangular silhouettes visible.
    g.organic('yellow',(0,0,y+.185),(.52,.53,.047),seed=9,rough=.15,seg=30,rings=8)
    for i in range(9):
        a=i*2.4;x=.52*cos(a);yy=.52*sin(a);z=surface_height(g,x,yy,y+.23)
        g.torus('green',(x,yy,z+.02),.072,.019,12,5)
        g.organic('tomato',(.39*cos(a),.39*sin(a),y+.26),(.039,.04,.03),i,rough=.05,exponent=.32,seg=8,rings=4)
    g.organic('cream',(-.12,.02,y+.29),(.19,.18,.093),rough=.06,seg=18,rings=8)
    for i in range(5):a=i*2.4;g.organic('lightgreen',(.16+.09*cos(a),.10+.09*sin(a),y+.265),(.084,.076,.069),i,rough=.10,seg=12,rings=6)

def build_pack(g,style):
    if style=='triangle':
        tri=[(-.64,0),(.64,0),(0,1.10)]
        g.prism('nori',tri,.34,(0,.17,.02),(pi/2,0,0))
        g.prism('label',[(-.27,.06),(.27,.06),(0,.58)],.015,(0,-.182,.04),(pi/2,0,0))
        g.prism('orange',[(-.14,.1),(.14,.1),(0,.40)],.02,(0,-.201,.04),(pi/2,0,0))
        g.box('red',(0,-.19,.87),(.105,.025,.32),bevel=.005)
        g.prism('glass',[(-.67,-.015),(.67,-.015),(0,1.13)],.37,(0,.183,.02),(pi/2,0,0))
        for s in [-1,1]:g.tube('glass',[(s*.65,-.177,.03),(0,-.177,1.13)],.012,4)
    elif style=='ramen':
        g.lathe('red',[(0,0),(.43,0),(.61,1.28),(.62,1.34),(.56,1.35),(.41,.06),(0,.06)],sides=24)
        g.torus('cream',(0,0,1.34),.597,.028,24,5);soup(g,'orange',1.27,.55);noodles(g,1.28,'cream',.49,13,.021);scallions(g,1.38,7,.32)
        g.box('label',(0,-.538,.58),(.64,.02,.47),bevel=.03)
        g.ell('orange',(0,-.557,.58),(.25,.009,.17),seg=18,rings=5)
        g.ell('cream',(0,-.569,.60),(.10,.01,.12),seg=12,rings=5)
        g.ell('yellow',(0,-.58,.60),(.054,.006,.065),seg=10,rings=5)
        for x in [-.09,.09]:g.tube('wood',[(x-.5,-.02,1.42),(x+.55,.38,1.42)],.015,5)
        k=g.mark();poly=[(0,0)]+[(.57*cos(i*pi/16),.57*sin(i*pi/16)) for i in range(17)];g.prism('red',poly,.012);g.transform(k,(0,.30,1.35),rot=(.6,0,0))
    elif style=='lunch':
        g.box('ceramic',(0,0,.11),(1.58,1.92,.22),bevel=.16)
        for x in [-.75,.75]:g.box('rim',(x,0,.25),(.045,1.80,.06),bevel=.015)
        for yy in [-.90,.90]:g.box('rim',(0,yy,.25),(1.48,.045,.06),bevel=.015)
        g.box('rim',(0,0,.24),(.05,1.70,.07),bevel=.008)
        for yy in [-.26,.29]:g.box('rim',(.37,yy,.24),(.70,.04,.07),bevel=.007)
        g.organic('rice',(-.36,-.29,.22),(.28,.50,.10),seed=3,rough=.035,exponent=.25,seg=16,rings=7)
        rng=random.Random(41)
        for i in range(140):xx=-.36+rng.uniform(-.26,.26);yy=-.30+rng.uniform(-.46,.46);g.ell('rice',(xx,yy,.315+rng.uniform(-.015,.01)),(.043,.019,.018),seg=6,rings=3,rot=(0,0,rng.random()*pi))
        for i in range(8):a=i*2.4;g.organic('brown',(-.38+.22*cos(a),.52+.19*sin(a),.33),(.12,.10,.09),seed=i,rough=.15,seg=12,rings=6)
        for i in range(5):g.box('egg',(.19+i*.077,-.58,.31),(.066,.35,.15),bevel=.014)
        for i in range(8):g.box('orange',(.20+(i%3)*.10,.50+(i//3)*.1,.31),(.055,.27,.05),rot=(0,.1,.4),bevel=.008)
        for i in range(4):leaf(g,(.25+(i%2)*.19,-.15+(i//2)*.18,.26),.28,.19,i)
        g.box('label',(-.37,.10,.40),(.66,.16,.012),bevel=.008);g.box('red',(-.37,.10,.414),(.42,.09,.005),bevel=.002)
        g.box('glass',(0,0,.48),(1.54,1.88,.08),bevel=.14)
    elif style=='can':
        g.lathe('blue',[(0,.04),(.34,.04),(.38,.12),(.39,1.30),(.35,1.40),(.34,1.45),(0,1.45)],sides=24)
        for z in [.06,1.43]:g.torus('silver',(0,0,z),.35,.025,24,5)
        soup(g,'silver',1.45,.34);g.torus('rim',(0,-.025,1.47),.095,.018,16,5,scale=(.65,1,1))
        poly=[(-.02,.32),(.17,.81),(.045,.81),(.16,1.16),(-.19,.63),(-.04,.63)]
        g.prism('yellow',poly,.012,(0,-.391,0),(pi/2,0,0))
    elif style in ['water','cola']:
        body='waterglass' if style=='water' else 'darkbrown';cap='blue' if style=='water' else 'red'
        g.lathe(body,[(0,.02),(.33,.02),(.40,.12),(.40,.30),(.37,.40),(.40,.50),(.40,1.04),(.36,1.20),(.20,1.40),(.18,1.55),(0,1.55)],sides=20)
        if style=='water':g.lathe('waterliquid',[(0,.08),(.35,.08),(.35,1.04),(.29,1.22),(0,1.22)],sides=24,smooth=True)
        for z in [.20,.36,.96,1.10]:g.torus('glassrim' if style=='water' else 'brown',(0,0,z),.387,.017,20,4)
        g.lathe(cap,[(.195,1.48),(.195,1.68)],sides=20);g.torus(cap,(0,0,1.5),.195,.018,20,4)
        for i in range(24):a=i*pi/12;g.tube(cap,[(.194*cos(a),.194*sin(a),1.51),(.194*cos(a),.194*sin(a),1.655)],.007,4)
        for zz in [1.43,1.46]:g.torus('glassrim',(0,0,zz),.188,.009,24,4)
        g.lathe('label' if style=='water' else 'red',[(.402,.53),(.402,.92)],sides=20)
        g.box(cap,(0,-.408,.72),(.44,.015,.17),bevel=.006)
        if style=='water':g.prism('cream',[(-.22,0),(-.08,.18),(.07,.03),(.20,.13),(.22,0)],.01,(0,-.421,.63),(pi/2,0,0))
    else:
        body={'bar':'chocolate','chocolate':'brown','chips':'yellow','jelly':'packpink'}[style]
        narrow=style in ['bar','chocolate'];w=.70 if narrow else 1.06;h=1.60
        v=[];f=[];n=12;m=12
        for side in [-1,1]:
            for j in range(m+1):
                t=j/m;zz=t*h
                for i in range(n+1):
                    u=(i/n-.5)*2;xx=u*w*.5*(.98+.04*cos(pi*t*2));depth=.08+.065*sin(pi*t)**.6*(1-u*u)+.006*sin(u*19+t*11)*(1-abs(u))
                    v.append((xx,side*depth,zz))
        k=(n+1)*(m+1)
        for s in range(2):
            for j in range(m):
                for i in range(n):a=s*k+j*(n+1)+i;f.append((a,a+1,a+n+2,a+n+1))
        for j in range(m):
            for i in [0,n]:a=j*(n+1)+i;f.append((a,a+n+1,a+n+1+k,a+k))
        for j in [0,m]:
            for i in range(n):a=j*(n+1)+i;f.append((a,a+1,a+1+k,a+k))
        g.add(v,f,body,smooth=True)
        for z in [.02,1.59]:
            g.box(body,(0,0,z),(w+.05,.16,.10),bevel=.009)
            for i in range(14):g.box('toast' if narrow else body,(-w/2+i*w/13,0,z),(.025,.18,.08),rot=(0,.14,0),bevel=.003)
        g.box('label',(0,-.163,1.1),(w*.85,.014,.30),rot=(0,0,-.10),bevel=.015)
        if style=='chips':
            g.box('red',(0,-.18,.46),(w*.88,.012,.63),bevel=.015)
            for x,z in [(-.20,.39),(.17,.60),(.13,.27)]:g.ell('dough',(x,-.204,z),(.22,.018,.15),seg=12,rings=5,rot=(0,-.3,.2))
        elif style=='chocolate':
            for x,z in [(-.13,.43),(.13,.43),(-.13,.69),(.13,.69)]:g.box('chocolate',(x,-.19,z),(.24,.07,.24),bevel=.025)
        elif style=='bar':
            for i in range(16):a=i*2.4;g.ell('dough',(.24*cos(a),-.18,.35+(i%5)*.095),(.055,.018,.075),seg=7,rings=4,rot=(0,0,a))
        else:
            for x,z,c in [(-.22,.42,'red'),(.20,.65,'green'),(.19,.28,'yellow')]:
                for xx,zz,ss in [(0,0,.09),(-.065,.11,.05),(.065,.11,.05),(-.065,-.10,.05),(.065,-.10,.05),(0,.10,.075)]:g.ell(c,(x+xx,-.185,z+zz),(ss,.025,ss),seg=9,rings=5)

def build_cake(g,style):
    y=plate(g,'whiteplate',r=1)
    if style=='tiramisu':
        poly=[(-.64,-.47),(.65,-.47),(.65,.48),(-.65,.47)]
        for i in range(4):baked_piece(g,(0,0,y+i*.13),poly,.14,'cream' if i%2 else 'brown','brown' if not i%2 else 'cream',i+9)
        baked_piece(g,(0,0,y+.54),poly,.028,'chocolate','chocolate',4)
        scatter_surface(g,'darkbrown',180,14,r=.76,size=.009)
        return
    body='chocolate' if style=='chocolate' else 'cream';base='dough' if style!='chocolate' else 'brown'
    g.lathe(base,[(0,y),(.76,y),(.79,y+.025),(.79,y+.13),(0,y+.13)],sides=40,smooth=True)
    g.lathe(body,[(0,y+.1),(.78,y+.1),(.80,y+.15),(.80,y+.64),(.79,y+.68),(.75,y+.715),(0,y+.715)],sides=40,smooth=True)
    if style!='cheese':
        g.lathe('dough' if style=='strawberry' else 'brown',[(.801,y+.28),(.802,y+.315)],sides=40)
        for i in range(9):a=i*2*pi/9;swirl(g,(.59*cos(a),.59*sin(a),y+.735),.107,.175,body,1.6)
    if style=='strawberry':
        for i in range(5):a=i*2*pi/5;fruitberry(g,(.32*cos(a),.32*sin(a),y+.73),.17)
        for i in range(10):
            a=i*2*pi/10;k=g.mark();poly=[(0,-.115),(.078,-.04),(.098,.053),(.07,.109),(-.04,.123),(-.094,.07),(-.082,-.03)]
            g.prism('berry',poly,.008);g.prism('cream',[(x*.48,yy*.78) for x,yy in poly],.010)
            g.transform(k,(.803*cos(a),.803*sin(a),y+.16),rot=(pi/2,0,a+pi/2))
    elif style=='chocolate':
        for i in range(17):
            a=i*2.4;rr=.40*sqrt(i/17);g.organic('brown',(rr*cos(a),rr*sin(a),y+.755),(.071,.058,.029),i,rough=.10,exponent=.28,seg=10,rings=6,rot=(.18,.1,a))
    else:
        for a in [0,2,4]:fruitberry(g,(.22*cos(a),.22*sin(a),y+.72),.16);fruitberry(g,(.36*cos(a+.8),.36*sin(a+.8),y+.78),.095,'blueberry')
        scatter_surface(g,'dough',55,5,r=.75,size=.007,exclude=('blueberry','berry'))

def build_waffle(g,style):
    y=plate(g,'whiteplate');g.lathe('dough',[(0,y),(.83,y),(.83,y+.11),(0,y+.11)],sides=28)
    # Orthogonal raised lattice, with the disk providing recessed pockets.
    for i in range(-4,5):
        x=i*.17;l=sqrt(.82**2-x*x)*2
        g.box('dough',(x,0,y+.16),(.048,l,.105),bevel=.012);g.box('dough',(0,x,y+.16),(l,.048,.105),bevel=.012)
    g.torus('dough',(0,0,y+.12),.815,.055,28,6)
    g.ell('cream',(.05,.1,y+.36),(.28,.27,.22),seg=16,rings=7)
    for a in [0,2,4]:fruitberry(g,(.48*cos(a),.48*sin(a),y+.2),.16)

def build_pastry(g,style):
    y=plate(g,'whiteplate');rng=random.Random(106)
    if style=='macaron':
        for i,(x,yy,c) in enumerate([(-.44,-.31,'pink'),(.34,-.34,'lightgreen'),(-.39,.34,'chocolate'),(.33,.34,'purple'),(0,.08,'yellow')]):
            z=y+.04+(i==4)*.23
            g.lathe('cream',[(0,z+.09),(.285,z+.09),(.29,z+.145),(0,z+.145)],(x,yy,0),sides=24,smooth=True)
            for zz,flip in [(z+.08,-1),(z+.17,1)]:
                g.organic(c,(x,yy,zz),(.31,.31,.087),seed=i,rough=.02,seg=24,rings=8)
                for j in range(28):a=j*2*pi/28;g.organic(c,(x+.282*cos(a),yy+.282*sin(a),zz-.03*flip),(.032,.024,.024),seed=j,rough=.12,seg=5,rings=3)
    elif style=='financier':
        for i,(x,yy,a) in enumerate([(-.37,-.27,-.15),(.34,-.25,.12),(0,.40,.4)]):
            k=g.mark();baked_piece(g,(0,0,0),[(-.21,-.36),(.21,-.36),(.22,.36),(-.22,.36)],.17,'dough','baked',i)
            g.organic('dough',(0,0,.17),(.15,.29,.055),seed=i,rough=.10,exponent=.6,seg=16,rings=6)
            g.tube('baked',[(-.03,-.22,.215),(.008,-.10,.223),(-.005,.02,.23),(.014,.18,.216)],.008,4)
            g.transform(k,(x,yy,y+.01),rot=(0,0,a))
    elif style=='tart':
        for x,yy in [(-.43,-.21),(.38,-.20),(0,.43)]:
            k=g.mark();v=[];f=[];n=40
            for r,z in [(.23,0),(.29,.08),(.33,.24),(.315,.27),(.265,.23),(.20,.06)]:
                for j in range(n):a=j*2*pi/n;rr=r*(1+.06*cos(a*20));v.append((rr*cos(a),rr*sin(a),z))
            for j in range(5):
                for i in range(n):f.append((j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i))
            g.add(v,f,'dough',smooth=True);g.organic('yellow',(0,0,.213),(.271,.271,.047),rough=.04,seg=24,rings=6)
            for i in range(9):
                a=i*2.4;rr=.21*sqrt(rng.random());g.organic('baked',(rr*cos(a),rr*sin(a),.250),(.036+rng.random()*.026,.025+rng.random()*.025,.003),i,rough=.25,seg=8,rings=3)
            g.transform(k,(x,yy,y+.01))
    elif style in ['croissant','saltbread']:
        count=1 if style=='croissant' else 3
        for kidx in range(count):
            k=g.mark();pts=[];radii=[];bend=.63 if style=='croissant' else .10
            for i in range(65):
                t=i/64;a=.14*pi+t*.72*pi;pts.append((.83*cos(a),bend*sin(a)-.15,.24+.025*sin(pi*t)))
                radii.append((.018+.265*sin(pi*t)**.82)*(1+.025*sin(t*20*pi)))
            g.tube('dough',pts,sides=16,radii=radii)
            for i in range(1,16):
                t=i/16;a=.14*pi+t*.72*pi;r=(.018+.265*sin(pi*t)**.82);center=Vector((.83*cos(a),bend*sin(a)-.15,.24+.025*sin(pi*t)))
                normal=Vector((bend*cos(a),.83*sin(a),0)).normalized()
                line=[tuple(center+normal*(r+.002)*cos(j*pi/16)+Vector((0,0,(r+.002)*sin(j*pi/16)))) for j in range(17)]
                g.tube('baked',line,.006 if style=='croissant' else .004,4)
                if i%2==0:g.tube('dough',[tuple(Vector(p)+normal*.008+Vector((0,0,.006))) for p in line],.014,5)
            if style=='saltbread':
                for i in range(12):g.organic('cream',(rng.uniform(-.34,.34),rng.uniform(-.09,.05),.47),(.012,.012,.010),i,seg=5,rings=3)
            scale=.69 if count>1 else 1.10
            g.transform(k,((kidx-1)*.45 if count>1 else 0,(kidx%2)*.40,y-.02),scale=(scale,scale,scale),rot=(0,0,(kidx-1)*.24))
    elif style in ['cookie','scone','brownie']:
        for i,(x,yy) in enumerate([(-.40,-.28),(.36,-.29),(0,.35)]):
            k=g.mark()
            if style=='cookie':poly=geo['wob'](20,.36,.05,i);height=.14
            elif style=='scone':poly=[(-.34,-.25),(.35,-.24),(.08,.39)];height=.25
            else:poly=[(-.29,-.28),(.28,-.29),(.30,.29),(-.28,.28)];height=.25
            mat='chocolate' if style=='brownie' else 'dough';side='darkbrown' if style=='brownie' else 'baked'
            baked_piece(g,(0,0,0),poly,height,mat,side,i+5,True)
            for j in range(15 if style!='scone' else 10):
                if style=='scone':
                    u=rng.random();v=rng.random()
                    if u+v>1:u,v=1-u,1-v
                    px=-.34+u*.69+v*.42;py=-.25+u*.01+v*.64
                else:a=rng.random()*2*pi;rr=.255*sqrt(rng.random());px=rr*cos(a);py=rr*sin(a)
                g.organic('chocolate' if style!='brownie' else 'dough',(px,py,height+.025),(.035,.032,.022),j,rough=.20,exponent=.43,seg=7,rings=4,rot=(.2,.15,j))
            # Exposed crumb holes along the irregular cut edge.
            for j in range(14):
                px=-.24+j*.035;py=-.247 if style=='scone' else -.28
                if style!='cookie':g.ell(side,(px,py-.002,.055+rng.random()*.14),(.006,.003,.008),seg=5,rings=3)
            g.transform(k,(x,yy,y+.008),rot=(0,0,i*.11-.10))
    elif style=='pretzel':
        k=g.mark()
        control=[(-.18,-.38,.17),(-.59,-.22,.13),(-.60,.25,.13),(-.31,.42,.14),(0,.20,.18),(.37,-.30,.18),(.22,-.43,.18),(-.30,-.31,.22),(0,.22,.17),(.33,.43,.14),(.61,.24,.13),(.57,-.21,.13),(.19,-.39,.17)]
        g.tube('dough',smooth_path(control,7),.108,12)
        for i in range(23):
            a=i*2.4;xx=.50*cos(a);yy=.30*sin(a);g.organic('cream',(xx,yy,.23),(.016,.014,.012),i,seg=5,rings=3)
        g.transform(k,(0,0,y+.01))
    elif style=='donut':
        for x,yy,c in [(-.35,-.18,'pink'),(.34,.23,'chocolate')]:
            g.torus('dough',(x,yy,y+.16),.255,.155,32,10)
            v=[];f=[];n=40;m=10
            for i in range(n):
                a=i*2*pi/n
                for j in range(m+1):
                    b=-.12+(pi+.24)*j/m
                    if j in [0,m]:b+=.18*sin(a*7)*(-1 if j==0 else 1)
                    r=.255+.158*cos(b);v.append((x+r*cos(a),yy+r*sin(a),y+.16+.158*sin(b)+.004))
            for i in range(n):
                for j in range(m):a=i*(m+1)+j;b=((i+1)%n)*(m+1)+j;f.append((a,b,b+1,a+1))
            g.add(v,f,c,smooth=True)
            for i in range(30):
                a=rng.random()*2*pi;b=rng.uniform(.35,2.8);r=.255+.162*cos(b)
                g.tube(['yellow','cream','green'][i%3],[(x+r*cos(a),yy+r*sin(a),y+.16+.162*sin(b)),(x+r*cos(a)+.025*cos(i),yy+r*sin(a)+.025*sin(i),y+.16+.162*sin(b)+.004)],.007,4)
    elif style=='pudding':
        g.lathe('egg',[(0,y),(.54,y),(.59,y+.065),(.56,y+.13),(.42,y+.57),(.37,y+.61),(0,y+.61)],sides=32,smooth=True)
        g.organic('brown',(0,0,y+.60),(.41,.41,.034),rough=.05,seg=28,rings=5)
        for i in range(7):
            a=i*2.4;d=.12+.05*sin(i)
            g.tube('brown',[(.39*cos(a),.39*sin(a),y+.606),(.43*cos(a),.43*sin(a),y+.50),(.46*cos(a),.46*sin(a),y+.61-d)],.020,6)
        swirl(g,(0,0,y+.64),.19,.24);g.organic('berry',(0,0,y+.98),(.09,.09,.09),rough=.03,seg=12,rings=6)
        g.tube('brown',[(0,0,y+1.04),(.018,0,y+1.19),(.08,0,y+1.23)],.009,5)
    elif style=='churros':
        for i in range(5):
            x=-.45+i*.21;g.tube('dough',[(x,-.66,y+.10),(x,.60,y+.10)],.075,12)
            for j in range(8):
                a=j*pi/4;g.tube('baked',[(x+.072*cos(a),-.66,y+.10+.072*sin(a)),(x+.072*cos(a),.60,y+.10+.072*sin(a))],.007,4)
        k=g.mark();bowl(g,'whiteplate',r=.27,h=.25);soup(g,'chocolate',.24,.235);g.transform(k,(.56,.5,y))

SHARED={'pasta','latte','bingsu','softserve'}
def flavor_colors(item):
    if item['builder']=='pasta':return ('c55b35','9f3e24') if item['style']=='tomato' else ('e7d6b1','c9b58d')
    return FLAVORS.get(item['style'],FLAVORS['vanilla'])

def mesh_from_geo(g,name,scale):
    keys=list(dict.fromkeys(g.m));mesh=bpy.data.meshes.new(name)
    zmin=min(v[2] for v in g.v)
    mesh.from_pydata([(x*scale,y*scale,(z-zmin)*scale) for x,y,z in g.v],[],g.f)
    for key in keys:mesh.materials.append(material(key))
    for p,key,smooth in zip(mesh.polygons,g.m,g.s):p.material_index=keys.index(key);p.use_smooth=smooth
    bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=1e-7)
    bmesh.ops.dissolve_degenerate(bm,edges=list(bm.edges),dist=1e-9)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bmesh.ops.triangulate(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
    uv=mesh.uv_layers.new(name='UVMap')
    for poly in mesh.polygons:
        axis=max(range(3),key=lambda i:abs(poly.normal[i]));axes=[i for i in range(3) if i!=axis]
        for li in poly.loop_indices:
            co=mesh.vertices[mesh.loops[li].vertex_index].co
            if keys[poly.material_index] in ['sushisalmon','sushituna','sushishrimp']:
                cx,cy=(-.38,.015) if co.x<0 else (.34,-.055)
                uv.data[li].uv=((co.x/scale-cx)/.62+.5,(co.y/scale-cy)/1.08+.5)
            else:uv.data[li].uv=(co[axes[0]]/scale*1.4+.43,co[axes[1]]/scale*1.4+.27)
    mesh.update();return mesh,keys

def geometry_hash(mesh):
    h=hashlib.sha256()
    for v in mesh.vertices:h.update(struct.pack('<3f',*v.co))
    for p in mesh.polygons:h.update(struct.pack('<3I',*p.vertices))
    return h.hexdigest()

def setup_studio():
    scene=bpy.context.scene
    scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
    studio=bpy.data.collections.new('Studio');scene.collection.children.link(studio)
    mesh=bpy.data.meshes.new('StudioFloorMesh');mesh.from_pydata([(-200,-200,-.001),(200,-200,-.001),(200,200,-.001),(-200,200,-.001)],[],[(0,1,2,3)])
    floor=bpy.data.objects.new('StudioFloor',mesh);studio.objects.link(floor);mesh.materials.append(material('studio','b4bbb7'))
    def area(name,loc,power,size):
        d=bpy.data.lights.new(name,'AREA');d.energy=power;d.shape='DISK';d.size=size;o=bpy.data.objects.new(name,d);studio.objects.link(o);o.location=loc;o.rotation_euler=(Vector((0,0,.08))-o.location).to_track_quat('-Z','Y').to_euler()
    area('Key',(-.5,-.7,1.1),16,.65);area('Fill',(.65,-.25,.65),5,.75);area('Back',(.15,.7,.8),7,.80)
    world=bpy.data.worlds.new('SoftNeutralWorld');scene.world=world;world.use_nodes=True;world.node_tree.nodes.clear();bg=world.node_tree.nodes.new('ShaderNodeBackground');wo=world.node_tree.nodes.new('ShaderNodeOutputWorld');world.node_tree.links.new(bg.outputs[0],wo.inputs[0]);bg.inputs[0].default_value=(.5,.5,.5,1);bg.inputs[1].default_value=.23
    d=bpy.data.cameras.new('FoodCamera');cam=bpy.data.objects.new('FoodCamera',d);studio.objects.link(cam);scene.camera=cam;d.type='ORTHO';d.lens=50
    scene.render.engine='CYCLES';scene.cycles.samples=24;scene.cycles.use_denoising=True;scene.cycles.device='CPU';scene.render.threads_mode='FIXED';scene.render.threads=10
    scene.render.resolution_x=512;scene.render.resolution_y=512;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGBA'
    scene.view_settings.view_transform='AgX';scene.view_settings.look='AgX - Medium High Contrast';scene.render.film_transparent=False
    return cam

def render_item(obj,path,angle=0):
    scene=bpy.context.scene;cam=scene.camera
    bpy.context.view_layer.update();dims=obj.dimensions;target=Vector((0,0,dims.z*.43));cam.location=(.62*cos(angle)-.42*sin(angle),-.62*sin(angle)-.42*cos(angle),.55)
    # Reference-like three-quarter, high enough to read ingredients in bowls.
    cam.location=(.40*cos(angle)-.62*sin(angle),-.62*cos(angle)-.40*sin(angle),.62 if dims.z<.18 else .44)
    cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=max(dims.x,dims.y,dims.z)*1.38
    scene.render.filepath=str(path);bpy.ops.render.render(write_still=True)

def main():
    args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else ['--sample']
    sample='--sample' in args
    bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
    selected={'TomatoPasta','CreamPasta','StrawberryLatte','MatchaLatte','StrawberryBingsu','VanillaSoftServe','KimchiStew','Ramen'}
    if sample and len(args)>1:selected=set(x for x in args if not x.startswith('--'))
    items=[x for x in MENU if not sample or x['id'] in selected]
    cache={};objects=[];report=[]
    for item in items:
        builder=item['builder'];family=builder if builder in SHARED else item['id']
        if family not in cache:
            g=Geo();globals()['build_'+builder](g,item['style']);finish_food(g,item)
            scale=.105 if builder in ['latte','drink','smoothie','ade','softserve'] else (.12 if builder=='tea' else .13)
            cache[family]=mesh_from_geo(g,'Shared_'+family,scale)
        mesh,keys=cache[family]
        col=bpy.data.collections.get(item['category'])
        if col is None:col=bpy.data.collections.new(item['category']);bpy.context.scene.collection.children.link(col)
        obj=bpy.data.objects.new('Food_'+item['id'],mesh);col.objects.link(obj);obj['menu_name']=item['name'];obj['mesh_family']=family;obj['recipe_reference']=item['recipe']
        f,fd=flavor_colors(item)
        for slot,key in zip(obj.material_slots,keys):slot.link='OBJECT';slot.material=material(key,f if key=='flavor' else fd if key=='flavordark' else None)
        obj.hide_render=True;objects.append(obj)
        report.append({**item,'family':family,'vertices':len(mesh.vertices),'triangles':len(mesh.polygons),'geometryHash':geometry_hash(mesh),'materialSlots':len(keys),'fbx':'Models/FBX/'+obj.name+'.fbx','glb':'Models/GLB/'+obj.name+'.glb','render':'Renders/'+obj.name+'.png'})
        print('BUILT',item['id'],len(mesh.polygons),flush=True)
    cam=setup_studio()
    for obj,item,row in zip(objects,items,report):
        obj.hide_render=False;bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
        if not sample:
            bpy.ops.export_scene.gltf(filepath=str(HERE/row['glb']),export_format='GLB',use_selection=True,export_yup=True,export_apply=False)
            bpy.ops.export_scene.fbx(filepath=str(HERE/row['fbx']),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,add_leaf_bones=False,bake_anim=False,path_mode='COPY',embed_textures=True)
        if '--build' not in args:render_item(obj,HERE/('QA' if sample else 'Renders')/(obj.name+'.png'))
        obj.hide_render=True
    if not sample:
        for i,obj in enumerate(objects):obj.hide_render=False;obj.location=((i%12)*.42,(i//12)*.42,0)
        bpy.ops.file.pack_all()
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'CraftedFood_134.blend'))
        (HERE/'catalog.json').write_text(json.dumps({'count':len(report),'uniqueGeometry':len(cache),'items':report},ensure_ascii=False,indent=2),encoding='utf8')
    else:(HERE/'QA/sample.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
    print('CRAFTED_COMPLETE',len(items),len(cache),flush=True)

if __name__=='__main__':main()
