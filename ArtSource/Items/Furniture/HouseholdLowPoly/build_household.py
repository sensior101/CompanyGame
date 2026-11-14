"""Household furniture art recipes. Blender 5.2, metres, front=-Y, up=Z.
Authoring only: existing FurnitureFunction / FurniturePlacement are metadata.
Single/double beds reuse the approved SingleBed mesh. One shared recipe library
handles all furniture geometry, palettes, exports and presentation.
-- --only ID selects a repair; --skip-render exports without presentation renders.
"""
import bpy, math, json, hashlib, sys
from pathlib import Path
from mathutils import Vector
from math import pi, sin, cos

HERE=Path(__file__).resolve().parent
for d in ('Models','Exports','QA'): (HERE/d).mkdir(parents=True,exist_ok=True)
PALETTES={
 'Cream':dict(Body='E3D8C4',Wood='AD8057',Fabric='D6C6A8',Accent='EDE1C9'),
 'Sage':dict(Body='A3B19A',Wood='B5A083',Fabric='798F7C',Accent='C2CCB4'),
 'Blue':dict(Body='778EA8',Wood='77614F',Fabric='526F91',Accent='B0C1D5'),
 'Rose':dict(Body='C69AA2',Wood='C1A38B',Fabric='AD7683',Accent='E1BCC0'),
 'Charcoal':dict(Body='555C67',Wood='44464A',Fabric='555C67',Accent='9499A2')}
FIXED=dict(White='F0EDE6',Dark='252B33',Metal='A3A8AB',Glass='C8E1DF',Screen='182533',Rubber='343638',Water='BAD5DB')
CATALOG=[]
GPU_READY=False
def model(id,label,category,placement='Floor',function='Decoration',note=''):
 def register(fn):
  CATALOG.append(dict(id=id,label=label,category=category,placement=placement,function=function,note=note,build=fn)); return fn
 return register

def rgba(h):
 def lin(v): return v/12.92 if v<=.04045 else ((v+.055)/1.055)**2.4
 return (*[lin(int(h[i:i+2],16)/255) for i in (0,2,4)],1)

def init(entry):
 global asset,root,scene,MATS,PARTS
 bpy.ops.wm.read_factory_settings(use_empty=True)
 scene=bpy.context.scene; scene.unit_settings.system='METRIC'; scene.unit_settings.scale_length=1
 asset=bpy.data.collections.new(entry['id']+'_ASSET'); scene.collection.children.link(asset)
 root=bpy.data.objects.new(entry['id'],None); asset.objects.link(root)
 root['FurnitureFunction']=entry['function']; root['FurniturePlacement']=entry['placement']
 root['PlayerHeightMetres']=1.8; root['Front']='-Y'; root['Label']=entry['label']
 MATS={}; PARTS=[]
 for palette,colors in PALETTES.items():
  if '--model-only' in sys.argv and palette!='Cream':continue
  MATS[palette]={}
  for role,color in {**colors,**FIXED}.items():
   m=bpy.data.materials.new(palette+'_'+role); m.use_nodes=True; m.diffuse_color=rgba(color); m.use_fake_user=True
   p=m.node_tree.nodes['Principled BSDF']; p.inputs['Base Color'].default_value=rgba(color)
   p.inputs['Roughness'].default_value=.78 if role in {'Fabric','Wood','Body','Accent'} else .32
   if role=='Metal': p.inputs['Metallic'].default_value=.8
   if role=='Glass':
    p.inputs['Transmission Weight'].default_value=.88; p.inputs['Roughness'].default_value=.09; p.inputs['IOR'].default_value=1.45
   if role=='Screen': p.inputs['Roughness'].default_value=.19
   MATS[palette][role]=m

def finish(o,name,role):
 o.name=name
 for c in list(o.users_collection): c.objects.unlink(o)
 asset.objects.link(o); o.parent=root; o['Role']=role; o.data.materials.clear(); o.data.materials.append(MATS['Cream'][role]); PARTS.append(o)
 return o
def apply(o,mod):
 bpy.context.view_layer.objects.active=o; bpy.ops.object.modifier_apply(modifier=mod.name)
def box(name,size,loc,role='Body',bev=.012,rot=None):
 bpy.ops.mesh.primitive_cube_add(size=1,location=loc); o=bpy.context.object; o.scale=size
 bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
 if bev:
  b=o.modifiers.new('LowPolyEdge','BEVEL'); b.width=min(bev,min(size)*.24); b.segments=1; apply(o,b)
 if rot:o.rotation_euler=rot
 return finish(o,name,role)
def mesh(name,verts,faces,role='Body'):
 m=bpy.data.meshes.new(name); m.from_pydata(verts,[],faces); m.update()
 o=bpy.data.objects.new(name,m); asset.objects.link(o); return finish(o,name,role)
def cyl(name,r,depth,loc,role='Metal',n=12,rot=None,r2=None):
 bpy.ops.mesh.primitive_cone_add(vertices=n,radius1=r,radius2=r if r2 is None else r2,depth=depth,location=loc)
 o=bpy.context.object
 if rot:o.rotation_euler=rot
 return finish(o,name,role)
def rod(name,a,b,r=.015,role='Metal',n=8):
 a,b=Vector(a),Vector(b); o=cyl(name,r,(b-a).length,(a+b)/2,role,n)
 o.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler();return o
def ring(name,r,t,loc,role='Metal',rot=None,n=20):
 bpy.ops.mesh.primitive_torus_add(major_segments=n,minor_segments=4,location=loc,major_radius=r,minor_radius=t)
 o=bpy.context.object
 if rot:o.rotation_euler=rot
 return finish(o,name,role)
def lathe(name,rings,loc=(0,0,0),role='Body',n=16,scale=(1,1)):
 # Rings are (radius,z). Include centre radii for closed bowls and hollow rims.
 v=[];loops=[];f=[]
 for r,z in rings:
  ids=[]
  for i in range(1 if r==0 else n):
   ids.append(len(v));v.append((loc[0]+r*cos(i*2*pi/n)*scale[0],loc[1]+r*sin(i*2*pi/n)*scale[1],loc[2]+z))
  loops.append(ids)
 for a,b in zip(loops,loops[1:]):
  for i in range(n):
   if len(a)==1:f.append((a[0],b[(i+1)%n],b[i]))
   elif len(b)==1:f.append((a[i],a[(i+1)%n],b[0]))
   else:f.append((a[i],a[(i+1)%n],b[(i+1)%n],b[i]))
 return mesh(name,v,f,role)
def handle(name,x,y,z,width=.12,vertical=False):
 if vertical:
  rod(name,(x,y,z-width/2),(x,y,z+width/2),.012)
  for dz in (-width/2,width/2): rod(name+'Mount',(x,y+.03,z+dz),(x,y,z+dz),.01)
 else:
  rod(name,(x-width/2,y,z),(x+width/2,y,z),.012)
  for dx in (-width/2,width/2):rod(name+'Mount',(x+dx,y+.03,z),(x+dx,y,z),.01)
def legs(w,d,h,role='Wood',radius=.03,z=0):
 for x in (-w/2,w/2):
  for y in (-d/2,d/2):box('Leg',(radius*2,radius*2,h),(x,y,z+h/2),role,.009)
def tabletop(w,d,h,role='Wood',thick=.045):return box('Tabletop',(w,d,thick),(0,0,h-thick/2),role,.018)
def drawer(w,d,z,h,x=0):
 box('DrawerFront',(w-.012,.025,h-.012),(x,-d/2-.003,z),'Body',.009);handle('DrawerPull',x,-d/2-.041,z,w*.32)
def carcass(w,d,h,z=0,role='Wood'):
 box('Bottom',(w,d,.04),(0,0,z+.02),role)
 box('Top',(w,d,.04),(0,0,z+h-.02),role)
 for x in (-w/2+.02,w/2-.02):box('Side',(.04,d,h-.08),(x,0,z+h/2),role)
 box('Back',(w-.08,.025,h-.08),(0,d/2-.015,z+h/2),role,.005)
def doors(w,d,h,z=0,count=2,closed_top=None):
 bottom=z+.008;top=z+h-.008 if closed_top is None else closed_top
 for i in range(count):
  x=-w/2+(i+.5)*w/count
  box('Door',(w/count-.012,.03,top-bottom),(x,-d/2-.015,(top+bottom)/2),'Body',.004 if closed_top else .008)
  handle('DoorPull',x+w/count*.31,-d/2-.06,z+h*.53,.16,True)
 if closed_top is not None:
  box('ClosedTopRail',(w-.035,.065,.065),(0,-d/2+.029,closed_top-.0325),'Body',.002)
  root['ClosedDoorTopMetres']=closed_top

def cushion(name,size,loc,role='Fabric',rot=None,back_contour=False,outline_power=.5):
 # Sparse superellipsoid upholstery: rounded edges and a genuinely inflated
 # surface instead of a bevelled cuboid. Smooth normals preserve soft padding.
 n=24;bands=12;v=[]
 def sp(x,e):return math.copysign(abs(x)**e,x)
 for j in range(1,bands):
  lat=-pi/2+j*pi/bands
  for i in range(n):
   a=i*2*pi/n
   x=sp(cos(lat),.55)*sp(cos(a),outline_power)*size[0]/2
   y=sp(cos(lat),.55)*sp(sin(a),outline_power)*size[1]/2
   z=sp(sin(lat),.55)*size[2]/2
   if back_contour:
    t=z/(size[2]/2)
    x*=.86+.13*math.exp(-((t-.35)/.45)**2)
    # The lower front curves forward to support the lumbar region.
    if y<0:y-=.028*math.exp(-((t+.48)/.30)**2)*(-y/(size[1]/2))
   v.append((x,y,z))
 low=len(v);v.append((0,0,-size[2]/2));high=len(v);v.append((0,0,size[2]/2));f=[]
 for j in range(bands-2):
  for i in range(n):f.append((j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i))
 for i in range(n):
  f.append((low,(i+1)%n,i));a=(bands-2)*n;f.append((high,a+i,a+(i+1)%n))
 o=mesh(name,v,f,role);o.location=loc
 if rot:o.rotation_euler=rot
 for face in o.data.polygons:face.use_smooth=True
 return o

def rounded_loft(name,profiles,role='Wood',loc=(0,0,0)):
 # Closed joined profiles, each (z,width,depth,corner_radius), for furniture
 # edges and sculpted supports that remain editable low-poly geometry.
 v=[]
 for z,w,d,r in profiles:
  for cx,cy,start in ((w/2-r,d/2-r,0),(-w/2+r,d/2-r,90),(-w/2+r,-d/2+r,180),(w/2-r,-d/2+r,270)):
   for i in range(5):
    a=math.radians(start+i*22.5);v.append((cx+r*cos(a)+loc[0],cy+r*sin(a)+loc[1],z+loc[2]))
 n=20;f=[tuple(reversed(range(n))),tuple(range((len(profiles)-1)*n,len(profiles)*n))]
 for j in range(len(profiles)-1):
  for i in range(n):f.append((j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i))
 return mesh(name,v,f,role)
def faucet(x=0,y=.22,z=0):
 cyl('FaucetBase',.035,.018,(x,y,z+.009))
 rod('FaucetStem',(x,y,z),(x,y,z+.23),.022)
 rod('FaucetSpout',(x,y,z+.23),(x,y-.16,z+.23),.022)
 rod('FaucetNozzle',(x,y-.16,z+.23),(x,y-.16,z+.19),.025)
 box('MixerLever',(.018,.09,.016),(x+.04,y-.01,z+.14),'Metal',.005)
def basin(name,w,d,depth,z,role='White',x=0,y=0):
 # A continuous watertight rectangular bowl: top ring, inner walls, bottom, underside.
 def rect(a,b,zz):return [(-a/2,-b/2,zz),(a/2,-b/2,zz),(a/2,b/2,zz),(-a/2,b/2,zz)]
 levels=[rect(w,d,z),rect(w-.05,d-.05,z),rect(w*.65,d*.65,z-depth),rect(w*.65+.02,d*.65+.02,z-depth-.018),rect(w,d,z-.018)]
 v=[(xx+x,yy+y,zz) for level in levels for xx,yy,zz in level];f=[]
 for a,b in [(0,1),(1,2),(3,4),(4,0)]:
  for i in range(4):f.append((a*4+i,a*4+(i+1)%4,b*4+(i+1)%4,b*4+i))
 f.extend([(8,9,10,11),(15,14,13,12)])
 o=mesh(name,v,f,role);cyl('Drain',.025,.003,(x,y,z-depth+.002),'Dark',12);return o

def approved_bed(double=False):
 src=HERE.parent/'SingleBed/SingleBed.blend'
 with bpy.data.libraries.load(str(src),link=False) as (a,b):b.collections=['SingleBed_ASSET']
 col=b.collections[0]
 scene.collection.children.link(col)
 bpy.context.view_layer.update()
 mapping={'Frame':'Wood','Mattress':'White','Duvet':'Fabric','Fold':'Accent','Pillow':'White'}
 for o in list(col.objects):
  if o.type!='MESH':continue
  transform=o.matrix_world.copy();o.parent=None;o.matrix_world=transform
  role=mapping[o.get('MaterialRole','Frame')]
  finish(o,o.name,role)
  if double:
   if o.name.startswith('Pillow'):
    o.location.x=-.35
    cp=o.copy();cp.data=o.data.copy();asset.objects.link(cp);cp.location.x=.35;PARTS.append(cp)
   else:
    o.location.x*=1.4
    for v in o.data.vertices:v.co.x*=1.4
 bpy.data.collections.remove(col)
@model('SingleBed','싱글침대','침실',function='Sleep',note='Approved SingleBed geometry reused')
def _():approved_bed()
@model('DoubleBed','더블침대','침실',function='Sleep')
def _():approved_bed(True)
@model('FloorMattress','바닥용 매트리스','침실',function='Sleep')
def _():
 box('Mattress',(1,2,.16),(0,0,.08),'White',.055)
 box('Cover',(1.01,1.48,.035),(0,-.25,.165),'Fabric',.025)
 box('Fold',(1.02,.19,.035),(0,.42,.187),'Accent',.02)
 box('Pillow',(.66,.40,.13),(0,.73,.22),'White',.055)
@model('Nightstand','침대 옆 협탁','침실',function='Storage, Surface')
def _():
 legs(.36,.31,.12);carcass(.46,.40,.43,.12);drawer(.42,.40,.43,.20)
 box('Shelf',(.38,.35,.025),(0,0,.23),'Wood')
@model('Vanity','소형 화장대와 거울','침실',function='Storage, Surface')
def _():
 # Matched curved apron and top, inset drawers and tapered legs form one
 # coherent dressing table; a thin oval frame replaces the stacked slabs.
 def rounded_slab(name,w,d,h,z,r,role):
  outline=[]
  for cx,cy,start in ((w/2-r,d/2-r,0),(-w/2+r,d/2-r,90),(-w/2+r,-d/2+r,180),(w/2-r,-d/2+r,270)):
   for i in range(5):
    a=math.radians(start+i*90/4);outline.append((cx+r*cos(a),cy+r*sin(a)))
  n=len(outline);v=[(x,y,zz) for zz in (z-h/2,z+h/2) for x,y in outline]
  f=[tuple(reversed(range(n))),tuple(range(n,2*n))]
  f += [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
  return mesh(name,v,f,role)
 rounded_slab('ContinuousApron',.85,.405,.125,.665,.055,'Wood')
 rounded_slab('RoundedTop',.90,.45,.032,.744,.065,'Wood')
 for x in (-1,1):
  for y in (-1,1):
   a=Vector((x*.375,y*.165,.018));b=Vector((x*.344,y*.133,.635))
   o=cyl('TaperedLeg',.018,(b-a).length,(a+b)/2,'Wood',8,r2=.028)
   o.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler()
 for x in (-.195,.195):
  box('InsetDrawer',(.382,.014,.096),(x,-.207,.663),'Body',.004)
  cyl('SmallDrawerKnob',.013,.018,(x,-.228,.663),'Metal',12,(pi/2,0,0))
 # The mirror sits in a shallow rear socket. The support is tucked behind it.
 box('MirrorSocket',(.15,.065,.075),(0,.15,.783),'Wood',.014)
 n=32;v=[]
 for rx,rz,y in ((.255,.295,.128),(.232,.272,.126),(.232,.272,.162),(.255,.295,.162)):
  v.extend([(rx*cos(i*2*pi/n),y,1.065+rz*sin(i*2*pi/n)) for i in range(n)])
 f=[]
 for a,b in ((0,1),(1,2),(2,3),(3,0)):
  for i in range(n):f.append((a*n+i,a*n+(i+1)%n,b*n+(i+1)%n,b*n+i))
 mesh('OvalMirrorFrame',v,f,'Wood')
 v=[(.233*cos(i*2*pi/n),y,1.065+.273*sin(i*2*pi/n)) for y in (.132,.138) for i in range(n)]
 f=[tuple(reversed(range(n))),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
 mesh('OvalMirrorGlass',v,f,'Metal')
@model('FullMirror','전신거울','침실')
def _():
 from mathutils import Matrix
 tilt=Matrix.Rotation(math.radians(-10),3,'X');pivot=Vector((0,0,.06))
 for o in [box('MirrorFrame',(.54,.045,1.6),(0,0,.86),'Body',.025),
           box('Mirror',(.47,.008,1.52),(0,-.028,.86),'Metal',.016)]:
  o.location=pivot+tilt@(o.location-pivot);o.rotation_euler=tilt.to_euler()
 for x in (-.21,.21):
  attach=pivot+tilt@(Vector((x,.025,1.14))-pivot)
  rod('RearStand',attach,(x,.48,.025),.018,'Wood')
 rod('BackBrace',(-.21,.48,.04),(.21,.48,.04),.018,'Wood')
 box('FrontFoot',(.56,.10,.06),(0,0,.03),'Wood')
 root['BackwardTiltDegrees']=10.
@model('DryingRack','빨래건조대','생활')
def _():
 for x in (-.47,.47):
  rod('CrossLeg',(x,-.34,.02),(x,.32,1.02),.015)
  rod('CrossLeg',(x,.34,.02),(x,-.32,1.02),.015)
  cyl('Hinge',.035,.035,(x,0,.52),'Body',12,(0,pi/2,0))
 for y in (-.32,.32):rod('TopRail',(-.5,y,1.02),(.5,y,1.02),.018)
 for x in [i*.12 for i in range(-4,5)]:rod('DryingBar',(x,-.32,1.02),(x,.32,1.02),.008,'Body')
@model('LaundryBasket','빨래바구니','생활',function='Storage')
def _():
 # One tapered, perforated moulded shell, with a solid bottom, thick rolled
 # lip, and integrated hand openings in the two short ends.
 n=48;levels=[.025,.055,.085,.11,.14,.165,.195,.22,.25,.275,.305,.33,.36,.405,.445]
 v=[]
 for z in levels:
  rx=.20+.06*z/.445;ry=.14+.045*z/.445
  v.extend([(rx*cos(i*2*pi/n),ry*sin(i*2*pi/n),z) for i in range(n)])
 f=[]
 for j in range(len(levels)-1):
  for i in range(n):
   vent=j in (2,4,6,8,10) and (i+(3 if j in (4,8) else 0))%6<4
   hand=j==12 and (i in (45,46,47,0,1,2,21,22,23,24,25,26))
   if not vent and not hand:f.append((j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i))
 shell=mesh('PerforatedBasketShell',v,f,'Body')
 solid=shell.modifiers.new('MouldedWallThickness','SOLIDIFY');solid.thickness=.008;solid.offset=-1;apply(shell,solid)
 base=cyl('SolidBasketBottom',1,.030,(0,0,.015),'Body',48);base.scale=(.204,.143,1)
 # Oval rolled rim is a continuous loop, not an assembly of straight sticks.
 v=[]
 for i in range(n):
  a=i*2*pi/n
  for j in range(6):
   b=j*2*pi/6
   v.append(((.26+.012*cos(b))*cos(a),(.185+.012*cos(b))*sin(a),.445+.012*sin(b)))
 f=[(i*6+j,((i+1)%n)*6+j,((i+1)%n)*6+(j+1)%6,i*6+(j+1)%6) for i in range(n) for j in range(6)]
 mesh('RolledOpeningRim',v,f,'Body')
 root['Construction']='Tapered open-top shell, 40 ventilation slots, two integrated carry holes'

def sofa(seats=2,corner=False):
 w=seats*.62+.24
 legs(w-.20,.62,.15,'Wood',.04)
 box('LowerFrame',(w-.02,.84,.20),(0,0,.24),'Body',.045)
 for x in (-w/2+.09,w/2-.09):box('Arm',(.18,.86,.48),(x,0,.47),'Fabric',.065)
 for i in range(seats):
  x=-w/2+.12+(i+.5)*.62
  box('SeatCushion',(.60,.66,.16),(x,-.07,.41),'Fabric',.055)
  box('BackCushion',(.60,.18,.48),(x,.32,.64),'Fabric',.06,(-.12,0,0))
 if corner:
  x=w/2-.43;box('ChaiseBase',(.70,.92,.21),(x,-.80,.25),'Body',.045)
  box('ChaiseCushion',(.68,.92,.16),(x,-.80,.43),'Fabric',.055)
  for xx in (x-.26,x+.26):box('ChaiseFoot',(.07,.07,.16),(xx,-1.17,.08),'Wood')
 box('AccentPillow',(.35,.15,.34),(-w/2+.39,.18,.64),'Accent',.06,(.1,.12,-.12))
@model('Sofa2','2인용 소파','거실',function='Seating')
def _():sofa(2)
@model('Sofa3','3인용 소파','거실',function='Seating')
def _():sofa(3)
@model('Armchair','1인용 안락의자','거실',function='Seating')
def _():
 # Tailored tub armchair: base, arms and back are ONE continuous upholstered
 # surface. A separate fitted seat pad is the only visible cushion seam.
 def outline(w,d,r):
  corners=[(w/2-r,d/2-r,0),(-w/2+r,d/2-r,90),(-w/2+r,-d/2+r,180),(w/2-r,-d/2+r,270)]
  points=[]
  for k,(cx,cy,start) in enumerate(corners):
   arc=[(cx+r*cos(math.radians(start+i*22.5)),cy+r*sin(math.radians(start+i*22.5))) for i in range(5)]
   points.extend(arc)
   nx,ny,ns=corners[(k+1)%4];end=(nx+r*cos(math.radians(ns)),ny+r*sin(math.radians(ns)))
   a=arc[-1]
   points.extend([(a[0]+(end[0]-a[0])*j/6,a[1]+(end[1]-a[1])*j/6) for j in range(1,6)])
  return points
 def smooth(a,b,x):
  t=max(0,min(1,(x-a)/(b-a)));return t*t*(3-2*t)
 reference=outline(1.18,.92,.18)
 heights=[]
 for x,y in reference:
  arm=smooth(.28,.49,abs(x));back=smooth(.02,.36,y)
  heights.append(.345+.29*arm*(1-back)+.585*back)
 profiles=[(1.03,.79,.13,.11,'flat',0),(1.035,.795,.135,.116,'flat',0),
           (1.10,.855,.16,.155,'flat',0),(1.16,.91,.18,.235,'flat',0),
           (1.18,.92,.18,-.070,'rim',0),(1.13,.875,.17,0,'rim',0),
           (.955,.735,.14,-.015,'rim',-.012),(.88,.665,.13,-.085,'rim',-.02),
           (.84,.62,.12,.30,'flat',-.03)]
 v=[]
 for w,d,r,z,kind,shift_y in profiles:
  for i,(x,y) in enumerate(outline(w,d,r)):v.append((x,y+shift_y,z if kind=='flat' else heights[i]+z))
 n=len(reference);f=[tuple(reversed(range(n))),tuple(range((len(profiles)-1)*n,len(profiles)*n))]
 for j in range(len(profiles)-1):
  for i in range(n):f.append((j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i))
 shell=mesh('ContinuousUpholsteredShell',v,f,'Fabric')
 sub=shell.modifiers.new('TailoredContinuousCurves','SUBSURF');sub.levels=1;sub.render_levels=1;apply(shell,sub)
 for face in shell.data.polygons:face.use_smooth=True
 cushion('FittedSeatCushion',(.84,.61,.125),(0,-.065,.3625),outline_power=.5)
 for x in (-.435,.435):
  for y in (-.285,.285):cyl('RecessedTaperedFoot',.025,.15,(x,y,.075),'Wood',8,r2=.035)
 root['SeatCushionWidthMetres']=.84
 root['Construction']='One continuous upholstered base/arms/back shell, one fitted seat pad, four recessed feet'
@model('CornerSofa','고급 코너형 소파','거실',function='Seating')
def _():sofa(3,True)
@model('LowTable','좌식 테이블','거실',function='Surface')
def _():
 tabletop(1.0,.60,.34);legs(.80,.42,.30,'Wood',.035)
@model('TVStand','낮은 TV장','거실',function='Storage, Surface')
def _():
 legs(1.40,.30,.10);carcass(1.6,.42,.36,.10)
 for x in (-.52,.52):
  box('Door',(.48,.025,.28),(x,-.22,.28),'Body');handle('Pull',x,-.26,.33,.20)
 for x in (-.27,.27):box('Divider',(.025,.36,.28),(x,0,.28),'Wood')
 box('MediaShelf',(.50,.36,.025),(0,0,.27),'Wood')
@model('ShoeCabinet','신발장','수납',function='Storage')
def _():
 carcass(.80,.36,1.12);doors(.80,.36,1.04,.05)
 for z in (.3,.58,.85):box('ShoeShelf',(.72,.30,.024),(0,0,z),'Wood')
@model('DisplayCabinet','피규어 진열장','수납',function='Storage, Display',note='Clear glass door, four usable shelves; no figures included')
def _():
 carcass(.82,.40,1.65,role='Body')
 for z in (.30,.65,1.0,1.35):box('DisplayShelf',(.74,.34,.024),(0,0,z),'White',.004)
 for x in (-.38,.38):box('DoorFrame',(.035,.025,1.56),(x,-.214,.825),'Wood',.004)
 for z in (.05,1.60):box('DoorFrame',(.78,.025,.035),(0,-.214,z),'Wood',.004)
 box('ClearGlass',(.72,.008,1.50),(0,-.214,.825),'Glass',0);handle('GlassDoorPull',.29,-.25,.82,.16,True)

def desk(w=1.15,d=.60,h=.74):
 tabletop(w,d,h);legs(w-.14,d-.14,h-.045,'Wood',.027)
 box('BackBrace',(w-.12,.025,.12),(0,d/2-.05,h-.16),'Body')
@model('StudyDeskHutch','책장 딸린 공부용 책상','책상',function='Surface, Storage')
def _():
 desk(1.1,.58);drawer(.44,.54,.62,.16,x=.27)
 for x in (-.52,.52):box('HutchSide',(.035,.24,.78),(x,.16,1.12),'Wood')
 for z in (.97,1.24,1.51):box('HutchShelf',(1.08,.24,.025),(0,.16,z),'Wood')
 box('HutchDivider',(.025,.24,.53),(.10,.16,1.24),'Body')
@model('PlainDesk','일반 책상','책상',function='Surface')
def _():desk(1.40,.75)
def gaming_desk(kind):
 w,d,h=1.3,.65,.75
 if kind==3:w=1.5;d=.70
 tabletop(w,d,h,'Dark',.05)
 for x in (-w/2+.04,w/2-.04):box('AccentEdge',(.025,d-.05,.015),(x,0,h+.001),'Accent',.006)
 if kind==1:
  for x in (-.53,.53):
   box('TFoot',(.10,.62,.045),(x,0,.0225),'Dark')
   box('TLeg',(.07,.07,.68),(x,.03,.38),'Metal')
 elif kind==2:
  for x in (-.53,.53):
   rod('ZLeg',(x,-.24,.045),(x,.20,.36),.038,'Body')
   rod('ZLeg',(x,.20,.36),(x,-.18,.70),.038,'Body')
   box('ZFoot',(.09,.60,.045),(x,0,.0225),'Dark')
 else:
  for x in (-.64,.64):
   box('HeightColumn',(.085,.10,.68),(x,.05,.38),'Body')
   box('HeightFoot',(.12,.64,.05),(x,.02,.025),'Dark')
  box('ControlPad',(.12,.055,.025),(.49,-.33,.70),'Dark')
  box('RearRiser',(1.15,.19,.035),(0,.23,.88),'Body')
  for x in (-.45,.45):box('RiserLeg',(.035,.15,.10),(x,.23,.81),'Body')
 box('CableTray',(.70,.13,.055),(0,.21,.65),'Dark')
 ring('CableGrommet',.028,.006,(.42,.23,.755),'Metal',n=12)
@model('GamingDeskT','컴퓨터 책상 01 T프레임','책상',function='Surface',note='Desk only, no computer')
def _():gaming_desk(1)
@model('GamingDeskZ','컴퓨터 책상 02 Z프레임','책상',function='Surface',note='Desk only, no computer')
def _():gaming_desk(2)
@model('GamingDeskLift','컴퓨터 책상 03 높이조절형','책상',function='Surface',note='Desk only; static height-adjustable design')
def _():gaming_desk(3)

@model('WoodChair','기본 목제 의자','의자',function='Seating')
def _():
 legs(.36,.36,.44,'Wood',.022);box('Seat',(.45,.43,.045),(0,0,.46),'Wood')
 for x in (-.19,.19):box('BackPost',(.04,.04,.48),(x,.18,.68),'Wood')
 for z in (.67,.84):box('BackSlat',(.40,.035,.09),(0,.18,z),'Body')
def swivel(gaming=False,massage=False):
 cyl('Hub',.09,.12,(0,0,.13),'Dark')
 for i in range(5):
  a=2*pi*i/5;x=.30*cos(a);y=.30*sin(a)
  rod('StarBase',(0,0,.15),(x,y,.09),.027,'Dark' if gaming else 'Metal')
  if gaming:
   for side in (-.018,.018):cyl('TwinCaster',.045,.020,(x-side*sin(a),y+side*cos(a),.045),'Rubber',12,(pi/2,0,a))
  else:cyl('Caster',.045,.045,(x,y,.045),'Rubber',10,(pi/2,0,a))
 cyl('GasLift',.027,.25,(0,0,.30),'Metal')
 if gaming:
  from mathutils import Matrix
  tilt=Matrix.Rotation(-math.radians(12),3,'X');anchor=Vector((0,.19,.49))
  rounded_loft('MouldedSeatPan',[(.395,.47,.44,.07),(.415,.55,.53,.08),(.447,.55,.53,.08)],'Dark',(0,-.035,0))
  seat=cushion('BucketSeatPad',(.49,.48,.115),(0,-.045,.495))
  for vertex in seat.data.vertices:
   # Raised, padded thigh supports are sculpted into the same seat surface.
   vertex.co.z+=.038*(abs(vertex.co.x)/.245)**4
  # A single closed high-back mesh with shoulder wings, a narrow waist,
  # integrated head support and two real strap openings.
  profiles=[(0,.43),(.04,.48),(.14,.49),(.24,.43),(.36,.41),(.48,.46),
            (.58,.58),(.64,.59),(.70,.53),(.75,.37),(.80,.36),(.85,.34),(.875,.28)]
  us=[-1,-.90,-.72,-.55,-.30,0,.30,.55,.72,.90,1];nx=len(us);nz=len(profiles)
  v=[]
  for back in (False,True):
   for z,width in profiles:
    for u in us:
     lumbar=.035*math.exp(-((z-.22)/.13)**2)*(1-u*u)
     head=.025*math.exp(-((z-.80)/.065)**2)*(1-u*u)
     wing=.095*max(0,(abs(u)-.50)/.50)**1.25*(1-.8*max(0,(z-.70)/.175))
     y=.058 if back else -.028-.015*(1-u*u)-lumbar-head-wing
     v.append((u*width/2,y,z))
  offset=nx*nz;f=[];material_ids=[]
  def face(indices,material=1):f.append(tuple(indices));material_ids.append(material)
  for j in range(nz-1):
   for i in range(nx-1):
    a=j*nx+i;quad=(a,a+1,a+1+nx,a+nx)
    if j==7 and i in (3,6):
     for k in range(4):face((quad[k],quad[(k+1)%4],quad[(k+1)%4]+offset,quad[k]+offset))
    else:
     face(quad,1 if abs((us[i]+us[i+1])/2)>.65 else 0)
     face(tuple(k+offset for k in reversed(quad)))
  for j in range(nz-1):
   for i in (0,nx-1):
    a=j*nx+i;b=(j+1)*nx+i;face((a,b,b+offset,a+offset))
  for j in (0,nz-1):
   for i in range(nx-1):
    a=j*nx+i;face((a,a+offset,a+1+offset,a+1))
  back=mesh('SculptedGamingBack',v,f,'Fabric');back.data.materials.append(MATS['Cream']['Dark'])
  back['MaterialRoles']=['Fabric','Dark']
  for poly,slot in zip(back.data.polygons,material_ids):poly.material_index=slot
  import bmesh
  bm=bmesh.new();bm.from_mesh(back.data);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(back.data);bm.free()
  sub=back.modifiers.new('SoftRacingSeatContours','SUBSURF');sub.levels=2;sub.render_levels=2;apply(back,sub)
  reduce=back.modifiers.new('GameReadyContours','DECIMATE');reduce.ratio=.68;reduce.use_collapse_triangulate=True;apply(back,reduce)
  for poly in back.data.polygons:poly.use_smooth=True
  back.location=anchor;back.rotation_euler=tilt.to_euler()
  spine=box('RearSupportSpine',(.075,.027,.34),(0,0,0),'Dark',.009)
  spine.location=anchor+tilt@Vector((0,.067,.24));spine.rotation_euler=tilt.to_euler()
  for x in (-.32,.32):
   box('ArmMount',(.12,.065,.035),(x*.84,.04,.415),'Dark',.009)
   box('ArmHeightSleeve',(.043,.052,.20),(x,.04,.53),'Dark',.008)
   cyl('ArmTelescopingPost',.012,.085,(x,.04,.655),'Metal',10)
   cushion('AdjustableArmPad',(.095,.27,.040),(x,-.02,.704),'Dark')
   box('ArmHeightButton',(.014,.016,.032),(x+math.copysign(.027,x),.015,.59),'Metal',.003)
  for x in (-.247,.247):
   cyl('ReclineHinge',.045,.032,(x,.18,.49),'Dark',16,(0,pi/2,0))
   cyl('HingeBolt',.016,.036,(x,.18,.49),'Metal',10,(0,pi/2,0))
  box('SeatMechanism',(.20,.22,.055),(0,.03,.374),'Dark',.012)
  rod('ReclineLever',(.10,.08,.38),(.26,.08,.38),.009,'Metal')
  box('LeverGrip',(.07,.03,.025),(.27,.08,.38),'Dark',.007)
  root['BackReclineDegrees']=12.
  root['BackFeatures']='High shoulder wings, tapered waist, integrated padded headrest, two open strap slots'
  root['Controls']='Static modelled height-adjustable armrests, recline hinges and adjustment lever'
  return
 box('Seat',(.50,.49,.10),(0,-.02,.47),'Fabric',.04)
 box('Back',(.46,.11,.65 if gaming else .46),(0,.22,.80 if gaming else .73),'Fabric',.04,(-.10,0,0))
 for x in (-.30,.30):
  rod('ArmPost',(x,.08,.45),(x,.08,.65),.018,'Dark')
  box('ArmPad',(.08,.32,.035),(x,-.01,.67),'Dark')
@model('GamingChair','컴퓨터용 의자','의자',function='Seating')
def _():swivel(True)
@model('OfficeChair','사무용 의자','의자',function='Seating')
def _():swivel()
@model('PlasticChair','플라스틱 의자','의자',function='Seating')
def _():
 legs(.36,.34,.43,'Body',.025);box('MoldedSeat',(.45,.44,.045),(0,0,.45),'Body',.018)
 for x in (-.19,.19):box('BackSide',(.065,.035,.40),(x,.18,.66),'Body')
 for z in (.60,.72,.84):box('BackSlat',(.34,.035,.065),(0,.18,z),'Body')
@model('RoundStool','원형 스툴','의자',function='Seating')
def _():
 cyl('RoundSeat',.22,.06,(0,0,.44),'Body',20)
 for a in (0,2*pi/3,4*pi/3):rod('Leg',(.16*cos(a),.16*sin(a),.02),(.12*cos(a),.12*sin(a),.41),.025,'Wood')
 ring('Brace',.14,.012,(0,0,.18),'Wood',n=16)
@model('BarChair','바 의자','의자',function='Seating')
def _():
 cyl('Pedestal',.24,.035,(0,0,.0175),'Metal',20)
 cyl('Stem',.035,.65,(0,0,.35),'Metal');ring('Footrest',.18,.015,(0,0,.28),'Metal')
 # A single curved bucket wraps from the seat into a low back and side wings.
 n=32;v=[(0,0,.705)];f=[]
 for radius in (.35,.70,1.):
  for i in range(n):
   a=2*pi*i/n
   edge=.055+.235*max(0,sin(a))**1.5+.045*abs(cos(a))**4
   v.append((.255*radius*cos(a),.235*radius*sin(a),.705+edge*radius**3))
 for i in range(n):f.append((0,1+i,1+(i+1)%n))
 for j in range(2):
  a=1+j*n;b=a+n
  for i in range(n):f.append((a+i,a+(i+1)%n,b+(i+1)%n,b+i))
 o=mesh('ContinuousBucketShell',v,f,'Body')
 solid=o.modifiers.new('BucketThickness','SOLIDIFY');solid.thickness=.023;solid.offset=-1;apply(o,solid)
 for face in o.data.polygons:face.use_smooth=True
 cushion('SeatPad',(.36,.28,.08),(0,-.02,.755),outline_power=1.0)
@model('MassageChair','안마의자','의자',function='Seating')
def _():
 box('Base',(.73,.85,.12),(0,0,.06),'Dark',.04)
 box('Seat',(.55,.57,.18),(0,-.04,.43),'Fabric',.06)
 box('ReclinedBack',(.64,.24,.86),(0,.29,.80),'Body',.07,(-.25,0,0))
 box('BackPadding',(.46,.13,.70),(0,.14,.81),'Fabric',.045,(-.25,0,0))
 for x in (-.39,.39):box('ArmPod',(.22,.77,.43),(x,-.03,.48),'Body',.07)
 for x in (-.145,.145):
  box('CalfPod',(.27,.38,.38),(x,-.57,.28),'Body',.05,(.24,0,0))
  box('CalfPad',(.16,.10,.24),(x,-.76,.30),'Fabric',.04,(.24,0,0))
 box('Headrest',(.33,.12,.18),(0,.18,1.13),'Accent',.045)
 box('Remote',(.075,.16,.025),(.40,-.22,.71),'Dark',.008)

@model('Drawer3','3단 서랍장','수납',function='Storage')
def _():
 legs(.67,.34,.10);carcass(.80,.44,.72,.10)
 for z in (.24,.47,.70):drawer(.73,.44,z,.22)
@model('Wardrobe','큰 옷장','수납',function='Storage')
def _():
 carcass(1.60,.60,2.10);doors(1.60,.60,1.98,.06,4,closed_top=2.099)
 for x in (-.39,.39):box('InternalDivider',(.025,.54,2.0),(x,0,1.05),'Wood')
 rod('HangingRail',(-.73,0,1.75),(.73,0,1.75),.015)
@model('Bookcase','책장','수납',function='Storage')
def _():
 carcass(.90,.32,1.80)
 for z in (.36,.71,1.06,1.41):box('Shelf',(.82,.28,.03),(0,0,z),'Wood')
 box('Divider',(.025,.28,.67),(.05,0,1.41),'Body')
@model('WallCabinet','벽 부착 수납장','수납','Wall','Storage')
def _():
 carcass(.90,.28,.60);doors(.90,.28,.53,.035,closed_top=.599)

def kitchen_base(cutout=False):
 w,d,h=.8,.60,.90
 box('ToeKick',(.74,.49,.10),(0,.025,.05),'Dark')
 # Open cabinet shell; the sink cavity is never closed by a hidden top mesh.
 box('Bottom',(.80,.60,.035),(0,0,.12),'Body')
 for x in (-.3825,.3825):box('Side',(.035,.60,.745),(x,0,.49),'Body')
 box('Back',(.73,.025,.745),(0,.2875,.49),'Body')
 doors(.8,.6,.72,.13,closed_top=.859)
 if cutout:
  # Hole x +/- .29, y +/- .205, exact countertop through-opening.
  for x in (-.3525,.3525):box('CutoutCounterSide',(.125,.63,.04),(x,0,.88),'White',.004)
  for y in (-.26,.26):box('CutoutCounterRail',(.58,.11,.04),(0,y,.88),'White',.004)
  root['SinkCutoutMetres']=[.58,.41];root['SinkInstallLocalZ']=.90
 else:box('Countertop',(.83,.63,.04),(0,0,.88),'White',.008)
 root['ModuleWidthMetres']=.8;root['CountertopHeightMetres']=.9
@model('KitchenBase','주방 하부장','주방',function='Storage, Surface')
def _():kitchen_base()
@model('SinkBase','싱크대 결합용 타공 하부장','주방',function='Storage, Surface',note='Additional assembly variant; actual 0.58 x 0.41m through-opening')
def _():kitchen_base(True)
@model('KitchenUpper','주방 상부장','주방','Wall','Storage')
def _():carcass(.80,.34,.70,role='Body');doors(.80,.34,.62,.04,closed_top=.699)
@model('RangeHood','레인지 후드','주방','Wall')
def _():
 box('HoodCanopy',(.70,.48,.10),(0,0,.05),'Body',.022)
 box('Chimney',(.26,.25,.48),(0,.10,.34),'Body',.012)
 box('Filter',(.51,.31,.012),(0,0,-.005),'Metal',.002)
 for x in (-.23,.23):cyl('UnderLight',.035,.009,(x,-.13,-.012),'White',12)
 for x in (-.08,0,.08):box('ControlButton',(.025,.008,.018),(x,-.244,.056),'Dark',.003)
@model('Induction','인덕션','주방','Surface','Cooking',note='Place root at countertop height 0.90m')
def _():
 box('HobFrame',(.60,.50,.030),(0,0,.015),'Body',.010)
 box('Hob',(.58,.48,.006),(0,0,.033),'Dark',.002)
 for x,y,r in [(-.14,.10,.105),(.14,.10,.085),(-.14,-.12,.075),(.14,-.12,.10)]:
  ring('CookingZone',r,.002,(x,y,.036),'Metal',n=24)
 for x in (-.09,0,.09):box('TouchControl',(.025,.006,.001),(x,-.218,.036),'Accent',0)
@model('Microwave','전자레인지','주방','Surface','Cooking')
def _():
 box('Housing',(.48,.37,.29),(0,0,.145),'Body',.02)
 box('Door',(.35,.018,.23),(-.045,-.195,.15),'Dark')
 box('DoorWindow',(.27,.006,.15),(-.06,-.207,.16),'Screen',.006)
 handle('DoorHandle',.087,-.23,.15,.15,True)
 box('Display',(.063,.008,.045),(.191,-.195,.23),'Screen',.004)
 for z in (.08,.14):cyl('Dial',.022,.02,(.19,-.208,z),'Metal',12,(pi/2,0,0))
@model('Sink','싱크대','주방','Surface','WaterSource',note='Root is counter insertion plane; basin extends down 0.20m. Fits SinkBase at z=0.90m.')
def _():
 # Flange underside sits on z=0. Bowl below the counter remains INSIDE
 # the 0.58 x 0.41m cutout with 10mm side clearance.
 specs=[(.62,.45,.012),(.54,.37,.012),(.38,.25,-.18),(.40,.27,-.20),(.56,.39,0),(.62,.45,0)]
 verts=[]
 for w,d,z in specs:verts.extend([(-w/2,-d/2,z),(w/2,-d/2,z),(w/2,d/2,z),(-w/2,d/2,z)])
 faces=[]
 for a,b in ((0,1),(1,2),(3,4),(4,5),(5,0)):
  for i in range(4):faces.append((a*4+i,a*4+(i+1)%4,b*4+(i+1)%4,b*4+i))
 faces.extend([(8,9,10,11),(15,14,13,12)])
 mesh('SinkBowl',verts,faces,'Metal');cyl('Drain',.025,.003,(0,0,-.178),'Dark',12)
 faucet(y=.27,z=0)
 root['InsertionPlaneZ']=0.;root['RequiredCutout']=[.58,.41];root['InstallOn']='SinkBase at local (0,0,0.90)'
@model('IslandTable','아일랜드 식탁','주방',function='Surface')
def _():
 rounded_loft('SoftEdgeIslandTop',[(.865,1.49,.76,.09),(.877,1.55,.82,.105),(.913,1.55,.82,.105),(.920,1.536,.806,.098)],'White')
 for x in (-.49,.49):
  rounded_loft('SculptedTrestle',[(0,.22,.56,.07),(.035,.22,.56,.07),(.12,.14,.44,.055),(.70,.115,.40,.05),(.84,.22,.46,.07),(.876,.27,.48,.075)],'Wood',(x,0,0))
 rod('FootRail',(-.49,-.19,.18),(.49,-.19,.18),.018,'Metal',12)
 root['UnderTop']='Open knee space; two sculpted trestles; no storage'
@model('RecyclingBins','분리수거함','생활',function='Storage')
def _():
 for x,mark in [(-.30,0),(0,1),(.30,2)]:
  box('Bin',(.28,.36,.50),(x,0,.25),'Body',.025)
  box('Lid',(.29,.37,.045),(x,0,.5225),'Accent',.014)
  box('Opening',(.18,.095,.006),(x,-.035,.547),'Dark',.018)
  for i in range(mark+1):box('SortingMark',(.025,.004,.045),(x+(i-mark/2)*.045,-.182,.37),'White',.003)
@model('Washer','세탁기','가전')
def _():
 box('Machine',(.60,.60,.85),(0,0,.425),'Body',.025)
 ring('DoorRim',.205,.025,(0,-.321,.40),'Metal',(pi/2,0,0),24)
 cyl('DoorGlass',.18,.025,(0,-.325,.40),'Screen',24,(pi/2,0,0))
 ring('InnerDrum',.145,.008,(0,-.342,.40),'Dark',(pi/2,0,0),20)
 box('ControlPanel',(.56,.02,.13),(0,-.308,.75),'White')
 cyl('ProgramDial',.036,.025,(.09,-.33,.75),'Metal',16,(pi/2,0,0))
 box('Display',(.12,.005,.045),(.22,-.323,.75),'Screen',.004)
 box('DetergentDrawer',(.20,.025,.08),(-.16,-.325,.75),'Body')
def fridge(mini=False):
 w,d,h=(.48,.48,.82) if mini else (.72,.67,1.80)
 box('FridgeBody',(w,d,h),(0,0,h/2),'Body',.025)
 if mini:
  box('Door',(w-.018,.045,h-.035),(0,-d/2-.012,h/2),'Body',.018)
  handle('Handle',w*.33,-d/2-.06,h*.68,.22,True)
 else:
  for z,hh in ((.56,1.08),(1.445,.65)):
   box('Door',(w-.018,.045,hh),(0,-d/2-.012,z),'Body',.018)
   handle('Handle',w*.33,-d/2-.06,z,.27,True)
  box('TemperatureDisplay',(.07,.008,.14),(-.18,-d/2-.04,1.43),'Screen')
@model('MiniFridge','소형 냉장고','가전',function='Storage')
def _():fridge(True)
@model('Fridge','기본 냉장고','가전',function='Storage')
def _():fridge()
@model('Oven','오븐','가전','Surface','Cooking')
def _():
 box('OvenBody',(.60,.56,.59),(0,0,.295),'Body',.018)
 box('OvenDoor',(.55,.025,.43),(0,-.295,.25),'Dark')
 box('GlassWindow',(.44,.007,.29),(0,-.312,.25),'Screen')
 handle('OvenPull',0,-.35,.43,.40)
 for x in (-.20,.20):cyl('OvenDial',.026,.025,(x,-.30,.535),'Metal',12,(pi/2,0,0))
 box('Timer',(.16,.01,.05),(0,-.291,.535),'Screen')
@model('Fan','선풍기','가전')
def _():
 cyl('Base',.23,.035,(0,0,.0175),'Body',20)
 cyl('Stand',.025,.65,(0,.04,.36),'Metal')
 cyl('Motor',.085,.15,(0,.06,.94),'Body',12,(pi/2,0,0))
 for r in (.11,.20,.27):ring('GuardRing',r,.006,(0,-.06,.94),'Metal',(pi/2,0,0),24)
 for i in range(12):
  a=i*pi/6;rod('GuardSpoke',(0,-.085,.94),(.27*cos(a),-.06,.94+.27*sin(a)),.004,'Metal',6)
 for i in range(3):
  a=i*2*pi/3
  verts=[]
  for x,z in ((.02,0),(.17,-.045),(.23,.055),(.11,.11)):
   verts.append((x*cos(a)-z*sin(a),-.025,.94+x*sin(a)+z*cos(a)))
  o=mesh('Blade',verts,[(0,1,2,3)],'Accent');s=o.modifiers.new('BladeThickness','SOLIDIFY');s.thickness=.008;apply(o,s)
 cyl('Hub',.045,.035,(0,-.09,.94),'Body',12,(pi/2,0,0))
@model('WallAC','벽걸이 에어컨','가전','Wall')
def _():
 box('ACBody',(.90,.23,.29),(0,0,.145),'Body',.04)
 box('Outlet',(.78,.025,.055),(0,-.117,.057),'Dark',.01)
 for z in (.042,.06,.078):box('Louver',(.76,.035,.009),(0,-.14,z),'White',.003,(-.2,0,0))
 box('Indicator',(.035,.003,.009),(.34,-.119,.17),'Accent',.002)
@model('StandingAC','스탠드 에어컨','가전')
def _():
 box('Tower',(.44,.32,1.80),(0,0,.90),'Body',.05)
 box('BlackPanel',(.31,.015,.65),(0,-.169,1.37),'Dark',.035)
 for z in (1.18,1.51):
  ring('VentRim',.115,.012,(0,-.184,z),'Metal',(pi/2,0,0),20)
  for dz in (-.06,0,.06):box('VentVane',(.17,.018,.012),(0,-.192,z+dz),'Body',.003)
 box('StatusDisplay',(.10,.008,.04),(0,-.17,.87),'Screen')

def television(w=1.10,h=.64,z=.43):
 box('TVBody',(w,.045,h),(0,0,z),'Body',.012)
 box('Screen',(w-.045,.006,h-.05),(0,-.026,z),'Screen',.004)
 box('LowerBezel',(w-.02,.009,.018),(0,-.03,z-h/2+.015),'Body',.003)
@model('TV','TV','가전','Surface','Display')
def _():
 television()
 for x in (-.37,.37):
  rod('Stand',(x,0,.15),(x-.07,-.13,.018),.012,'Dark')
  rod('Stand',(x,0,.15),(x+.07,.12,.018),.012,'Dark')
@model('MobileTV','이동형 TV','가전',function='Display')
def _():
 cyl('RollingBase',.25,.035,(0,0,.06),'Body',20)
 for x,y in ((-.16,-.12),(.16,-.12),(-.16,.12),(.16,.12)):cyl('Caster',.045,.035,(x,y,.045),'Rubber',10,(pi/2,0,0))
 cyl('Stand',.025,.95,(0,.08,.55),'Metal')
 television(.66,.40,1.22)
 rod('Mount',(0,.08,.99),(0,.03,1.22),.035,'Body')
@model('DesktopSet','데스크톱 컴퓨터 세트','가전','Surface','Display',note='Monitor, tower, keyboard and mouse are separately named meshes')
def _():
 television(.60,.36,.37)
 box('MonitorFoot',(.26,.18,.025),(0,0,.0125),'Body');box('MonitorStem',(.045,.035,.20),(0,.03,.12),'Body')
 box('Tower',(.20,.40,.44),(.43,.03,.22),'Body',.016)
 box('TowerFront',(.17,.015,.39),(.43,-.18,.23),'Dark')
 for z in (.15,.32):ring('FanAccent',.055,.005,(.43,-.19,z),'Accent',(pi/2,0,0),16)
 cyl('Power',.012,.005,(.43,-.19,.42),'Metal',10,(pi/2,0,0))
 box('Keyboard',(.44,.15,.025),(0,-.20,.0125),'Dark')
 for row in range(4):
  for col in range(12):box('Key',(.027,.023,.007),(-.185+col*.034,-.245+row*.03,.029),'White',.002)
 box('Mouse',(.062,.10,.035),(.28,-.20,.0175),'Body',.014)
@model('Laptop','노트북','가전','Surface','Display')
def _():
 box('LaptopBase',(.34,.24,.018),(0,0,.009),'Body',.008)
 box('KeyboardWell',(.30,.095,.003),(0,.02,.02),'Dark',.003)
 for row in range(3):
  for col in range(10):box('Key',(.022,.018,.003),(-.126+col*.028,-.012+row*.028,.023),'White',.001)
 box('Trackpad',(.10,.05,.002),(0,-.075,.02),'Accent',.003)
 box('DisplayLid',(.34,.015,.22),(0,.13,.125),'Body',.006,(-.13,0,0))
 box('Screen',(.31,.003,.185),(0,.118,.125),'Screen',.003,(-.13,0,0))

@model('Toilet','변기','욕실',function='Seating, WaterSource')
def _():
 box('Pedestal',(.27,.43,.30),(0,-.02,.15),'White',.065)
 lathe('Bowl',[(.05,.21),(.16,.25),(.22,.36),(.22,.40),(.17,.41),(.15,.31),(.04,.28)],(0,-.10,0),'White',20,(.9,1.25))
 o=ring('Seat',.198,.022,(0,-.10,.414),'White',n=24);o.scale=(.90,1.23,1)
 box('Cistern',(.36,.17,.40),(0,.24,.59),'White',.045)
 box('TankLid',(.38,.19,.035),(0,.24,.80),'Body',.014)
 cyl('FlushButton',.025,.008,(.08,.24,.822),'Metal',12)
 box('SeatLid',(.34,.035,.40),(0,.14,.65),'Body',.04,(-.08,0,0))
@model('Washbasin','세면대','욕실',function='WaterSource')
def _():
 box('Pedestal',(.22,.26,.67),(0,.07,.335),'White',.045)
 basin('CeramicBasin',.56,.43,.14,.80,'White');faucet(y=.18,z=.80)
@model('BathroomMirror','화장실 거울','욕실','Wall')
def _():
 box('Frame',(.60,.035,.75),(0,0,.375),'Body',.035)
 box('Mirror',(.54,.006,.69),(0,-.022,.375),'Metal',.025)
 box('BottomShelf',(.62,.12,.025),(0,-.05,.02),'Body')
@model('WallShower','벽 부착 샤워기','욕실','Wall','WaterSource')
def _():
 rod('Rail',(0,0,.10),(0,0,1.15),.015)
 for z in (.15,1.10):rod('WallMount',(0,.06,z),(0,0,z),.022)
 box('Mixer',(.25,.065,.065),(0,-.015,.08),'Metal',.015)
 rod('ShowerNeck',(0,0,1.15),(0,-.18,1.15),.018)
 cyl('RainHead',.105,.025,(0,-.21,1.14),'Metal',20)
 cyl('SprayFace',.09,.004,(0,-.21,1.125),'Dark',20)
 for i in range(8):
  a=2*pi*i/8;cyl('Nozzle',.004,.004,(.06*cos(a),-.21+.06*sin(a),1.122),'White',6)
 rod('Handset',( .12,-.02,.53),(.12,-.08,.72),.02)
 # Polygon hose curve rather than a solid straight pole.
 pts=[(.12,-.02,.53),(.17,-.04,.34),(.15,-.06,.16),(.08,-.06,.03),(-.02,-.06,.02),(-.09,-.04,.08)]
 for a,b in zip(pts,pts[1:]):rod('Hose',a,b,.009)
@model('ShowerBooth','샤워부스','욕실')
def _():
 box('Tray',(.92,.92,.08),(0,0,.04),'White',.03)
 box('Drain',(.08,.08,.004),(.25,.25,.083),'Metal')
 for x in (-.44,.44):
  for y in (-.44,.44):box('CornerPost',(.025,.025,1.95),(x,y,1.055),'Metal',.003)
 for z in (.10,2.02):
  for x in (-.44,.44):box('SideRail',(.025,.90,.025),(x,0,z),'Metal',.003)
  for y in (-.44,.44):box('CrossRail',(.90,.025,.025),(0,y,z),'Metal',.003)
 for x in (-.44,.44):box('GlassSide',(.008,.86,1.90),(x,0,1.055),'Glass',0)
 box('GlassBack',(.86,.008,1.90),(0,.44,1.055),'Glass',0)
 for x in (-.22,.22):box('GlassDoor',(.425,.008,1.90),(x,-.44,1.055),'Glass',0)
 for x in (-.055,.055):handle('DoorHandle',x,-.475,1.02,.25,True)
@model('Bathtub','욕조','욕실',function='WaterSource')
def _():
 # Bowl authored around floor origin; a genuine interior cavity, not a capped box.
 basin('Tub',.76,1.65,.43,.56,'White')
 box('Plinth',(.53,1.35,.10),(0,0,.05),'Body',.03)
 faucet(x=0,y=.73,z=.56)
@model('TowelRail','수건걸이','욕실','Wall')
def _():
 rod('Rail',(-.28,-.07,.09),(.28,-.07,.09),.013)
 for x in (-.28,.28):
  rod('Mount',(x,0,.09),(x,-.07,.09),.018)
  box('WallPlate',(.055,.015,.09),(x,.007,.09),'Body')
@model('TrashBin','쓰레기통','생활',function='Storage')
def _():
 lathe('Bin',[(.0,0),(.14,0),(.16,.36),(.142,.36),(.122,.025),(0,.025)],role='Body',n=16)
 cyl('Lid',.165,.025,(0,0,.375),'Accent',16)
 box('Pedal',(.10,.08,.018),(0,-.16,.025),'Metal')

def geometry_hash():
 return hashlib.sha256(json.dumps([(o.name, [list(v.co) for v in o.data.vertices],[list(p.vertices) for p in o.data.polygons],list(o.location),list(o.rotation_euler)) for o in PARTS]).encode()).hexdigest()
def set_palette(name):
 for o in PARTS:
  roles=list(o.get('MaterialRoles',[o['Role']]))
  for slot,role in enumerate(roles):o.data.materials[slot]=MATS[name][role]
 root['Palette']=name
SIZE_TARGETS=json.loads((HERE.parent/'furniture_sizes_20261107.json').read_text(encoding='utf8'))['items']
def apply_size_target(identifier,lo,hi):
 # Same table as ../apply_sizes.py, so rebuilding a recipe keeps the game-scale size. Baked into the vertices.
 target=SIZE_TARGETS.get(identifier)
 if not target:return lo,hi
 f=[target[i]/(hi[i]-lo[i]) if target[i] and hi[i]-lo[i]>1e-5 else 1.0 for i in range(3)]
 if all(abs(x-1)<1e-4 for x in f):return lo,hi
 for o in PARTS:
  o.location=Vector((o.location.x*f[0],o.location.y*f[1],o.location.z*f[2]))
  rot=o.rotation_euler.to_matrix();inv=rot.inverted()
  for v in o.data.vertices:
   w=rot@v.co;v.co=inv@Vector((w.x*f[0],w.y*f[1],w.z*f[2]))
  o.data.update()
 bpy.context.view_layer.update()
 pts=[o.matrix_world@v.co for o in PARTS for v in o.data.vertices]
 return [min(p[i] for p in pts) for i in range(3)],[max(p[i] for p in pts) for i in range(3)]
def prepare(entry):
 # Bake stable normals/triangulation and UVs, keeping semantic object names.
 for o in PARTS:
  bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
  bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
  bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
  bpy.ops.mesh.normals_make_consistent(inside=False)
  if not o.data.uv_layers:bpy.ops.uv.smart_project(island_margin=.02)
  bpy.ops.object.mode_set(mode='OBJECT')
  mod=o.modifiers.new('ExportTriangles','TRIANGULATE');apply(o,mod)
 bpy.context.view_layer.update()
 pts=[o.matrix_world@v.co for o in PARTS for v in o.data.vertices]
 lo=[min(p[i] for p in pts) for i in range(3)];hi=[max(p[i] for p in pts) for i in range(3)]
 # Sink insertion plane is intentionally z=0 with the bowl below it.
 if entry['id']!='Sink':
  for o in PARTS:o.location.z-=lo[2]
  hi[2]-=lo[2];lo[2]=0
 bpy.context.view_layer.update()
 lo,hi=apply_size_target(entry['id'],lo,hi)
 row={k:v for k,v in entry.items() if k!='build'}
 row.update(dimensions=[hi[i]-lo[i] for i in range(3)],bounds_min=lo,bounds_max=hi,triangles=sum(len(o.data.polygons) for o in PARTS),meshes=len(PARTS),roles=sorted({role for o in PARTS for role in o.get('MaterialRoles',[o['Role']])}),geometry_sha256=geometry_hash(),palettes=list(PALETTES))
 assert row['triangles']<12000,(entry['id'],row['triangles'])
 return row
def export_asset(path,embedded=False):
 bpy.ops.export_scene.fbx(filepath=str(path.with_suffix('.fbx')),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',use_custom_props=True,add_leaf_bones=False,bake_anim=False,path_mode='COPY' if embedded else 'AUTO',embed_textures=embedded)
 bpy.ops.export_scene.gltf(filepath=str(path.with_suffix('.glb')),use_selection=True,export_format='GLB')

def export(entry,row):
 out=HERE/'Exports'/entry['id'];out.mkdir(exist_ok=True)
 for palette in (['Cream'] if '--model-only' in sys.argv else PALETTES):
  set_palette(palette);bpy.ops.object.select_all(action='DESELECT')
  for o in asset.objects:o.select_set(True)
  bpy.context.view_layer.objects.active=root
  path=out/(entry['id']+'_'+palette)
  export_asset(path)
  assert geometry_hash()==row['geometry_sha256']
 set_palette('Cream')

def presentation(entry,row):
 global GPU_READY
 studio=bpy.data.collections.new('Studio_NOT_EXPORTED');scene.collection.children.link(studio)
 def move(o):
  for c in list(o.users_collection):c.objects.unlink(o)
  studio.objects.link(o)
 bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,min(0,row['bounds_min'][2])-.008));floor=bpy.context.object;floor.name='StudioFloor';move(floor)
 m=bpy.data.materials.new('StudioNeutral');m.diffuse_color=(.65,.64,.61,1);m.use_nodes=True
 m.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.65,.64,.61,1);floor.data.materials.append(m)
 center=Vector([(a+b)/2 for a,b in zip(row['bounds_min'],row['bounds_max'])])
 for name,loc,power,size in [('Key',(-3,-4,6),900,4),('Fill',(4,-2,4),500,3),('Rim',(1,4,5),650,3)]:
  d=bpy.data.lights.new(name,'AREA');d.energy=power;d.size=size
  o=bpy.data.objects.new(name,d);studio.objects.link(o);o.location=loc;o.rotation_euler=(center-o.location).to_track_quat('-Z','Y').to_euler()
 world=bpy.data.worlds.new('World');world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.35,.35,.35,1);scene.world=world
 cam=bpy.data.objects.new('PreviewCamera',bpy.data.cameras.new('PreviewCamera'));studio.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO'
 cam.location=center+Vector((3,-4,2.8));cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler()
 # Fit the projected box, including asymmetric desk sets and corner sofas.
 inv=cam.rotation_euler.to_matrix().transposed()
 corners=[Vector((x,y,z)) for x in (row['bounds_min'][0],row['bounds_max'][0]) for y in (row['bounds_min'][1],row['bounds_max'][1]) for z in (row['bounds_min'][2],row['bounds_max'][2])]
 cp=[inv@(v-center) for v in corners]
 cam.data.ortho_scale=max(max(v.x for v in cp)-min(v.x for v in cp),max(v.y for v in cp)-min(v.y for v in cp))*1.23
 scene.render.engine='CYCLES';scene.cycles.samples=16;scene.cycles.use_denoising=True
 preferences=bpy.context.preferences.addons['cycles'].preferences
 preferences.compute_device_type='OPTIX'
 if not GPU_READY:
  preferences.get_devices();GPU_READY=True
 for device in preferences.devices:device.use=device.type=='OPTIX'
 scene.cycles.device='GPU'
 scene.render.use_persistent_data=True
 scene.render.threads_mode='FIXED';scene.render.threads=4
 scene.render.resolution_x=540;scene.render.resolution_y=540;scene.render.resolution_percentage=100;scene.view_settings.view_transform='AgX'
 scene.render.image_settings.file_format='PNG'
 folder=HERE/'QA'/entry['id'];folder.mkdir(exist_ok=True)
 if '--model-only' in sys.argv and '--skip-render' not in sys.argv:
  set_palette('Cream');scene.render.filepath=str(folder/'Cream.png');bpy.ops.render.render(write_still=True)
 elif '--skip-render' not in sys.argv:
  # Render all five material variants in one pass, with identical directional
  # lighting. Linked geometry is used only for this presentation strip.
  set_palette('Cream')
  extra=[];lights=[o for o in studio.objects if o.type=='LIGHT']
  for light in lights:light.hide_render=True
  sun_data=bpy.data.lights.new('StripKey','SUN');sun_data.energy=2.4;sun_data.angle=.12
  sun=bpy.data.objects.new('StripKey',sun_data);studio.objects.link(sun);sun.rotation_euler=(.45,-.55,-.35)
  original_location=cam.location.copy();old_scale=cam.data.ortho_scale
  right=cam.rotation_euler.to_matrix()@Vector((1,0,0))
  bpy.context.view_layer.update()
  for i,palette in enumerate(PALETTES):
   if not i:continue
   for source in PARTS:
    o=source.copy();o.data=source.data;o.parent=None;studio.objects.link(o)
    o.matrix_world=source.matrix_world.copy();o.location+=right*(old_scale*i)
    o.material_slots[0].link='OBJECT';o.material_slots[0].material=MATS[palette][source['Role']]
    extra.append(o)
  cam.location=original_location+right*(old_scale*2);cam.data.ortho_scale=old_scale*5
  scene.render.resolution_x=2700;scene.render.resolution_y=540
  scene.render.filepath=str(folder/'Palettes.png');bpy.ops.render.render(write_still=True)
  for o in extra:bpy.data.objects.remove(o,do_unlink=True)
  bpy.data.objects.remove(sun,do_unlink=True)
  for light in lights:light.hide_render=False
  cam.location=original_location;cam.data.ortho_scale=old_scale;scene.render.resolution_x=540
 set_palette('Cream')
 # Blender text block makes all palettes switchable without regenerating the asset.
 text=bpy.data.texts.new('Switch_Color.py')
 text.write("# Change PALETTE then Run Script in Blender Text Editor.\nimport bpy\nPALETTE = 'Sage' # Cream, Sage, Blue, Rose, Charcoal\nfor o in bpy.data.objects:\n    if o.type == 'MESH' and 'Role' in o:\n        o.data.materials[0] = bpy.data.materials[PALETTE + '_' + o['Role']]\n")
 for screen in bpy.data.screens:
  for area in screen.areas:
   if area.type=='VIEW_3D':
    area.spaces.active.region_3d.view_location=center;area.spaces.active.region_3d.view_distance=cam.data.ortho_scale*1.4;area.spaces.active.shading.color_type='MATERIAL'
 bpy.ops.object.select_all(action='DESELECT');root.select_set(True);bpy.context.view_layer.objects.active=root
 bpy.context.preferences.filepaths.save_version=0
 bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'Models'/(entry['id']+'.blend')))
 (folder/'Manifest.json').write_text(json.dumps(row,ensure_ascii=False,indent=2),encoding='utf8')

STYLE_NAMES={'WhiteWood':'화이트앤우드','Eclectic':'틸 · 코랄 · 머스터드 · 퍼플','MarbleGold':'대리석 + 금','ChicBlack':'시크 블랙'}

def material_geometry_signature(collection):
 # Excludes materials only; positions, topology, UVs, shading and transforms are protected.
 rows=[]
 for o in sorted(collection.objects,key=lambda x:x.name):
  row=[o.name,o.type,list(map(list,o.matrix_world))]
  if o.type=='MESH':
   row += [[list(v.co) for v in o.data.vertices],[(list(p.vertices),p.material_index,p.use_smooth) for p in o.data.polygons],
           [[list(d.uv) for d in layer.data] for layer in o.data.uv_layers]]
  rows.append(row)
 return hashlib.sha256(json.dumps(rows).encode()).hexdigest()

def make_style_textures(folder):
 # Tileable, deterministic surface colour maps. Standard PNGs also survive FBX/GLB export.
 import numpy as np,struct,zlib
 folder.mkdir(parents=True,exist_ok=True);n=512
 y,x=np.mgrid[0:n,0:n]/n
 rng=np.random.default_rng(7319);fy,fx=np.meshgrid(np.fft.fftfreq(n)*n,np.fft.fftfreq(n)*n,indexing='ij')
 def noise(sx,sy):
  data=np.fft.ifft2(np.fft.fft2(rng.normal(size=(n,n)))*np.exp(-.5*((fx/sx)**2+(fy/sy)**2))).real
  return data/(data.std()+1e-9)
 broad=noise(4,3);fibres=noise(100,2);fine=noise(180,5)
 grain=np.sin(2*pi*(32*x+.10*broad))
 wood=np.clip(.87+.040*fibres+.025*fine+.018*grain,.69,1.)
 field=noise(3,3)+.25*noise(8,8)+.065*noise(24,24)
 vein=np.exp(-(field/.055)**2);halo=np.exp(-(field/.23)**2)
 branch=np.exp(-((field+.22*noise(12,12))/.032)**2)*np.clip(.7+field,0,1)
 marble=np.clip(.985-.31*vein-.08*halo-.12*branch+.008*noise(50,50),0,1)
 def save(name,rgb):
  a=np.clip(rgb,0,255).astype('uint8');raw=b''.join(b'\0'+row.tobytes() for row in a)
  def chunk(kind,data):return struct.pack('>I',len(data))+kind+data+struct.pack('>I',zlib.crc32(kind+data)&0xffffffff)
  (folder/(name+'.png')).write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',n,n,8,2,0,0,0))+chunk(b'IDAT',zlib.compress(raw,9))+chunk(b'IEND',b''))
 save('NaturalOak',wood[:,:,None]*np.array([217,178,124])[None,None,:]+np.array([17,19,22]))
 save('BlackOak',wood[:,:,None]*np.array([33,37,40])[None,None,:]+np.array([10,11,13]))
 save('Calacatta',marble[:,:,None]*np.array([242,239,229])[None,None,:])

def style_surface(style,role,name,entry):
 n=name.lower();id=entry['id'];cat=entry['category']
 mirror=n in ('mirror','ovalmirrorglass') or n.startswith('mirror.')
 if mirror:return ('Mirror','DDE4E7',1.,.035,None)
 if role=='Glass':return ('Glass','D7E5E3',0,.07,None)
 if role=='Screen':return ('Screen','10191F',.12,.17,None)
 if role=='Rubber':return ('Rubber','181C20',0,.86,None)
 if role=='Water':return ('Water','B5DADE',0,.09,None)
 cloth=role=='Fabric' or any(w in n for w in ('pillow','duvet','mattress','cover','fold','padding','headrest','calfpad'))
 # Upholstered sofa bases and the gaming chair's two-material back stay upholstered.
 cloth=cloth or (id in ('Sofa2','Sofa3','CornerSofa','Armchair') and role=='Body') or (id=='GamingChair' and ('back' in n or 'armpad' in n))
 fixed_dark=any(w in n for w in ('drain','outlet','opening','filter','keyboardwell','hob')) and role=='Dark'
 if fixed_dark:return ('FunctionalDark','141A20',.15,.40,None)
 trim=role=='Metal' or (role=='Wood' and any(w in n for w in ('leg','rail','brace','stand','frame','socket','trestle','foot')))
 trim=trim or (id=='GamingChair' and any(w in n for w in ('starbase','reclinehinge','armmount')))
 trim=trim or (id in ('FullMirror','BathroomMirror') and n in ('mirrorframe','frame')) or (id=='PlasticChair' and n.startswith('leg'))
 trim=trim or (id=='LaundryBasket' and 'rim' in n) or (id=='RecyclingBins' and n.startswith('lid')) or (id=='Laptop' and n=='trackpad') or n=='lowerbezel'
 textiles={'WhiteWood':('EEE9DE','D9D2C2'),'Eclectic':('DC735D','146C70'),'MarbleGold':('E8DBC0','21453D'),'ChicBlack':('20252C','383D45')}
 if cloth:
  primary,secondary=textiles[style]
  alt=role in ('Accent','Dark') or ('back' in n and style=='Eclectic')
  if id=='Armchair' and 'shell' in n and style in ('Eclectic','MarbleGold'):alt=True
  if 'pillow' in n:alt=True
  return ('UpholsteryAccent' if alt else 'Upholstery',secondary if alt else primary,0,.89,None)
 countertop=(id in ('KitchenBase','SinkBase') and 'counter' in n) or (id.startswith('GamingDesk') and n.startswith('tabletop'))
 if style=='WhiteWood':
  if (id=='Toilet' and n.startswith('seatlid')) or (id=='BathroomMirror' and n in ('frame','bottomshelf')):return ('Oak','C8A274',0,.52,'NaturalOak')
  if countertop or (id=='KitchenUpper' and role=='Body' and n.startswith(('side','top','bottom'))) or (id=='PlasticChair' and n.startswith('leg')):return ('Oak','C8A274',0,.52,'NaturalOak')
  if role=='Wood' or (role=='Accent' and not cat=='가전') or (id=='GamingChair' and 'seatpan' in n):return ('Oak','C8A274',0,.52,'NaturalOak')
  if role=='Metal':return ('SatinSteel','B7BFC3',.88,.27,None)
  return ('WarmWhite' if role!='Dark' else 'WarmGrey','F3F0E8' if role!='Dark' else 'B8BBB8',0,.38,None)
 if style=='Eclectic':
  if role=='Metal':return ('MulberryMetal','683B60',.58,.30,None)
  colors={'Body':'197A7B','Wood':'D7A13E','Accent':'D96A52','White':'E9D7B0','Dark':'493950'}
  if 'top' in n and id in ('KitchenBase','SinkBase','IslandTable'):return ('TealTop','197A7B',0,.38,None)
  if any(w in n for w in ('drawer','door','lid')) and role=='Body':return ('CoralPanel','D96A52',0,.42,None)
  return (role,colors.get(role,'197A7B'),0,.44,None)
 if style=='MarbleGold':
  if countertop:return ('CalacattaMarble','F2EFE5',0,.23,'Calacatta')
  if trim:return ('BrushedGold','D5AA55',1.,.25,None)
  if role=='Accent':return ('EmeraldEnamel','183D33',.08,.25,None)
  if role=='Dark':return ('Obsidian','151C1A',.15,.25,None)
  # Marble applies to hard housings and furniture panels, never cloth, glass or controls.
  if role in ('Body','Wood','White') and not any(w in n for w in ('key','button','nozzle','louver','underlight')):return ('CalacattaMarble','F2EFE5',0,.23,'Calacatta')
  return ('IvoryEnamel','EFE6D2',.05,.25,None)
 if role=='Wood':return ('BlackOak','25292D',0,.53,'BlackOak')
 if role=='Metal':return ('Gunmetal','454A50',.92,.28,None)
 colors={'Body':'24292F','Accent':'454C55','White':'3C4147','Dark':'101419'}
 return (role,colors.get(role,'24292F'),.05,.36 if role!='Dark' else .28,None)

def style_material(style,spec,folder,cache):
 key=(style,spec)
 if key in cache:return cache[key]
 label,color,metal,rough,texture=spec
 m=bpy.data.materials.new(style+'_'+label);m.use_nodes=True;m.diffuse_color=rgba(color)
 bs=m.node_tree.nodes.get('Principled BSDF');bs.inputs['Base Color'].default_value=rgba(color);bs.inputs['Metallic'].default_value=metal;bs.inputs['Roughness'].default_value=rough
 if label in ('Glass','Water'):
  bs.inputs['Transmission Weight'].default_value=.88;bs.inputs['IOR'].default_value=1.45 if label=='Glass' else 1.333
 if label.startswith('Upholstery'):bs.inputs['Sheen Weight'].default_value=.18
 if texture:
  tex=m.node_tree.nodes.new('ShaderNodeTexImage');tex.image=bpy.data.images.load(str(folder/(texture+'.png')),check_existing=True)
  m.node_tree.links.new(tex.outputs['Color'],bs.inputs['Base Color'])
 m['SurfaceFinish']=label;m['Style']=style;cache[key]=m
 return m

def build_material_variants(only=None):
 global GPU_READY
 dest=HERE/'MaterialVariants';dest.mkdir(exist_ok=True);make_style_textures(dest/'Textures')
 baseline_path=dest/'SourceBaseline.json'
 if not baseline_path.exists():
  protected=list((HERE/'Models').glob('*.blend'))+list((HERE/'Exports').glob('*/*_Cream.fbx'))+list((HERE/'Exports').glob('*/*_Cream.glb'))+list((HERE/'QA').glob('*/Cream.png'))
  baseline_path.write_text(json.dumps({str(p.relative_to(HERE)):hashlib.sha256(p.read_bytes()).hexdigest() for p in protected},indent=2),encoding='utf8')
 entries=json.loads((HERE/'RequestedCatalog.json').read_text(encoding='utf8'))
 for entry in entries:
  id=entry['id']
  if only and id not in only:continue
  bpy.ops.wm.open_mainfile(filepath=str(HERE/'Models'/(id+'.blend')),load_ui=False,use_scripts=False)
  col=bpy.data.collections[id+'_ASSET'];scene=bpy.context.scene;root=bpy.data.objects[id]
  meshes=[o for o in col.objects if o.type=='MESH'];signature=material_geometry_signature(col)
  specs={o.name:list(o.get('MaterialRoles',[o['Role']])) for o in meshes};cache={}
  original=json.loads((HERE/'QA'/id/'Manifest.json').read_text(encoding='utf8'))
  selected_styles=sys.argv[sys.argv.index('--styles')+1].split(',') if '--styles' in sys.argv else list(STYLE_NAMES)
  for style in selected_styles:
   assert style in STYLE_NAMES,style
   out=dest/style/id;out.mkdir(parents=True,exist_ok=True)
   for o in meshes:
    for slot,role in enumerate(specs[o.name]):o.data.materials[slot]=style_material(style,style_surface(style,role,o.name,entry),dest/'Textures',cache)
   root['Palette']=style;root['MaterialStyle']=STYLE_NAMES[style]
   assert material_geometry_signature(col)==signature,(id,style,'Geometry changed')
   bpy.ops.object.select_all(action='DESELECT')
   for o in col.objects:o.select_set(True)
   bpy.context.view_layer.objects.active=root
   export_asset(out/(id+'_'+style),embedded=True)
   scene.render.engine='CYCLES';scene.cycles.samples=16;scene.cycles.use_denoising=True
   prefs=bpy.context.preferences.addons['cycles'].preferences;prefs.compute_device_type='OPTIX'
   if not GPU_READY:prefs.get_devices();GPU_READY=True
   for d in prefs.devices:d.use=d.type=='OPTIX'
   scene.cycles.device='GPU';scene.render.use_persistent_data=True
   scene.render.resolution_x=540;scene.render.resolution_y=540;scene.render.resolution_percentage=100
   scene.render.filepath=str(out/'Preview.png');bpy.ops.render.render(write_still=True)
   bpy.ops.file.pack_all();bpy.context.preferences.filepaths.save_version=0
   bpy.ops.wm.save_as_mainfile(filepath=str(out/(id+'_'+style+'.blend')))
   row={**original,'style':style,'style_label':STYLE_NAMES[style],'palettes':[style],'geometry_preserved':True,'source_geometry_signature':signature,'material_count':len({m for o in meshes for m in o.data.materials}),'surfaces':sorted({m['SurfaceFinish'] for o in meshes for m in o.data.materials})}
   (out/'Manifest.json').write_text(json.dumps(row,ensure_ascii=False,indent=2),encoding='utf8')
   print('MATERIAL_VARIANT_DONE',id,style,flush=True)
 baseline=json.loads(baseline_path.read_text(encoding='utf8'))
 changed=[p for p,h in baseline.items() if hashlib.sha256((HERE/p).read_bytes()).hexdigest()!=h]
 assert not changed,changed
 (dest/'SourcePreservation.json').write_text(json.dumps({'protected_files':len(baseline),'changed_files':changed,'passed':True},indent=2),encoding='utf8')
 print('MATERIAL_VARIANTS_COMPLETE',flush=True)

if __name__=='__main__':
 args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
 only=args[args.index('--only')+1].split(',') if '--only' in args else None
 if '--material-variants' in args:
  build_material_variants(only);sys.exit(0)
 if '--finalize-models' in args:
  for path in sorted((HERE/'Models').glob('*.blend')):
   if only and path.stem not in only:continue
   bpy.ops.wm.open_mainfile(filepath=str(path),load_ui=False,use_scripts=False)
   used={m for o in bpy.context.scene.objects if o.type=='MESH' for m in o.data.materials if m}
   for m in list(bpy.data.materials):
    if m not in used:bpy.data.materials.remove(m)
   if 'Switch_Color.py' in bpy.data.texts:bpy.data.texts.remove(bpy.data.texts['Switch_Color.py'])
   bpy.context.preferences.filepaths.save_version=0
   bpy.ops.wm.save_as_mainfile(filepath=str(path))
  print('MODEL_ONLY_SOURCES_FINALIZED',flush=True)
  sys.exit(0)
 (HERE/'RequestedCatalog.json').write_text(json.dumps([{k:v for k,v in e.items() if k!='build'} for e in CATALOG],ensure_ascii=False,indent=2),encoding='utf8')
 for i,entry in enumerate(CATALOG):
  if only and entry['id'] not in only:continue
  print('BUILD_START',i+1,len(CATALOG),entry['id'],flush=True)
  init(entry);entry['build']();row=prepare(entry)
  if '--model-only' in sys.argv:row['palettes']=['Cream']
  export(entry,row);presentation(entry,row)
  print('BUILD_DONE',entry['id'],row['triangles'],flush=True)
 rows=[json.loads(p.read_text(encoding='utf8')) for p in (HERE/'QA').glob('*/Manifest.json')]
 (HERE/'Catalog.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2),encoding='utf8')
 print('HOUSEHOLD_COMPLETE',len(rows),flush=True)
