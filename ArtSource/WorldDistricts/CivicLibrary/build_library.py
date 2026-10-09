"""Civic Library: meter-scale Blender source and Unity FBX export.
Run: blender --background --python build_library.py
Authoring axes: X horizontal, -Y street/front, Z up. Unity FBX: -Z front, Y up.
"""
import bpy, math, random, json, os, sys
from mathutils import Vector
import numpy as np

OUT = os.path.dirname(os.path.abspath(__file__))
TEX = os.path.join(OUT, 'Textures')
os.makedirs(TEX, exist_ok=True)
random.seed(117)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
for d in list(bpy.data.materials): bpy.data.materials.remove(d)
SCENE = bpy.context.scene
SCENE.unit_settings.system='METRIC'
SCENE.unit_settings.scale_length=1.0
groups = {}
M = {}
texture_files = {}

def collection(name):
    c = bpy.data.collections.new(name)
    SCENE.collection.children.link(c)
    groups[name] = c
    return c

for n in ('Structure','Glass','Frames','Wood','Roof','Steps','Ramp','Railings','Bench','Landscape','InteriorHint'):
    collection(n)

def assign(obj, name, group, mat):
    obj.name = name
    for c in list(obj.users_collection): c.objects.unlink(obj)
    groups[group].objects.link(obj)
    obj.data.materials.append(M[mat])
    return obj

def image(name, pixels, noncolor=False):
    h,w,_ = pixels.shape
    im = bpy.data.images.new(name, width=w, height=h, alpha=True)
    if noncolor: im.colorspace_settings.name='Non-Color'
    im.pixels.foreach_set(pixels.astype(np.float32).ravel())
    im.filepath_raw=os.path.join(TEX,name+'.png')
    im.file_format='PNG'
    im.save()
    return im

def make_textures():
    n=1024; y,x=np.mgrid[0:n,0:n]; rng=np.random.default_rng(117)
    rows=y//64; shifted=(x+(rows%2)*85)%n; cols=shifted//171
    variation=rng.uniform(-.035,.035,(16,7))[rows,cols]
    grain=rng.normal(0,.013,(n,n))
    mortar=(y%64<6)|(shifted%171<5)
    v=np.clip(.24+variation+grain,.12,.38); v[mortar]=.15+grain[mortar]*.3
    p=np.stack([v*.95,v,v*1.035,np.ones_like(v)],-1)
    brick=image('CL_Brick_BaseColor',p)
    heights=np.where(mortar,.12,.68)+grain*2
    dy,dx=np.gradient(heights); normals=np.stack([-dx*4,-dy*4,np.ones_like(dx)],-1)
    normals/=np.linalg.norm(normals,axis=-1)[:,:,None]
    normal=image('CL_Brick_Normal',np.concatenate([normals*.5+.5,np.ones((n,n,1))],axis=-1),True)
    # Vertical oak planks: grain belongs to each wood surface, not a painted mask.
    phase=x/1024*90+np.sin(y/1024*13)*.8
    grain=(np.sin(phase)+.35*np.sin(phase*4.7)+.1*np.sin(phase*12))* .023
    boards=(x//128)%8
    variation=np.array([.03,-.01,.02,-.025,.01,-.01,.018,-.018])[boards]
    seam=np.where(x%128<2,-.08,0)
    oak=np.stack([.57+grain+variation+seam,.36+grain*.75+variation*.7+seam,.18+grain*.5+variation*.4+seam,np.ones_like(grain)],-1)
    wood=image('CL_Oak_BaseColor',np.clip(oak,0,1))
    return brick,normal,wood

brick,brick_normal,oak = make_textures()

def mat(name, color, rough=.6, metal=0., tex=None, normal=None, emission=None, glass=False):
    m=bpy.data.materials.new('CL_'+name); m.use_nodes=True
    m.node_tree.nodes.clear()
    bs=m.node_tree.nodes.new('ShaderNodeBsdfPrincipled');bs.name='Principled BSDF'
    output=m.node_tree.nodes.new('ShaderNodeOutputMaterial');output.is_active_output=True
    m.node_tree.links.new(bs.outputs['BSDF'],output.inputs['Surface'])
    bs.inputs['Base Color'].default_value=(*color,1)
    bs.inputs['Roughness'].default_value=rough
    bs.inputs['Metallic'].default_value=metal
    m.diffuse_color=(*color,.22 if glass else 1)
    if tex:
        t=m.node_tree.nodes.new('ShaderNodeTexImage'); t.image=tex
        m.node_tree.links.new(t.outputs['Color'],bs.inputs['Base Color'])
    if normal:
        t=m.node_tree.nodes.new('ShaderNodeTexImage'); t.image=normal
        norm=m.node_tree.nodes.new('ShaderNodeNormalMap'); norm.inputs['Strength'].default_value=.45
        m.node_tree.links.new(t.outputs['Color'],norm.inputs['Color'])
        m.node_tree.links.new(norm.outputs['Normal'],bs.inputs['Normal'])
    if emission:
        bs.inputs['Emission Color'].default_value=(*color,1)
        bs.inputs['Emission Strength'].default_value=emission
    if glass:
        bs.inputs['Transmission Weight'].default_value=1
        bs.inputs['IOR'].default_value=1.45
        bs.inputs['Coat Weight'].default_value=.12
    M[name]=m
    return m

mat('Brick',(.24,.25,.265),.86,tex=brick,normal=brick_normal)
mat('Oak',(.57,.36,.18),.55,tex=oak)
mat('Glass',(.91,.97,1),.065,glass=True)
mat('Frame',(.045,.052,.055),.35,.7)
mat('Concrete',(.61,.625,.62),.8)
mat('Plaster',(.84,.82,.75),.82)
mat('Pavement',(.67,.67,.63),.88)
mat('Grass',(.21,.30,.11),.94)
mat('Soil',(.13,.105,.075),.98)
mat('LeafDark',(.11,.23,.07),.85)
mat('LeafLight',(.30,.41,.115),.86)
mat('Bark',(.24,.20,.15),.96)
mat('Paper',(.81,.77,.60),.92)
mat('BookNavy',(.11,.20,.24),.85)
mat('BookOchre',(.58,.38,.12),.82)
mat('BookSage',(.31,.41,.29),.83)
mat('BookTerracotta',(.47,.21,.13),.86)
mat('Upholstery',(.24,.31,.255),.9)
mat('Light',(.98,.92,.73),.32,emission=4)

def uv_box(obj, meters=2.56):
    mesh=obj.data; uv=mesh.uv_layers.new(name='UVMap')
    for p in mesh.polygons:
        ax=max(range(3),key=lambda a:abs(p.normal[a]))
        dims=(1,2) if ax==0 else (0,2) if ax==1 else (0,1)
        for li in p.loop_indices:
            co=mesh.vertices[mesh.loops[li].vertex_index].co
            uv.data[li].uv=(co[dims[0]]/meters,co[dims[1]]/meters)

def box(name, loc, size, matname, group='Structure', bevel=0):
    x,y,z=(s/2 for s in size)
    verts=[(-x,-y,-z),(x,-y,-z),(x,y,-z),(-x,y,-z),(-x,-y,z),(x,-y,z),(x,y,z),(-x,y,z)]
    faces=[(0,3,2,1),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7),(4,5,6,7)]
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update()
    ob=bpy.data.objects.new(name,mesh);ob.location=loc
    assign(ob,name,group,matname); uv_box(ob)
    if bevel:
        mod=ob.modifiers.new('Soft edge','BEVEL'); mod.width=bevel; mod.segments=1
        bpy.context.view_layer.objects.active=ob; bpy.ops.object.modifier_apply(modifier=mod.name)
        ob.data.update()
    return ob

def beam(name, a,b,r, matname='Frame',group='Railings',verts=8):
    d=Vector(b)-Vector(a); mid=(Vector(a)+Vector(b))/2
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts,radius=r,depth=d.length,location=mid)
    ob=bpy.context.object; ob.rotation_euler=d.to_track_quat('Z','Y').to_euler()
    assign(ob,name,group,matname); return ob

def ico(name, loc, size, matname, group='Landscape', sub=1):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=sub,radius=1,location=loc)
    ob=bpy.context.object; ob.scale=size
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    assign(ob,name,group,matname)
    for p in ob.data.polygons:p.use_smooth=True
    return ob

def panel_glass(name, xa,xb,ya,yb,za,zb):
    box(name,((xa+xb)/2,(ya+yb)/2,(za+zb)/2),(xb-xa,yb-ya,zb-za),'Glass','Glass')

def facade_window(name, a,b,zlo,zhi,y=-9.015):
    panel_glass(name,a,b,y-.025,y+.025,zlo,zhi)
    for x in [a+i*(b-a)/max(1,round((b-a)/1.5)) for i in range(max(1,round((b-a)/1.5))+1)]:
        box(name+'_mullion',(x,y-.055,(zlo+zhi)/2),(.065,.13,zhi-zlo),'Frame','Frames')
    for z in (zlo,zhi):box(name+'_rail',((a+b)/2,y-.055,z),(b-a,.14,.075),'Frame','Frames')

# Site datum and building floor plates.
box('Site_38x29', (0,-5.5,-.132),(38,29,.30),'Pavement','Landscape')
box('Foundation',(0,0,.37),(30,18,.74),'Concrete')
box('GroundFloor',(0,0,.77),(29.5,17.5,.06),'Plaster')
box('FirstFloor',(0,0,4.97),(30,18,.28),'Concrete')
box('UpperFloorFinish',(0,0,5.125),(29.5,17.5,.035),'Plaster')
box('FirstFloorCeiling',(0,0,4.81),(29.5,17.5,.04),'Plaster','InteriorHint')
box('UpperCeiling',(0,0,9.31),(30,18,.12),'Plaster','InteriorHint')
box('BackBrickWall',(0,8.83,5.05),(30,.34,8.5),'Brick')
box('LeftBrickWall',(-14.83,0,5.05),(.34,18,8.5),'Brick')
box('BackInnerWall',(0,8.62,5.05),(29.5,.04,8.4),'Plaster','InteriorHint')
box('LeftInnerWall',(-14.62,0,5.05),(.04,17.5,8.4),'Plaster','InteriorHint')

# Readable two-story front facade, glazed corner and quiet brick bays.
for name,a,b in [('WestPier',-15,-12.2),('EastPier',7.8,10.0)]:
    box(name,((a+b)/2,-8.89,5.05),(b-a,.38,8.5),'Brick')
for a,b in [(-12.2,-4.6),(4.6,7.8),(10,15)]:
    for lo,hi in ((.9,4.78),(5.17,9.28)):facade_window('FrontGlazing',a,b,lo,hi)
    box('BrickSpandrel',((a+b)/2,-8.95,4.965),(b-a,.35,.4),'Brick')
for x in (-3.85,3.85):
    box('EntryOakBacking',(x,-8.985,5.05),(1.5,.30,8.5),'Oak','Wood')
    for j in range(12):
        box('VerticalOakFin',(x-.7+j*.127,-9.195,5.05),(.066,.16,8.5),'Oak','Wood')
facade_window('EntranceUpper',-3.1,3.1,5.0,9.28)
facade_window('EntranceLeft',-3.1,-1.55,.82,4.8)
facade_window('EntranceRight',1.55,3.1,.82,4.8)
facade_window('EntranceTransom',-1.55,1.55,3.65,4.8)
for x in (-.78,.78):
    panel_glass('CentralDoorGlass',x-.75,x+.75,-9.10,-9.05,.82,3.65)
    for sx in (x-.75,x+.75):box('DoorStile',(sx,-9.14,2.235),(.085,.13,2.83),'Frame','Frames')
    for z in (.87,3.61):box('DoorRail',(x,-9.14,z),(1.5,.13,.1),'Frame','Frames')
    beam('DoorHandle',(x+(.46 if x<0 else -.46),-9.26,1.75),(x+(.46 if x<0 else -.46),-9.26,2.35),.023)

# East return: glass wraps corner, framed with brick piers and spandrels.
for ya,yb in [(-9,-3),(-2.3,3.2),(3.9,7.8)]:
    for lo,hi in ((.9,4.78),(5.17,9.28)):
        panel_glass('EastCornerGlass',14.975,15.025,ya,yb,lo,hi)
        count=max(1,round((yb-ya)/1.5))
        for i in range(count+1):box('EastMullion',(15.06,ya+(yb-ya)*i/count,(lo+hi)/2),(.13,.065,hi-lo),'Frame','Frames')
        for z in (lo,hi):box('EastRail',(15.06,(ya+yb)/2,z),(.13,yb-ya,.075),'Frame','Frames')
    box('EastSpandrel',(14.93,(ya+yb)/2,4.97),(.35,yb-ya,.4),'Brick')
for ya,yb in [(-3,-2.3),(3.2,3.9),(7.8,9)]:
    box('EastBrickPier',(14.84,(ya+yb)/2,5.05),(.34,yb-ya,8.5),'Brick')

# Thin projecting roof with warm timber soffit; no bulky decorative roof box.
box('ThinRoofCanopy',(0,-.12,9.55),(32.6,20.5,.26),'Frame','Roof',.025)
box('OakSoffit',(0,-.12,9.397),(32.2,20.15,.045),'Oak','Roof')
for x in [-13,-9,-5,0,5,9,13]:
    bpy.ops.mesh.primitive_cylinder_add(vertices=12,radius=.075,depth=.025,location=(x,-9.7,9.363))
    assign(bpy.context.object,'SoffitDownlight','Roof','Light')

# Front landing and exactly five low stair treads.
box('EntranceLanding',(0,-10,.365),(30,2,.75),'Concrete')
for i in range(5):
    height=.15*(i+1); front=-13.1+i*.42; back=front+.42
    box('EntryStep_%02d'%(i+1),(0,(front+back)/2,height/2),(6.4,back-front,height),'Concrete','Steps')
for x in (-3.11,3.11):
    beam('StairHandrail',(x,-13.12,1.13),(x,-10.8,1.92),.027)
    for y,z in [(-13.03,.15),(-12.0,.45),(-11.05,.75)]:beam('StairPost',(x,y,z),(x,y,z+1.0),.022)
    beam('StairLandingRail',(x,-10.8,1.92),(x,-10.2,1.92),.027)

def ramp(name, xa,xb,ya,yb,za,zb):
    verts=[(xa,ya,0),(xb,ya,0),(xb,yb,0),(xa,yb,0),(xa,ya,za),(xb,ya,zb),(xb,yb,zb),(xa,yb,za)]
    faces=[(0,3,2,1),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7),(4,5,6,7)]
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update()
    ob=bpy.data.objects.new(name,mesh);groups['Ramp'].objects.link(ob);mesh.materials.append(M['Concrete']);uv_box(ob)
    return ob

ramp('Ramp_LowerRun_1in20',7,14.5,-17,-15.2,.018,.393)
box('Ramp_TurnLanding',(15.5,-15,.1965),(2,4,.393),'Concrete','Ramp')
ramp('Ramp_UpperRun_1in20',7,14.5,-14.8,-13,.768,.393)
box('Ramp_TopLanding',(6.1,-12.9,.384),(1.8,3.8,.768),'Concrete','Ramp')

def ramp_rail(name, xa,xb,y,za,zb):
    for lift in (1.02,.54):beam(name,(xa,y,za+lift),(xb,y,zb+lift),.024 if lift>1 else .018)
    count=math.ceil(abs(xb-xa)/1.6)
    for i in range(count+1):
        t=i/count;x=xa+(xb-xa)*t;z=za+(zb-za)*t
        beam(name+'_post',(x,y,z),(x,y,z+1.02),.022)

ramp_rail('LowerOuterRail',7,16.5,-17,.018,.393)
# Correct linear lower rail slope to run endpoints; turn portion remains level.
for ob in list(groups['Railings'].objects):
    if ob.name.startswith('LowerOuterRail'):bpy.data.objects.remove(ob,do_unlink=True)
ramp_rail('LowerOuterRail',7,14.5,-17,.018,.393)
ramp_rail('TurnFrontRail',14.5,16.5,-17,.393,.393)
ramp_rail('LowerInnerRail',7,14.5,-15.2,.018,.393)
ramp_rail('UpperInnerRail',7,14.5,-14.8,.768,.393)
ramp_rail('UpperOuterRail',7,14.5,-13,.768,.393)
ramp_rail('TurnRearRail',14.5,16.5,-13,.393,.393)
for lift in (1.02,.54):beam('TurnRail',(16.5,-17,.393+lift),(16.5,-13,.393+lift),.024)
for y in (-17,-15,-13):beam('TurnPost',(16.5,y,.393),(16.5,y,1.413),.022)
for x in (5.2,7):
    start_y=-14.8 if x==5.2 else -13
    for lift in (1.02,.54):beam('TopLandingRail',(x,start_y,.768+lift),(x,-11,.768+lift),.024)
    for y in ((-14.8,-13,-11) if x==5.2 else (-13,-11)):beam('TopLandingPost',(x,y,.768),(x,y,1.788),.022)
# The top route opens onto the 2m-wide facade landing at y=-11, connected to central door.

# Exactly one front-left bench, six seat slats and four backrest slats.
for j in range(6):box('BenchSeatSlat',(-10.5,-15.6+j*.095,.53),(2.5,.08,.065),'Oak','Bench',.012)
for j in range(4):box('BenchBackSlat',(-10.5,-15.13,.75+j*.105),(2.5,.06,.088),'Oak','Bench',.012)
for x in (-11.48,-9.52):
    for y in (-15.58,-15.15):box('BenchLeg',(x,y,.26),(.07,.07,.52),'Frame','Bench')
    box('BenchBackSupport',(x,-15.10,.7),(.05,.05,.83),'Frame','Bench')
    box('BenchFoot',(x,-15.4,.055),(.12,.68,.07),'Frame','Bench')

# Low, restrained landscape leaves sight lines and both approaches clear.
for name,loc,size in [
    ('WestLawn',(-10.55,-13.18,.01),(12.9,3.35,.04)),
    ('LeftBenchLawn',(-10.5,-16.75,.01),(13.0,.70,.04)),
    ('EastLawn',(17.55,-4.0,.01),(2.2,26,.04)),
    ('RampGarden',(10.9,-11.85,.05),(7.0,1.1,.10)),
    ('FrontEastLawn',(11.9,-18.35,.01),(13.8,1.5,.04))]:box(name,loc,size,'Grass','Landscape')

def shrub(name,x,y,z=.12,scale=.48,flowers=False):
    for j in range(4):
        a=j*2.4;ico(name,(x+math.cos(a)*scale*.45,y+math.sin(a)*scale*.38,z+scale*.45),(scale*.66,scale*.6,scale*.58),'LeafDark' if j%2 else 'LeafLight',sub=1)
    if flowers:
        for j in range(5):
            a=j*2.2;ico(name+'_blossom',(x+math.cos(a)*scale*.48,y+math.sin(a)*scale*.40,z+scale*.9),(.10,.10,.08),'Paper',sub=1)

for i in range(18):
    x=-16.3+i*.62
    shrub('WestFoundationShrub',x,-11.9,.08,.42, i%3==0)
for i in range(10):shrub('RampGardenShrub',7.6+i*.65,-11.86,.10,.33,i%3==0)
for i in range(10):shrub('EasternShrub',17.8,-14+i*2.1,.08,.40,i%3==0)
for x,y in [(-15.8,-14.5),(-6.1,-13.55),(-4.7,-12.2),(8.0,-18.1),(12.1,-18.3),(16.5,-18.15)]:shrub('FlowerBed',x,y,.10,.45,True)

def tree(name,x,y,h):
    beam(name+'_trunk',(x,y,.02),(x+.12,y,h*.73),.055,'Bark','Landscape',7)
    for j in range(5):
        a=j*2.4;end=Vector((x+math.cos(a)*.75,y+math.sin(a)*.65,h*.7+j*.11))
        beam(name+'_branch',(x+.08,y,h*.44+j*.1),end,.024,'Bark','Landscape',6)
    verts=[];faces=[]
    for j in range(350):
        a=random.uniform(0,math.tau);r=random.random()**.5
        zz=random.uniform(-1,1); spread=(max(.1,1-zz*zz))**.5
        p=Vector((x+math.cos(a)*r*spread*1.16,y+math.sin(a)*r*spread*.96,h*.78+zz*.96))
        size=random.uniform(.09,.17);theta=random.uniform(0,math.tau)
        u=Vector((math.cos(theta),math.sin(theta),random.uniform(-.4,.4)))*size
        v=Vector((-math.sin(theta),math.cos(theta),random.uniform(-.2,.2)))*size*.43
        k=len(verts);verts.extend([p-u,p+v,p+u,p-v]);faces.extend([(k,k+1,k+2),(k,k+2,k+3)])
    mesh=bpy.data.meshes.new(name+'_leaves');mesh.from_pydata(verts,[],faces);mesh.update()
    ob=bpy.data.objects.new(name+'_leaves',mesh);groups['Landscape'].objects.link(ob)
    mesh.materials.append(M['LeafDark']);mesh.materials.append(M['LeafLight'])
    for p in mesh.polygons:p.material_index=random.randrange(2)
    # Leaves intentionally two-sided in Unity foliage material mapping.

for name,x,y,h in [('TreeWest',-16,-12.65,4.1),('TreeWestYoung',-6.4,-12.5,3.6),('TreeEast',17.5,-10,4.3),('TreeEastRear',17.5,4.9,4.8),('TreeFrontEast',17.4,-18.2,3.7)]:tree(name,x,y,h)

# Shallow, closed decorative interior visible through transparent glazing.
def shelf(name,x,y,z,width=3.3):
    box(name+'_back',(x,y+.19,z+1.08),(width,.055,2.16),'Oak','InteriorHint')
    for side in (-1,1):box(name+'_side',(x+side*(width/2-.035),y,z+1.08),(.07,.46,2.16),'Oak','InteriorHint')
    for row in range(5):
        zz=z+.09+row*.49
        box(name+'_shelf',(x,y,zz),(width,.47,.055),'Oak','InteriorHint')
        if row<4:
            bx=x-width/2+.10
            while bx<x+width/2-.15:
                w=random.uniform(.065,.125);hh=random.uniform(.30,.40)
                bookmat=random.choice(['Paper','BookNavy','BookOchre','BookSage','BookTerracotta'])
                box(name+'_book',(bx+w/2,y-.02,zz+.03+hh/2),(w,.29,hh),bookmat,'InteriorHint')
                bx+=w+.016

for floor in (.81,5.15):
    for x in (-10,-6,5.9,11.7):shelf('LibraryBooks',x,-4.4,floor,3.25)
    for x in (-8,7.5):shelf('BackBooks',x,3.9,floor,4.2)
    for x in (-9.4,6.1,12.1):
        box('ReadingTable',(x,-6.65,floor+.74),(1.75,.86,.075),'Oak','InteriorHint',.02)
        for dx in (-.72,.72):
            for dy in (-.29,.29):box('ReadingTableLeg',(x+dx,-6.65+dy,floor+.36),(.055,.055,.70),'Frame','InteriorHint')
        for dx in (-.51,.51):
            cx=x+dx;cy=-7.55
            box('ReadingChairSeat',(cx,cy,floor+.44),(.48,.46,.07),'Upholstery','InteriorHint',.02)
            box('ReadingChairBack',(cx,cy-.22,floor+.76),(.48,.065,.55),'Upholstery','InteriorHint',.02)
            for lx in (-.18,.18):
                for ly in (-.17,.17):box('ReadingChairLeg',(cx+lx,cy+ly,floor+.21),(.035,.035,.42),'Oak','InteriorHint')
    for x in (-9,-3,3,9):
        box('CeilingLinearLight',(x,-3,floor+3.85),(3.0,.09,.025),'Light','InteriorHint')

# Join by material within rendering groups to limit Unity renderer count.
# Access geometry stays individually named for review and collider setup.
for g in ('Structure','Glass','Frames','Wood','Roof','Railings','Bench','Landscape','InteriorHint'):
    coll=groups[g]
    for material in list(M.values()):
        obs=[o for o in list(coll.objects) if o.type=='MESH' and len(o.data.materials)==1 and o.data.materials[0]==material]
        if len(obs)<2:continue
        bpy.ops.object.select_all(action='DESELECT')
        for o in obs:o.select_set(True)
        bpy.context.view_layer.objects.active=obs[0]
        bpy.ops.object.join();ob=obs[0];ob.name=g+'__'+material.name

# Normalize transforms, add a single root and category empties (FBX grouping).
root=bpy.data.objects.new('CivicLibrary',None);SCENE.collection.objects.link(root)
for g,coll in groups.items():
    parent=bpy.data.objects.new(g,None);coll.objects.link(parent);parent.parent=root
    for ob in list(coll.objects):
        if ob==parent:continue
        world=ob.matrix_world.copy();ob.parent=parent;ob.matrix_world=world
        if ob.type=='MESH':
            bpy.context.view_layer.objects.active=ob
            bpy.ops.object.select_all(action='DESELECT');ob.select_set(True)
            bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)

# Exports contain the model only; presentation ground, cameras and lights excluded.
model=[root]+[o for o in SCENE.objects if o.parent and (o.parent==root or o.parent.parent==root)]
bpy.ops.object.select_all(action='DESELECT')
for ob in model:ob.select_set(True)
bpy.context.view_layer.objects.active=root
fbx=os.path.join(OUT,'CivicLibrary.fbx')
bpy.ops.export_scene.fbx(filepath=fbx,use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',bake_space_transform=False,use_mesh_modifiers=True,add_leaf_bones=False,path_mode='RELATIVE',embed_textures=False)

points=[o.matrix_world@Vector(c) for o in model if o.type=='MESH' for c in o.bound_box]
mins=[min(p[i] for p in points) for i in range(3)];maxs=[max(p[i] for p in points) for i in range(3)]
triangle_count=sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in model if o.type=='MESH')
manifest={
    'units':'meters','blender_front':'-Y','unity_front':'-Z','floor_elevation':.75,
    'shell_dimensions':[30,18,8.8], 'bounds_min':mins,'bounds_max':maxs,
    'triangles':triangle_count,'mesh_objects':len([o for o in model if o.type=='MESH']),
    'access':{'stair_count':5,'stair_rise':.15,'stair_tread':.42,'stair_width':6.4,'ramp_run_length':7.5,'ramp_run_rise':.375,'ramp_width':1.8,'ramp_slope':'1:20','ramp_landing':[2,4],'bench_count':1,'plaza_elevation':.018,'upper_ramp_exit_opening':{'x':7,'y_min':-14.8,'y_max':-13,'width':1.8}},
    'materials':[]}
for name,m in M.items():
    bs=m.node_tree.nodes.get('Principled BSDF')
    entry={'name':m.name,'baseColor':list(m.diffuse_color),'roughness':float(bs.inputs['Roughness'].default_value),'metallic':float(bs.inputs['Metallic'].default_value)}
    if name=='Brick':entry.update(baseColorTexture='Textures/CL_Brick_BaseColor.png',normalTexture='Textures/CL_Brick_Normal.png')
    if name=='Oak':entry.update(baseColorTexture='Textures/CL_Oak_BaseColor.png')
    if name=='Glass':entry.update(unityShader='Universal Render Pipeline/Lit',surface='Transparent',alpha=.18,cull='Off',smoothness=.92,castShadows=False)
    if name.startswith('Leaf'):entry.update(cull='Off')
    if name=='Light':entry.update(emission=[.98,.92,.73,1],emissionStrength=2)
    entry['base_color']=entry['baseColor']
    entry['base_texture']=entry.get('baseColorTexture','')
    entry['normal_texture']=entry.get('normalTexture','')
    manifest['materials'].append(entry)
with open(os.path.join(OUT,'material_manifest.json'),'w',encoding='utf-8') as f:json.dump(manifest,f,ensure_ascii=False,indent=2)

# Presentation objects are kept in their own excluded collection in the .blend.
present=bpy.data.collections.new('_Presentation_ONLY');SCENE.collection.children.link(present)
def present_link(o):
    for c in list(o.users_collection):c.objects.unlink(o)
    present.objects.link(o)

road=bpy.data.materials.new('PresentationRoad');road.diffuse_color=(.18,.19,.19,1);road.use_nodes=True
road.node_tree.nodes.clear()
road_bs=road.node_tree.nodes.new('ShaderNodeBsdfPrincipled');road_out=road.node_tree.nodes.new('ShaderNodeOutputMaterial')
road.node_tree.links.new(road_bs.outputs['BSDF'],road_out.inputs['Surface']);road_bs.inputs['Base Color'].default_value=(.18,.19,.19,1)
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.325));ground=bpy.context.object;ground.name='_BackdropGround';ground.data.materials.append(road);present_link(ground)
bpy.ops.mesh.primitive_cube_add(size=1,location=(0,-21,-.14));curb=bpy.context.object;curb.name='_PresentationCurb';curb.dimensions=(52,1.7,.28);curb.data.materials.append(M['Pavement']);present_link(curb)

world=bpy.data.worlds.new('ClearDay') if not SCENE.world else SCENE.world;SCENE.world=world;world.use_nodes=True
nodes=world.node_tree.nodes;nodes.clear();links=world.node_tree.links
out=nodes.new('ShaderNodeOutputWorld');mix=nodes.new('ShaderNodeMixShader');lp=nodes.new('ShaderNodeLightPath')
ambient=nodes.new('ShaderNodeBackground');ambient.inputs['Color'].default_value=(.65,.77,1,1);ambient.inputs['Strength'].default_value=.5
sky=nodes.new('ShaderNodeBackground');sky.inputs['Color'].default_value=(.38,.62,.84,1);sky.inputs['Strength'].default_value=.8
links.new(lp.outputs['Is Camera Ray'],mix.inputs[0]);links.new(ambient.outputs[0],mix.inputs[1]);links.new(sky.outputs[0],mix.inputs[2]);links.new(mix.outputs[0],out.inputs[0])
bpy.ops.object.light_add(type='SUN',location=(0,-15,20));sun=bpy.context.object;sun.name='_DaySun';sun.rotation_euler=(math.radians(25),math.radians(-28),math.radians(-25));sun.data.energy=2.0;sun.data.angle=.035;present_link(sun)
for z in (3.9,8.25):
    for x in (-8,7):
        bpy.ops.object.light_add(type='AREA',location=(x,-4,z));light=bpy.context.object;light.name='_InteriorPreviewLight';light.data.energy=330;light.data.shape='DISK';light.data.size=5;light.data.color=(1,.88,.72);present_link(light)

def camera(name,loc,target,lens):
    bpy.ops.object.camera_add(location=loc);cam=bpy.context.object;cam.name=name;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.lens=lens;cam.data.clip_end=300;present_link(cam);return cam
eye=camera('Camera_EyeLevel',(35,-56,3.4),(0,-1,4.4),45)
overview=camera('Camera_AccessOverview',(35,-49,29),(0,-3,3.0),47)
SCENE.camera=eye;SCENE.render.engine='CYCLES';SCENE.cycles.samples=48;SCENE.cycles.use_denoising=True
SCENE.render.resolution_x=1500;SCENE.render.resolution_y=950;SCENE.render.resolution_percentage=100
SCENE.render.image_settings.file_format='PNG';SCENE.view_settings.view_transform='AgX'
SCENE.render.film_transparent=False
SCENE.render.filepath=os.path.join(OUT,'CivicLibrary_EyeLevel.png')
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT,'CivicLibrary.blend'))
bpy.ops.file.make_paths_relative()
if '--no-render' not in sys.argv:bpy.ops.render.render(write_still=True)
SCENE.camera=overview;SCENE.render.filepath=os.path.join(OUT,'CivicLibrary_AccessOverview.png');SCENE.cycles.samples=32
if '--no-render' not in sys.argv:bpy.ops.render.render(write_still=True)
SCENE.camera=eye
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT,'CivicLibrary.blend'))
print('CIVIC_LIBRARY_DONE '+json.dumps({'triangles':triangle_count,'bounds_min':mins,'bounds_max':maxs,'fbx':fbx}))
