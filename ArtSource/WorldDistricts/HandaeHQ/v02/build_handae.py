"""Handae headquarters: staged, reference-led architectural asset for Blender/Unity.
Blender --background --factory-startup --python build_handae.py -- --stage blockout|final
Coordinates are metres, front -Y; all model object transforms are identity.
"""
import argparse
import bpy
import bmesh
import json
import math
from mathutils import Vector
from pathlib import Path
import sys

parser = argparse.ArgumentParser()
parser.add_argument('--stage', choices=['blockout', 'final'], default='blockout')
parser.add_argument('--views', default='front,left,right,top')
args = parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
FINAL = args.stage == 'final'
OUT = Path(__file__).resolve().parent
REVIEW = OUT / ('FinalRenders' if FINAL else 'BlockoutRenders')
REVIEW.mkdir(parents=True, exist_ok=True)
EXPORT = OUT / 'UnityExport'
EXPORT.mkdir(exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1.0
scene.unit_settings.length_unit = 'METERS'
scene.render.film_transparent = False
bpy.context.preferences.filepaths.save_version = 0

COLS = {}
for name in ['Tower_A','Tower_B','Podium','Glass','Frames','LED_Strips','Entrances','Roof','Signage','Ground_Details','_Presentation']:
    c = bpy.data.collections.new(name)
    scene.collection.children.link(c)
    COLS[name] = c

def empty(name, collection, parent=None):
    obj = bpy.data.objects.new(name, None)
    COLS[collection].objects.link(obj)
    obj.parent = parent
    return obj

ROOT = empty('HandaeHQ_v02', 'Podium')
ROOT['units'] = 'metres / 1 Blender unit = 1 m'
ROOT['front_axis'] = '-Y in Blender; FBX -Z forward, Y up'
ROOT['stage'] = args.stage
ROOT['reference_views'] = 'User supplied front / left / right / top, 2026-10-04'
PARTS = {name: empty(name+'_Root', name, ROOT) for name in ['Tower_A','Tower_B','Podium']}

def material(name, rgb, rough=.4, metal=0, emission=None, strength=0):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*rgb,1)
    m.use_nodes = True
    p = m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value = (*rgb,1)
    p.inputs['Roughness'].default_value = rough
    p.inputs['Metallic'].default_value = metal
    if emission:
        p.inputs['Emission Color'].default_value = (*emission,1)
        p.inputs['Emission Strength'].default_value = strength
    return m

MATS = {
    'DarkGlass': material('DarkGlass', (.018,.043,.066), .235,.56),
    'BlackMetal': material('BlackMetal', (.024,.030,.035), .34,.65),
    'LED_White': material('LED_White', (.8,.83,.78), .3,.1,(1,.88,.68),3.5),
    'Concrete_Dark': material('Concrete_Dark', (.042,.051,.059), .7,.06),
    'InteriorGlow': material('InteriorGlow', (.14,.11,.065), .34,.25,(.82,.51,.23),.85),
    'SignMaterial': material('SignMaterial', (.78,.80,.78), .38,.25,(.8,.9,1),.65),
    'TerraceGreen': material('TerraceGreen', (.04,.095,.055), .93),
    'Paving': material('Paving', (.26,.29,.30), .9),
}
MATS['DarkGlass'].node_tree.nodes.get('Principled BSDF').inputs['Coat Weight'].default_value = .32
MATS['DarkGlass'].node_tree.nodes.get('Principled BSDF').inputs['Coat Roughness'].default_value = .18
if FINAL:
    # A small reusable mock-office image on real glazing modules. No room geometry.
    # It avoids flat white luminous squares and exports with both FBX and GLB.
    texture_dir=EXPORT/'Textures';texture_dir.mkdir(exist_ok=True)
    interior=bpy.data.images.new('Handae_OfficeGlow',width=64,height=128,alpha=True)
    pixels=[]
    for iy in range(128):
        v=iy/127
        for ix in range(64):
            u=ix/63
            light=math.exp(-((v-.80)/.045)**2)*(.68 if .10<u<.90 else .13)
            ambient=.05+.035*math.sin(math.pi*v)
            if v<.19:ambient=.095*(v/.19)
            if .28<v<.50 and (.06<u<.29 or .61<u<.88):ambient*=.23
            edge=min(1,min(u,1-u)*18,min(v,1-v)*18)
            pixels.extend(((ambient+light)*edge,(ambient*.73+light*.77)*edge,(ambient*.43+light*.42)*edge,1))
    interior.pixels=pixels;interior.filepath_raw=str(texture_dir/'Handae_OfficeGlow.png');interior.file_format='PNG';interior.save();interior.pack()
    nodes=MATS['InteriorGlow'].node_tree.nodes;links=MATS['InteriorGlow'].node_tree.links
    tex=nodes.new('ShaderNodeTexImage');tex.image=interior;tex.interpolation='Linear'
    p=nodes.get('Principled BSDF');p.inputs['Metallic'].default_value=.12;p.inputs['Roughness'].default_value=.32
    p.inputs['Emission Strength'].default_value=.65
    links.new(tex.outputs['Color'],p.inputs['Base Color']);links.new(tex.outputs['Color'],p.inputs['Emission Color'])
CLAY = material('_Clay', (.30,.34,.36), .67)
CLAY_GLASS = material('_ClayGlass', (.21,.26,.29), .5)

class MeshBatch:
    def __init__(self):
        self.v=[]; self.f=[]; self.mi=[]; self.uv=[]
    def face(self, points, mat=0):
        start=len(self.v)
        self.v.extend(points)
        self.f.append(tuple(range(start,start+len(points))))
        self.mi.append(mat)
    def tube(self, points, width, depth=None):
        # Rectangular cross section with shared vertices, low cost, no subdivision.
        if len(points)<2: return
        depth = width if depth is None else depth
        pts=[Vector(p) for p in points]
        start=len(self.v)
        for i,p in enumerate(pts):
            tangent=(pts[min(i+1,len(pts)-1)]-pts[max(0,i-1)]).normalized()
            ref=Vector((0,0,1)) if abs(tangent.z)<.9 else Vector((0,1,0))
            across=tangent.cross(ref).normalized()*width/2
            out=tangent.cross(across).normalized()*depth/2
            self.v.extend([tuple(p-across-out),tuple(p+across-out),tuple(p+across+out),tuple(p-across+out)])
        self.f.append(tuple(start+j for j in (3,2,1,0))); self.mi.append(0)
        for i in range(len(pts)-1):
            for j in range(4):
                self.f.append((start+4*i+j,start+4*i+(j+1)%4,start+4*(i+1)+(j+1)%4,start+4*(i+1)+j)); self.mi.append(0)
        self.f.append(tuple(start+4*(len(pts)-1)+j for j in range(4))); self.mi.append(0)
    def object(self,name,collections,mats,parent=ROOT,smooth=False):
        if not self.f: return None
        mesh=bpy.data.meshes.new(name+'_Mesh')
        mesh.from_pydata(self.v,[],self.f); mesh.update()
        for m in mats: mesh.materials.append(MATS[m] if isinstance(m,str) else m)
        for i,p in enumerate(mesh.polygons):
            p.material_index=min(self.mi[i],len(mats)-1)
            p.use_smooth=smooth and len(p.vertices)==4
        bm=bmesh.new(); bm.from_mesh(mesh)
        bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00001)
        # Concave roof/slab caps contain straight runs; remove zero-area export triangles.
        bmesh.ops.triangulate(bm,faces=[f for f in bm.faces if len(f.verts)>4],ngon_method='EAR_CLIP')
        bmesh.ops.dissolve_degenerate(bm,dist=.000001,edges=list(bm.edges))
        zero=[f for f in bm.faces if f.calc_area()<1e-9]
        if zero:bmesh.ops.delete(bm,geom=zero,context='FACES')
        # Dissolve can re-form a concave n-gon. Lock the final export triangulation
        # and discard only mathematically zero-area faces after this last operation.
        bmesh.ops.triangulate(bm,faces=list(bm.faces),quad_method='FIXED',ngon_method='EAR_CLIP')
        zero=[f for f in bm.faces if f.calc_area()<1e-9 or (len(f.verts)==3 and
            (f.verts[1].co-f.verts[0].co).cross(f.verts[2].co-f.verts[0].co).length_squared<1e-14)]
        if zero:bmesh.ops.delete(bm,geom=zero,context='FACES')
        bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
        if name.endswith('_CurtainWall_Panels'):
            # Disconnected panes need an explicit exterior direction: normal recalculation
            # can otherwise orient separate islands toward the tower interior.
            cfg=next(c for c in TOWERS if name.startswith(c['name']))
            bm.normal_update()
            for face in bm.faces:
                c=face.calc_center_median();t=c.z/cfg['height']
                center=Vector((cfg['sign']*(27-6.5*ease(t/.52)+4.5*ease((t-.52)/.48)),
                    cfg['base_y']+cfg['sweep_y']*ease(t/.56),c.z))
                if face.normal.dot(c-center)<0:face.normal_flip()
            bm.normal_update()
        bm.to_mesh(mesh); bm.free()
        ob=bpy.data.objects.new(name,mesh)
        for col in collections: COLS[col].objects.link(ob)
        ob.parent=parent
        # Metric planar UV per face; intentionally tiled architecture UV0.
        # Unity importer generates separate packed lightmap UV1.
        uv=mesh.uv_layers.new(name='UV0_MetreTile')
        for p in mesh.polygons:
            normal=p.normal
            axis=max(range(3),key=lambda k:abs(normal[k]))
            axes=[k for k in range(3) if k!=axis]
            window_uv=('CurtainWall_Panels' in name or 'GlassStorefronts' in name)
            if window_uv:
                tangent=normal.cross(Vector((0,0,1))).normalized()
                coords=[mesh.vertices[mesh.loops[li].vertex_index].co for li in p.loop_indices]
                projections=[co.dot(tangent) for co in coords]
                umin=min(projections);umax=max(projections);zmin=min(co.z for co in coords);zmax=max(co.z for co in coords)
            for li in p.loop_indices:
                co=mesh.vertices[mesh.loops[li].vertex_index].co
                uv.data[li].uv=((co.dot(tangent)-umin)/max(.001,umax-umin),(co.z-zmin)/max(.001,zmax-zmin)) if window_uv else (co[axes[0]]/4,co[axes[1]]/4)
        ob['export_asset']=True
        return ob

def box(batch,center,size,mat=0):
    p=Vector(center); s=Vector(size)/2
    v=[tuple(p+Vector((a*s.x,b*s.y,c*s.z))) for c in (-1,1) for b in (-1,1) for a in (-1,1)]
    for face in [(0,2,3,1),(4,5,7,6),(0,1,5,4),(2,6,7,3),(0,4,6,2),(1,3,7,5)]:
        batch.face([v[i] for i in face],mat)

def loft(batch,rings,cap=True):
    n=len(rings[0]); base=len(batch.v)
    batch.v.extend(p for ring in rings for p in ring)
    for j in range(len(rings)-1):
        for i in range(n):
            batch.f.append((base+j*n+i,base+j*n+(i+1)%n,base+(j+1)*n+(i+1)%n,base+(j+1)*n+i));batch.mi.append(0)
    if cap:
        batch.f.extend([tuple(base+i for i in reversed(range(n))),tuple(base+(len(rings)-1)*n+i for i in range(n))]);batch.mi.extend([0,0])

def profile(u,w,d,r):
    # Arc-length parameterization keeps facade module spacing regular around corners.
    lengths=[w-2*r,math.pi*r/2,d-2*r,math.pi*r/2,w-2*r,math.pi*r/2,d-2*r,math.pi*r/2]
    s=(u%1)*sum(lengths)
    idx=0
    while idx<7 and s>lengths[idx]: s-=lengths[idx];idx+=1
    t=s/lengths[idx]
    if idx==0: return (-w/2+r+(w-2*r)*t,-d/2)
    if idx==2: return (w/2,-d/2+r+(d-2*r)*t)
    if idx==4: return (w/2-r-(w-2*r)*t,d/2)
    if idx==6: return (-w/2,d/2-r-(d-2*r)*t)
    centers={1:(w/2-r,-d/2+r,-90),3:(w/2-r,d/2-r,0),5:(-w/2+r,d/2-r,90),7:(-w/2+r,-d/2+r,180)}
    x,y,a=centers[idx];a=math.radians(a+t*90)
    return(x+r*math.cos(a),y+r*math.sin(a))

def ease(t):
    t=max(0,min(1,t));return t*t*(3-2*t)

def bezier(a,b,c,d,count=12):
    return [tuple((1-t)**3*a[k]+3*(1-t)**2*t*b[k]+3*(1-t)*t*t*c[k]+t**3*d[k] for k in range(2)) for t in [i/count for i in range(count)]]

OUTLINE=[]
for controls in [
    ((-44,-8),(-44,-30),(-33,-36),(-21,-29)),
    ((-21,-29),(-9,-23),(-7,-11),(5,-13)),
    ((5,-13),(17,-14),(24,-34),(35,-29)),
    ((35,-29),(43,-26),(44,-20),(44,-10)),
    ((44,-10),(44,4),(44,21),(41,26)),
    ((41,26),(37,32),(24,31),(14,31)),
    ((14,31),(3,31),(-14,31),(-29,31)),
    ((-29,31),(-42,31),(-44,24),(-44,14)),
    ((-44,14),(-44,8),(-44,0),(-44,-8)),
]: OUTLINE.extend(bezier(*controls))

def contour(level,inset=0):
    # Upper floors gently step back; the central recess remains a usable void.
    shrink=[0,.35,1.1,1.9,2.75,3.4][min(5,level)]+inset
    return [(x*(1-shrink/44),y*(1-shrink/36)) for x,y in OUTLINE]

def inside(p,poly):
    x,y=p; hit=False
    for i,a in enumerate(poly):
        b=poly[(i+1)%len(poly)]
        if (a[1]>y)!=(b[1]>y) and x<(b[0]-a[0])*(y-a[1])/(b[1]-a[1])+a[0]: hit=not hit
    return hit

FLOOR=5.0
PODIUM_TOP=26.1

def wave(x,y,level):
    # The facade ribbons rise into the centre of the forecourt, as in the front reference.
    # Upper ribbons rise more than lower ones, rather than five parallel floor stripes.
    strength=[.12,.38,.67,.9,1,1][max(0,min(5,level))]
    front=1-ease((y+6)/13)
    return 3.35*math.exp(-((x+3)/17.5)**2)*front*strength

def tower_point(cfg,u,z,offset=0):
    h=cfg['height'];t=z/h
    x,y=profile(u,cfg['width'],cfg['depth'],6.5)
    a=profile(u+.0001,cfg['width'],cfg['depth'],6.5)
    b=profile(u-.0001,cfg['width'],cfg['depth'],6.5)
    dx,dy=a[0]-b[0],a[1]-b[1];length=math.hypot(dx,dy)
    x+=dy/length*offset;y-=dx/length*offset
    waist=1-.035*math.sin(math.pi*t)**2
    x*=waist;y*=waist
    cx=cfg['sign']*(27-6.5*ease(t/.52)+4.5*ease((t-.52)/.48))
    cy=cfg['base_y']+cfg['sweep_y']*ease(t/.56)
    angle=math.radians(-cfg['sign']*(22+2.2*math.sin(math.pi*t)))
    xx=x*math.cos(angle)-y*math.sin(angle)
    yy=x*math.sin(angle)+y*math.cos(angle)
    crown_shift=(-1+y/(cfg['depth']/2))*ease((t-.96)/.04)
    return (cx+xx,cy+yy,z+crown_shift)

TOWERS=[dict(name='Tower_A',sign=1,height=159.2,width=22,depth=34,base_y=-9,sweep_y=12),
        dict(name='Tower_B',sign=-1,height=149.2,width=21.5,depth=33,base_y=-1,sweep_y=7)]

def build_tower(cfg):
    name=cfg['name'];parent=PARTS[name];h=cfg['height']
    n=64;rows=round(h/4);levels=[h*i/rows for i in range(rows+1)]
    if not FINAL:
        mass=MeshBatch();loft(mass,[[tower_point(cfg,i/n,z) for i in range(n)] for z in levels])
        mass.object(name+'_Massing',[name,'Glass'],[CLAY_GLASS],parent,True)
    else:
        panes=MeshBatch();mullions=MeshBatch();transoms=MeshBatch();backing=MeshBatch()
        for j,(low,high) in enumerate(zip(levels[:-1],levels[1:])):
            for i in range(n):
                mid=tower_point(cfg,(i+.5)/n,(low+high)/2)
                if high < PODIUM_TOP-.1 and inside(mid[:2],contour(min(4,int(low/FLOOR)))): continue
                # Identical modules, fixed joint width; intentional office lighting bands.
                lit=j in (3,10,17,24,31,37) and ((i+3*j)%23)<5
                u0=(i+.035)/n;u1=(i+.965)/n
                panes.face([tower_point(cfg,u0,low+.09),tower_point(cfg,u1,low+.09),tower_point(cfg,u1,high-.09),tower_point(cfg,u0,high-.09)],int(lit))
                if lit:
                    backing.face([tower_point(cfg,u0,low+.1,-.4),tower_point(cfg,u1,low+.1,-.4),tower_point(cfg,u1,high-.1,-.4),tower_point(cfg,u0,high-.1,-.4)])
        for i in range(n):
            pts=[]
            for z in levels:
                p=tower_point(cfg,i/n,z,.055)
                if z<PODIUM_TOP-.1 and inside(p[:2],contour(min(4,int(z/FLOOR)))):
                    if len(pts)>1: mullions.tube(pts,.11,.16)
                    pts=[]
                else: pts.append(p)
            if len(pts)>1: mullions.tube(pts,.11,.16)
        for z in levels:
            if z<PODIUM_TOP-.1: continue
            # Thin metal face bands need no hidden back/side faces at distant facade scale.
            for i in range(n):
                transoms.face([tower_point(cfg,i/n,z-.06,.13),tower_point(cfg,(i+1)/n,z-.06,.13),
                    tower_point(cfg,(i+1)/n,z+.06,.13),tower_point(cfg,i/n,z+.06,.13)])
        panes.object(name+'_CurtainWall_Panels',[name,'Glass'],['DarkGlass','InteriorGlow'],parent)
        mullions.object(name+'_VerticalMullions',[name,'Frames'],['BlackMetal'],parent)
        transoms.object(name+'_HorizontalTransoms',[name,'Frames'],['BlackMetal'],parent)
        backing.object(name+'_MockInterior_ShadowPlanes',[name],['BlackMetal'],parent)

    # Two accent lines per tower, deliberately separate and 0.22 m proud of glazing.
    # They follow the long curved front corners and return along the crown's front edge.
    frame=MeshBatch();led=MeshBatch()
    for u in (.03,.24):
        samples=[h*i/44 for i in range(45)]
        pts=[tower_point(cfg,u,z,.23) for z in samples if z>=PODIUM_TOP-.1 or not inside(tower_point(cfg,u,z)[:2],contour(min(4,int(z/FLOOR))))]
        frame.tube(pts,.38,.20)
        led.tube([tower_point(cfg,u,z,.36) for z in samples if z>=PODIUM_TOP-.1 or not inside(tower_point(cfg,u,z)[:2],contour(min(4,int(z/FLOOR))))],.15,.09)
    # Modest luminous crown segment, not a fully illuminated perimeter.
    led.tube([tower_point(cfg,.03+.21*i/28,h+.35,.29) for i in range(29)],.17,.12)
    frame.object(name+'_SilhouetteMetal',[name,'Frames'],['BlackMetal'] if FINAL else [CLAY],parent)
    ledob=led.object(name+'_LED_Outline',[name,'LED_Strips'],['LED_White'] if FINAL else [CLAY],parent)
    ledob['surface_offset_m']=.36
    roof=MeshBatch()
    lower=[tower_point(cfg,i/n,h-.1,.10) for i in range(n)]
    upper=[tower_point(cfg,i/n,h+.8,.10) for i in range(n)]
    inner=[tower_point(cfg,i/n,h+.8,-.45) for i in range(n)]
    innerlow=[tower_point(cfg,i/n,h-.1,-.45) for i in range(n)]
    # Four-sided parapet section, fully closed, with open roof service court.
    for i in range(n):
        k=(i+1)%n
        for ringA,ringB in [(lower,upper),(upper,inner),(inner,innerlow),(innerlow,lower)]:
            roof.face([ringA[i],ringA[k],ringB[k],ringB[i]])
    roof.object(name+'_Roof_Parapet',[name,'Roof'],['BlackMetal'] if FINAL else [CLAY],parent)
    deck=MeshBatch();deck.face([(x,y,h-2.15) for x,y,z in innerlow])
    deck.object(name+'_RoofDeck',[name,'Roof'],['Concrete_Dark'] if FINAL else [CLAY],parent)
    if FINAL:
        equip=MeshBatch()
        center=tower_point(cfg,.5,h-.2)
        # Place a low screened service box well inside the parapet, no helipad ornament.
        cx=cfg['sign']*25;cy=cfg['base_y']+cfg['sweep_y']
        box(equip,(cx,cy,h-.9),(7,9,1.4))
        equip.object(name+'_Roof_Service',[name,'Roof'],['Concrete_Dark'],parent)

for cfg in TOWERS: build_tower(cfg)

def build_podium():
    parent=PARTS['Podium'];glazing=MeshBatch();slabs=MeshBatch();frames=MeshBatch();backs=MeshBatch()
    for level in range(5):
        low=.45+level*FLOOR;high=low+FLOOR
        poly=contour(level,.22);edge=contour(level)
        loft(slabs,[[(x,y,high-.52+wave(x,y,level)) for x,y in edge],[(x,y,high+wave(x,y,level)) for x,y in edge]])
        if FINAL:
            for i,a in enumerate(poly):
                b=poly[(i+1)%len(poly)]
                length=math.dist(a,b)
                if length<.05:continue
                steps=max(1,math.ceil(length/2.2))
                for k in range(steps):
                    f0=(k+.018)/steps;f1=(k+.982)/steps
                    p=(a[0]+(b[0]-a[0])*f0,a[1]+(b[1]-a[1])*f0)
                    q=(a[0]+(b[0]-a[0])*f1,a[1]+(b[1]-a[1])*f1)
                    # Warm storefront and broad central glazing; rear is quiet dark glass.
                    lit=(i<48 and (level==0 or (i//5+level)%3!=0))
                    lp=wave(*p,level-1) if level else 0; lq=wave(*q,level-1) if level else 0
                    hp=wave(*p,level);hq=wave(*q,level)
                    glazing.face([(p[0],p[1],low+.08+lp),(q[0],q[1],low+.08+lq),(q[0],q[1],high-.6+hq),(p[0],p[1],high-.6+hp)],int(lit))
                    frames.tube([(p[0],p[1],low+lp),(p[0],p[1],high-.48+hp)],.12,.18)
                    if level==0:
                        frames.tube([(p[0],p[1],low+3.45),(q[0],q[1],low+3.45)],.12,.18)
        else:
            loft(glazing,[[(x,y,low+(wave(x,y,level-1) if level else 0)) for x,y in poly],[(x,y,high-.5+wave(x,y,level)) for x,y in poly]])
    # Roof terrace parapet; a recess remains open in the front center.
    edge=contour(5);inner=contour(5,.5)
    for i in range(len(edge)):
        k=(i+1)%len(edge)
        for a,b in [([(x,y,25.45+wave(x,y,5)) for x,y in edge],[(x,y,26.1+wave(x,y,5)) for x,y in edge]),
                    ([(x,y,26.1+wave(x,y,5)) for x,y in edge],[(x,y,26.1+wave(x,y,5)) for x,y in inner]),
                    ([(x,y,26.1+wave(x,y,5)) for x,y in inner],[(x,y,25.45+wave(x,y,5)) for x,y in inner])]:
            slabs.face([a[i],a[k],b[k],b[i]])
    # Broad facade ribbon on the left, rising toward the central entrance.
    fascia=MeshBatch()
    for i in range(1,26):
        a=contour(2,-.18)[i];b=contour(2,-.18)[i+1]
        za=12+6*ease((i-13)/13);zb=12+6*ease((i-12)/13)
        fascia.face([(a[0],a[1]-.055,za),(b[0],b[1]-.055,zb),(b[0],b[1]-.055,25.40+wave(*b,4)),(a[0],a[1]-.055,25.40+wave(*a,4))])
    slabs.object('Podium_CurvedFloorRibbons',['Podium'],['Concrete_Dark'] if FINAL else [CLAY],parent,True)
    glazing.object('Podium_GlassStorefronts',['Podium','Glass'],['DarkGlass','InteriorGlow'] if FINAL else [CLAY_GLASS],parent)
    frames.object('Podium_StorefrontFrames',['Podium','Frames'],['BlackMetal'],parent)
    fascia.object('Podium_SculptedSignFascia',['Podium'],['Concrete_Dark'] if FINAL else [CLAY],parent,True)

    ground=MeshBatch()
    loft(ground,[[(x*1.018,y*1.018,.02) for x,y in contour(0)],[(x*1.018,y*1.018,.45) for x,y in contour(0)]])
    ground.object('Entrance_ContinuousPlinth',['Entrances','Ground_Details'],['Paving'] if FINAL else [CLAY],ROOT)
    # Two broad shallow steps and a centre ramp into the recessed main entrance.
    entries=MeshBatch()
    box(entries,(2,-16.0,.15),(14,6,.30))
    box(entries,(2,-14.9,.33),(14,3.8,.24))
    for points in [[(-2,-22,.02),(6,-22,.02),(6,-17,.45),(-2,-17,.45)],
                   [(-2,-22,0),(6,-22,0),(6,-17,0),(-2,-17,0)],
                   [(-2,-22,0),(-2,-22,.02),(-2,-17,.45),(-2,-17,0)],
                   [(6,-22,0),(6,-17,0),(6,-17,.45),(6,-22,.02)]]: entries.face(points)
    entries.object('Entrance_StepsAndRamp',['Entrances'],['Paving'] if FINAL else [CLAY],ROOT)
    if FINAL:
        # Restrained planted roof ribbons, sized to the exposed terrace behind the front edge.
        beds=MeshBatch();soil=MeshBatch();rail=MeshBatch()
        outer=contour(5,1.25);inner=contour(5,3.3)
        for i in list(range(2,36))+list(range(53,94)):
            a,b=outer[i],outer[i+1];c,d=inner[i],inner[i+1]
            middle=((a[0]+b[0]+c[0]+d[0])/4,(a[1]+b[1]+c[1]+d[1])/4)
            # Avoid planting through either tower's footprint.
            if any(inside(middle,[tower_point(cfg,k/64,26)[:2] for k in range(64)]) for cfg in TOWERS): continue
            soil.face([(a[0],a[1],26.05+wave(*a,5)),(b[0],b[1],26.05+wave(*b,5)),(d[0],d[1],26.05+wave(*d,5)),(c[0],c[1],26.05+wave(*c,5))])
            beds.tube([(a[0],a[1],25.88+wave(*a,5)),(b[0],b[1],25.88+wave(*b,5))],.25,.55)
            beds.tube([(c[0],c[1],25.88+wave(*c,5)),(d[0],d[1],25.88+wave(*d,5))],.25,.55)
        terrace=contour(5,.35)
        rail.tube([(x,y,26.65+wave(x,y,5)) for x,y in terrace]+[(terrace[0][0],terrace[0][1],26.65+wave(*terrace[0],5))],.075,.09)
        for i in range(0,len(terrace),3):
            x,y=terrace[i];rail.tube([(x,y,25.45+wave(x,y,5)),(x,y,26.65+wave(x,y,5))],.07)
        beds.object('Podium_Terrace_PlanterEdges',['Podium','Roof'],['Concrete_Dark'],parent)
        soil.object('Podium_Terrace_Planting',['Podium','Ground_Details'],['TerraceGreen'],parent)
        rail.object('Podium_Terrace_Railings',['Podium','Frames'],['BlackMetal'],parent)
        # Entry doors are distinct frame/panel geometry, not a painted texture.
        doors=MeshBatch()
        for x in (-1.5,2,5.5):
            doors.tube([(x,-13.32,.46),(x,-13.32,4.25)],.15,.24)
        doors.tube([(-1.5,-13.32,4.25),(5.5,-13.32,4.25)],.18,.24)
        for x in (1.55,2.45): doors.tube([(x,-13.5,1.65),(x,-13.5,2.55)],.045)
        doors.object('MainEntrance_DoorFrames',['Entrances','Frames'],['BlackMetal'],ROOT)

build_podium()

if FINAL:
    sign_contour=contour(2,-.18)[:28]
    def sign_y(x):
        for a,b in zip(sign_contour[:-1],sign_contour[1:]):
            if min(a[0],b[0])<=x<=max(a[0],b[0]) and abs(b[0]-a[0])>.0001:
                return a[1]+(b[1]-a[1])*(x-a[0])/(b[0]-a[0])-.25
        return -29.0
    # Text is converted to mesh; no font dependency is required by FBX or Unity.
    for body,size,x,z in [('HAN DAE',1.7,-25,20.3),('CONSTRUCTION',.91,-25,18.45)]:
        cu=bpy.data.curves.new('CompanySign','FONT');cu.body=body;cu.size=size;cu.align_x='CENTER';cu.extrude=.025;cu.resolution_u=3
        ob=bpy.data.objects.new('Sign_'+body.replace(' ','_'),cu);COLS['Signage'].objects.link(ob)
        cu.materials.append(MATS['SignMaterial'])
        bpy.ops.object.select_all(action='DESELECT');ob.select_set(True);bpy.context.view_layer.objects.active=ob
        bpy.ops.object.convert(target='MESH');bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
        for vertex in ob.data.vertices:
            vx,vy,vz=vertex.co
            world_x=x+vx
            vertex.co=(world_x,sign_y(world_x)-vz,z+vy)
        ob.data.update()
        ob.parent=ROOT;ob['export_asset']=True
        uv=ob.data.uv_layers.new(name='UV0_MetreTile')
        for loop in ob.data.loops:
            co=ob.data.vertices[loop.vertex_index].co;uv.data[loop.index].uv=(co.x/4,co.z/4)
    logo=MeshBatch()
    logo.tube([(-34,-28.62,18.2),(-34,-28.62,21.6)],.30,.12)
    logo.tube([(-31.7,-28.62,18.2),(-31.7,-28.62,21.6)],.30,.12)
    logo.tube([(-34,-28.62,19.9),(-31.7,-28.62,19.9)],.30,.12)
    logo.v=[(x,sign_y(x)+(y+28.62),z) for x,y,z in logo.v]
    logo.object('Sign_Generic_H_Mark',['Signage'],['SignMaterial'],ROOT)

# Studio geometry is excluded from the model hierarchy and every export.
def presentation_object(name,kind):
    data=getattr(bpy.data,kind+'s').new(name, 'AREA') if kind=='light' else bpy.data.cameras.new(name)
    ob=bpy.data.objects.new(name,data);COLS['_Presentation'].objects.link(ob);return ob

floor=MeshBatch();box(floor,(0,0,-.24),(4000,4000,.4))
FLOOR_OB=floor.object('_StudioGround',['_Presentation'],['Paving'] if FINAL else [CLAY],None)
FLOOR_OB['export_asset']=False
world=bpy.data.worlds.new('StudioSky');world.use_nodes=True
world.node_tree.nodes['Background'].inputs[0].default_value=(.32,.42,.59,1) if FINAL else (.55,.61,.68,1)
world.node_tree.nodes['Background'].inputs[1].default_value=.45 if FINAL else .65
scene.world=world

def area(name,pos,power,size,color,target=(0,0,70)):
    ob=presentation_object(name,'light');ob.data.energy=power;ob.data.shape='DISK';ob.data.size=size;ob.data.color=color;ob.location=pos
    ob.rotation_euler=(Vector(target)-ob.location).to_track_quat('-Z','Y').to_euler();return ob

area('Key_SoftSun',(-130,-170,230),420000,120,(1,.81,.64))
area('Sky_Fill',(130,-30,200),350000,120,(.64,.79,1))
area('Rim',(-10,160,210),500000,90,(.78,.86,1))
sun_data=bpy.data.lights.new('Sun','SUN');sun=bpy.data.objects.new('Sun',sun_data);COLS['_Presentation'].objects.link(sun)
sun.rotation_euler=(math.radians(27),math.radians(-24),math.radians(-25));sun_data.energy=1.7 if FINAL else 2.1;sun_data.angle=.12

def cam(name,pos,target,scale,ortho=True):
    ob=presentation_object('Camera_'+name,'camera');ob.location=pos;ob.rotation_euler=(Vector(target)-ob.location).to_track_quat('-Z','Y').to_euler()
    ob.data.type='ORTHO' if ortho else 'PERSP';ob.data.ortho_scale=scale;ob.data.lens=55;ob.data.clip_end=6000
    return ob

CAMS={
    'front':cam('Front',(0,-340,105),(0,0,78),185),
    'left':cam('Left',(-275,-150,95),(0,0,75),187),
    'right':cam('Right',(275,-150,95),(0,0,75),187),
    'top':cam('Top',(0,0,360),(0,0,0),105),
    'hero':cam('Hero',(185,-330,115),(0,0,74),190,False),
    'terrace':cam('Terrace',(125,-155,170),(0,0,30),126),
}
scene.camera=CAMS['hero']
scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=48 if FINAL else 20;scene.cycles.use_denoising=True
scene.cycles.max_bounces=5;scene.cycles.diffuse_bounces=2;scene.cycles.glossy_bounces=3;scene.cycles.transmission_bounces=2
scene.view_settings.view_transform='AgX';scene.view_settings.exposure=.3 if FINAL else 0
scene.render.resolution_x=1320;scene.render.resolution_y=1600;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'

model_meshes=[obj for obj in ROOT.children_recursive if obj.type=='MESH']
vertices=sum(len(o.data.vertices) for o in model_meshes)
triangles=sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in model_meshes)
manifest={'stage':args.stage,'dimensions_target_m':{'Tower_A':160,'Tower_B':150,'Podium':26.1,'footprint_width_approx':90,'footprint_depth_approx':67},
          'objects':len(model_meshes),'vertices':vertices,'triangles':triangles,'units':'metres','front':'Blender -Y',
          'reference_priority':'Four supplied reference views; inward facing towers and wavy podium',
          'materials':list(MATS),'collections':[c for c in COLS if not c.startswith('_')],
          'scope':'Exterior only, no full interior. Shadow planes and emission window modules are mock interiors.',
          'scene_integration':'Standalone asset; existing 24 m / 65.5 m Handae scene object has not been replaced.',
          'led_surface_offset_m':.36,'notes':['Matched opposing plan rotations +/-22 degrees','Towers bend inward by 6.5 m then relax 4.5 m','Glazing modules use consistent arc-length sampling','Front podium ribbons rise up to 3.35 m towards the recessed centre']}
(OUT/(args.stage+'_manifest.json')).write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')

blend_path=OUT/('HandaeHQ_v02.blend' if FINAL else 'HandaeHQ_v02_Blockout.blend')
bpy.ops.wm.save_as_mainfile(filepath=str(blend_path))
if FINAL:
    bpy.ops.object.select_all(action='DESELECT');ROOT.select_set(True)
    for obj in ROOT.children_recursive: obj.select_set(True)
    bpy.context.view_layer.objects.active=ROOT
    bpy.ops.export_scene.fbx(filepath=str(EXPORT/'HandaeHQ_v02.fbx'),use_selection=True,global_scale=1,apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_UNITS',axis_forward='-Z',axis_up='Y',object_types={'MESH','EMPTY'},
        use_mesh_modifiers=True,mesh_smooth_type='FACE',use_tspace=True,add_leaf_bones=False,bake_anim=False,path_mode='COPY',embed_textures=True)
    bpy.ops.export_scene.gltf(filepath=str(EXPORT/'HandaeHQ_v02.glb'),export_format='GLB',use_selection=True,export_yup=True,export_apply=True)

requested=args.views.split(',')
for view in requested:
    if view not in CAMS:continue
    scene.camera=CAMS[view]
    if view=='top': scene.render.resolution_x=1400;scene.render.resolution_y=1300
    elif view=='terrace':scene.render.resolution_x=1500;scene.render.resolution_y=1200
    else:scene.render.resolution_x=1320;scene.render.resolution_y=1600
    scene.render.filepath=str(REVIEW/(view+'.png'))
    bpy.ops.render.render(write_still=True)
scene.camera=CAMS['hero']
scene.render.resolution_x=1320;scene.render.resolution_y=1600
bpy.ops.wm.save_as_mainfile(filepath=str(blend_path))
print('HAND_AE_COMPLETE '+json.dumps(manifest,ensure_ascii=False))
