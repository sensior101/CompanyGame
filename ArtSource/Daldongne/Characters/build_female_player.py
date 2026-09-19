"""Reproducible reference-inspired low-poly girl. Run in a fresh Blender process.

All design coordinates are metres, Unity Y-up/+Z-forward. The .blend contains
editable named parts; the JSON carries explicit joint pivots and split normals
for a deterministic native Unity import, independent of FBX axis conventions.
"""
import bpy, bmesh, math, json, sys
from mathutils import Vector, Matrix
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
MALE = globals().get('MALE', '--male' in sys.argv)
ASSET_NAME = 'ReferenceBoy' if MALE else 'ReferenceGirl'
OUT = ROOT / 'CompanyGame/Assets/Art/Daldongne/Players' / ASSET_NAME
OUT.mkdir(parents=True, exist_ok=True)
HERE.mkdir(parents=True, exist_ok=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
for data in list(bpy.data.materials):
    bpy.data.materials.remove(data)

COLORS = {
    'Skin': '#E9B899', 'Hair': '#553C32', 'HairHighlight': '#70503D',
    'Sage': '#718A77', 'SageRib': '#4C6758', 'Cream': '#E5D7B8',
    'Skirt': '#A9765C', 'Boot': '#504139', 'Sole': '#C0AB8C',
    'Brass': '#CBA967', 'Rose': '#CE9686',
    'EyeInk': '#34251E', 'EyeIris': '#492B1D', 'EyeSpark': '#F6EEE0',
    'SkinWarm': '#E4AD96', 'LipGlow': '#DDA38E',
}
def linear(v): return v/12.92 if v <= .04045 else ((v+.055)/1.055)**2.4
mats = {}
for name, color in COLORS.items():
    rgb = tuple(int(color[i:i+2],16)/255 for i in (1,3,5))
    m = bpy.data.materials.new(name); m.diffuse_color=(*rgb,1); m.use_nodes=True
    bsdf=next((n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED'),None)
    if bsdf is None:
        bsdf=m.node_tree.nodes.new('ShaderNodeBsdfPrincipled')
        output=m.node_tree.nodes.new('ShaderNodeOutputMaterial')
        m.node_tree.links.new(bsdf.outputs['BSDF'],output.inputs['Surface'])
    bsdf.inputs['Base Color'].default_value=(*(linear(v) for v in rgb),1)
    bsdf.inputs['Roughness'].default_value=.76
    mats[name]=m

# Joint values are WORLD rest positions; meshes are exported relative to them.
JOINTS = {
    'Hips': (None, (0,.767,0)),
    'Head': ('Hips',(0,1.43,0)),
    'Hair': ('Head',(0,1.43,-.13)),
    'LeftArm': ('Hips',(-.255,1.12,0)),
    'RightArm': ('Hips',(.255,1.12,0)),
    'LeftLeg': ('Hips',(-.118,.73,0)),
    'RightLeg': ('Hips',(.118,.73,0)),
    'LeftKnee': ('LeftLeg',(-.118,.425,0)),
    'RightKnee': ('RightLeg',(.118,.425,0)),
}
parts=[]
def B(p): return Vector((p[0],-p[2],p[1]))
def U(p): return [round(p.x,6),round(p.z,6),round(-p.y,6)]
def mesh(name,verts,faces,mat,joint='Hips',smooth=False):
    data=bpy.data.meshes.new(name); data.from_pydata([B(v) for v in verts],[],faces); data.update()
    bm=bmesh.new(); bm.from_mesh(data); bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces)); bm.to_mesh(data); bm.free()
    ob=bpy.data.objects.new(name,data); bpy.context.collection.objects.link(ob); data.materials.append(mats[mat])
    for p in data.polygons: p.use_smooth=smooth
    ob['joint']=joint; parts.append(ob); return ob

def loft(name,rings,mat,joint='Hips',n=12,smooth=False,cap=True):
    # ring = center x,y,z + horizontal x/z radii
    verts=[]
    for x,y,z,rx,rz in rings:
        for i in range(n):
            a=2*math.pi*i/n; verts.append((x+rx*math.sin(a),y,z+rz*math.cos(a)))
    faces=[]
    for r in range(len(rings)-1):
        for i in range(n):
            a=r*n+i; b=r*n+(i+1)%n; faces.append((a,b,b+n,a+n))
    if cap: faces += [tuple(range(n-1,-1,-1)),tuple((len(rings)-1)*n+i for i in range(n))]
    return mesh(name,verts,faces,mat,joint,smooth)

def ellipsoid(name,c,r,mat,joint='Hips',n=16,rings=8,smooth=False):
    levels=[]
    for j in range(rings+1):
        a=math.pi*j/rings; k=max(.001,math.sin(a))
        levels.append((c[0],c[1]+r[1]*math.cos(a),c[2],r[0]*k,r[2]*k))
    return loft(name,levels,mat,joint,n,smooth)

def tube(name,points,radius,mat,joint='Hips',n=6):
    verts=[]
    for i,point in enumerate(points):
        p=Vector(point); tangent=Vector(points[min(i+1,len(points)-1)])-Vector(points[max(0,i-1)])
        tangent.normalize(); axis=tangent.cross(Vector((0,0,1)))
        if axis.length<.01: axis=tangent.cross(Vector((0,1,0)))
        axis.normalize(); other=tangent.cross(axis).normalized()
        rad=radius[i] if isinstance(radius,list) else radius
        for k in range(n):
            a=math.tau*k/n; v=p+rad*(math.cos(a)*axis+math.sin(a)*other); verts.append(tuple(v))
    faces=[]
    for j in range(len(points)-1):
        for k in range(n):
            a=j*n+k;b=j*n+(k+1)%n;faces.append((a,b,b+n,a+n))
    faces += [tuple(range(n-1,-1,-1)),tuple((len(points)-1)*n+i for i in range(n))]
    return mesh(name,verts,faces,mat,joint)

def bevelbox(name,c,size,mat,joint='Hips',bevel=.012,rotation=0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=B(c));ob=bpy.context.object;ob.name=name
    ob.scale=(size[0],size[2],size[1]);bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    ob.rotation_euler[1]=rotation
    mod=ob.modifiers.new('Tailored edges','BEVEL');mod.width=bevel;mod.segments=1
    bpy.ops.object.modifier_apply(modifier=mod.name)
    ob.data.materials.append(mats[mat]);ob['joint']=joint;parts.append(ob);return ob

# Reference-directed face module shares the existing mesh/palette/joint helpers.
exec(compile((HERE/'build_cute_face.py').read_text(encoding='utf-8'), str(HERE/'build_cute_face.py'), 'exec'))

# Knit shirt, pleated A-line skirt and a framed rectangular belt buckle.
loft('Cotton shirt',[(0,.805,0,.16,.105),(0,1.055,0,.184,.13),(0,1.17,0,.115,.1)],'Cream',n=12)
loft('Ribbed crew neck',[(0,1.16,0,.094,.083),(0,1.205,0,.089,.081)],'Cream',n=12)
for i in range(9):
    a=math.tau*i/9;tube('Neck rib',[(.091*math.sin(a),1.168,.083*math.cos(a)),(.088*math.sin(a),1.198,.081*math.cos(a))],.0026,'Sole',n=4)
if MALE:
    loft('Tailored shorts waist',[(0,.722,0,.171,.115),(0,.832,0,.175,.120),(0,.860,0,.171,.118)],'Skirt',n=16)
else:
    loft('Pleated skirt',[(0,.591,0,.238,.16),(0,.62,0,.231,.157),(0,.834,0,.176,.12),(0,.86,0,.171,.118)],'Skirt',n=20)
    for i in range(12):
        a=math.tau*i/12;tube('Skirt pleat',[(.172*math.sin(a),.835,.12*math.cos(a)),(.230*math.sin(a),.607,.159*math.cos(a))],.003,'Sole',n=4)
loft('Waist belt',[(0,.829,0,.180,.125),(0,.867,0,.178,.124)],'Boot',n=16)
for s in (-1,1):
    bevelbox('Buckle upright',(s*.028,.848,.136),(.009,.044,.01),'Brass',bevel=.002)
    bevelbox('Belt keeper',(s*.09,.847,.117),(.013,.056,.018),'Cream',bevel=.002)
for y in (.831,.866):bevelbox('Buckle rail',(0,y,.136),(.064,.007,.011),'Brass',bevel=.002)

# Open bomber jacket: connected swept shell, not a solid block over the shirt.
levels=[(.635,.216,.148),(.68,.234,.16),(.90,.228,.159),(1.08,.232,.148),(1.155,.177,.116)]
verts=[]; faces=[]; sections=18
for y,rx,rz in levels:
    for i in range(sections+1):
        a=.40+(math.tau-.80)*i/sections
        verts.append((rx*math.sin(a),y,rz*math.cos(a)))
for j in range(len(levels)-1):
    for i in range(sections):
        a=j*(sections+1)+i;faces.append((a,a+1,a+sections+2,a+sections+1))
jacket=mesh('Open bomber body',verts,faces,'Sage',smooth=False)
bpy.context.view_layer.objects.active=jacket
mod=jacket.modifiers.new('Fabric thickness','SOLIDIFY');mod.thickness=.012
bpy.ops.object.modifier_apply(modifier=mod.name)
for s in (-1,1):
    edge=[(s*rx*math.sin(.4),y,rz*math.cos(.4)+.008) for y,rx,rz in levels]
    tube('Zip piping',edge,.009,'Cream')
    for i in range(15):
        y=.66+i*.029
        x=.088 if y<1.08 else .08;z=.158 if y<1.08 else .134
        bevelbox('Zipper tooth',(s*x,y,z),(.013,.009,.009),'Brass',bevel=.002)
    tube('Hem rib',[(s*.082,.635,.14),(s*.159,.635,.113),(s*.217,.635,.032),(s*.202,.635,-.084)],.018,'SageRib')
    tube('Welt pocket',[(s*.143,.771,.154),(s*.201,.826,.115)],.014,'SageRib')
    tube('Pocket upper stitch',[(s*.149,.769,.169),(s*.205,.821,.13)],.004,'Cream')
    bevelbox('Chest patch',(s*.141,1.057,.14),(.054,.066,.015),'SageRib',bevel=.005)
    bevelbox('Chest patch inset',(s*.141,1.057,.152),(.037,.047,.006),'Cream',bevel=.002)
    tube('Raised collar',[(s*.085,1.048,.145),(s*.089,1.143,.123),(s*.115,1.182,.066)],.025,'Sage')
    tube('Collar stitch',[(s*.087,1.056,.169),(s*.094,1.14,.147),(s*.12,1.18,.087)],.004,'Cream')
hood=[]
for i in range(13):
    a=math.pi/2+math.pi*i/12;hood.append((.148*math.sin(a),1.161,-.01+.12*math.cos(a)))
tube('Hood rolled rim',hood,.042,'Sage')
bevelbox('Zip pull',(.086,.625,.171),(.026,.048,.011),'Brass',bevel=.005)

# Balloon sleeves and mitten hands. Each is bound to a shoulder pivot.
for s,label in [(-1,'Left'),(1,'Right')]:
    arm=label+'Arm'; leg=label+'Leg'; knee=label+'Knee'
    loft(label+' puff sleeve',[(s*.243,1.12,0,.068,.086),(s*.281,1.059,0,.093,.103),(s*.299,.919,.004,.093,.104),(s*.334,.817,.026,.107,.109),(s*.325,.744,.03,.079,.077)],'Sage',arm,12)
    loft(label+' rib cuff',[(s*.325,.746,.03,.077,.074),(s*.32,.692,.031,.069,.066)],'SageRib',arm,12)
    for i in range(10):
        a=math.tau*i/10;tube(label+' cuff rib',[(s*.32+.071*math.sin(a),.699,.03+.068*math.cos(a)),(s*.325+.077*math.sin(a),.74,.03+.073*math.cos(a))],.0028,'Sage',arm,4)
    ellipsoid(label+' hand',(s*.318,.643,.037),(.053,.069,.046),'Skin',arm,12,6,True)
    ellipsoid(label+' thumb',(s*.278,.661,.066),(.022,.033,.023),'Skin',arm,8,6,True)
    for d in (-1,0,1):tube(label+' finger seam',[(s*.318+d*.018,.615,.075),(s*.318+d*.018,.636,.079)],.0018,'Rose',arm,4)
    bevelbox(label+' shoulder badge',(s*.371,1.015,.008),(.013,.07,.056),'Cream',arm,bevel=.004)
    loft(label+' thigh',[(s*.118,.739,0,.072,.078),(s*.128,.55,0,.065,.067),(s*.118,.425,0,.054,.056)],'Skin',leg,12,True)
    if MALE:
        # Each trouser leg follows its thigh; the cuff clears the knee pivot.
        loft(label+' tailored shorts leg',[(s*.103,.817,0,.080,.108),(s*.117,.706,0,.091,.102),(s*.124,.571,0,.087,.088),(s*.123,.531,0,.087,.088)],'Skirt',leg,12)
        loft(label+' shorts turned cuff',[(s*.123,.529,0,.091,.092),(s*.124,.558,0,.090,.092)],'Skirt',leg,12)
        tube(label+' shorts outer seam',[(s*.203,.693,0),(s*.209,.568,0)],.0023,'Sole',leg,4)
    loft(label+' calf',[(s*.118,.432,0,.054,.056),(s*.118,.358,-.004,.059,.062),(s*.118,.186,0,.047,.05)],'Skin',knee,12,True)
    loft(label+' boot shaft',[(s*.118,.122,.007,.079,.091),(s*.118,.21,0,.074,.081),(s*.118,.335,0,.075,.073),(s*.118,.362,0,.084,.081)],'Boot',knee,12)
    loft(label+' boot collar',[(s*.118,.351,0,.086,.083),(s*.118,.374,0,.084,.081)],'Sole',knee,12)
    # Elongated round toe, separate hard-wearing outsole.
    loft(label+' outsole',[(s*.118,.012,.058,.083,.139),(s*.118,.027,.058,.092,.148),(s*.118,.067,.058,.092,.148),(s*.118,.078,.058,.088,.141)],'Sole',knee,12)
    loft(label+' boot toe',[(s*.118,.068,.058,.087,.14),(s*.118,.118,.064,.086,.133),(s*.118,.155,.055,.075,.114),(s*.118,.169,.025,.060,.075)],'Boot',knee,12)
    bevelbox(label+' tongue',(s*.118,.226,.078),(.073,.172,.022),'Boot',knee,bevel=.01)
    for j in range(4):
        y=.179+j*.042;z=.095 if j<2 else .084
        for t in (-1,1):
            ellipsoid(label+' eyelet',(s*.118+t*.039,y,z),(.009,.01,.007),'Brass',knee,6,4)
        tube(label+' crossed lace',[(s*.118-.038,y,z+.006),(s*.118+.035,y+.027,z+.006)],.005,'Cream',knee,5)
        tube(label+' crossed lace',[(s*.118+.038,y,z+.007),(s*.118-.035,y+.027,z+.007)],.005,'Cream',knee,5)
    tube(label+' boot back seam',[(s*.118,.086,-.071),(s*.118,.335,-.078)],.004,'Sole',knee,5)
    tube(label+' pull tab',[(s*.118-.016,.342,-.068),(s*.118-.016,.396,-.075),(s*.118+.016,.396,-.075),(s*.118+.016,.342,-.068)],.008,'Boot',knee,5)

if MALE:
    exec(compile((HERE/'build_male_hair.py').read_text(encoding='utf-8'), str(HERE/'build_male_hair.py'), 'exec'))
else:
    # Large locks are sculpted with changing elliptical cross sections.
    # The back curtain sits behind the coat and thighs with breathing room for gait.
    for i in range(7):
        t=(i-3)/3; x=t*.204
        loft('Long back lock %02d'%i,[
            (x*.86,1.568,-.087-.048*(1-t*t),.028,.029),
            (x,1.515,-.14-.059*(1-t*t),.052,.06),
            (x*1.08,1.405,-.17-.059*(1-t*t),.055,.067),
            (x*1.20,1.13,-.185-.088*(1-t*t),.063,.075),
            (x*1.44,.863,-.21-.074*(1-t*t),.078,.082),
            (x*1.28,.639+abs(t)*.038,-.205-.068*(1-t*t),.067,.070),
            (x*1.12,.57+abs(t)*.065,-.17-.063*(1-t*t),.006,.018)],
            'Hair' if i%3 else 'HairHighlight','Hair',8,True)
    for s in (-1,1):
        loft('Outer flying lock',[(s*.21,1.58,-.05,.042,.037),(s*.253,1.3,-.098,.046,.044),(s*.343,.98,-.132,.042,.042),(s*.394,.8,-.13,.03,.034),(s*.369,.66,-.10,.006,.012)],'Hair','Hair',8,True)
        loft('Face framing lock',[(s*.19,1.579,.085,.022,.025),(s*.211,1.505,.129,.047,.043),(s*.214,1.39,.131,.048,.045),(s*.217,1.17,.135,.045,.039),(s*.224,1.005,.148,.046,.038),(s*.212,.921,.18,.037,.03),(s*.175,.900,.18,.004,.008)],'Hair','Head',10,True)
        loft(('Left' if s<0 else 'Right')+' ear covering lock',[
            (s*.227,1.552,-.014,.030,.036),
            (s*.243,1.48,-.012,.042,.061),
            (s*.258,1.399,-.010,.045,.069),
            (s*.271,1.295,-.024,.048,.073),
            (s*.283,1.175,-.044,.052,.074),
            (s*.285,1.071,-.064,.047,.064),
            (s*.273,.991,-.082,.029,.038),
            (s*.252,.96,-.095,.005,.01)],'Hair','Head',10,True)
        # Closed broad underlayer overlaps both the front lock and rear curtain.
        # It hides skin/background slits as the camera travels around either side.
        loft(('Left' if s<0 else 'Right')+' continuous side hair underlayer',[
            (s*.211,1.586,.002,.028,.082),
            (s*.232,1.505,.005,.043,.126),
            (s*.245,1.410,-.002,.050,.140),
            (s*.245,1.290,-.013,.055,.151),
            (s*.241,1.160,-.028,.060,.158),
            (s*.238,1.030,-.044,.053,.182),
            (s*.225,.936,-.040,.037,.201),
            (s*.220,.900,-.060,.012,.110)],'Hair','Head',8,True)
for i in range(5):
    x=(i-2)*.077; bottom=1.497+abs(i-2)*.007
    loft('Heavy fringe lock %02d'%i,[(x*.75,1.605,.108,.023,.02),(x,1.528,.18,.046,.032),(x*1.08,bottom+.015,.216-abs(x)*.18,.045,.029),(x*1.05,bottom,.207-abs(x)*.2,.021,.014)],'Hair' if i!=1 else 'HairHighlight','Head',10,True)

if not MALE:
    # Bow behind cap: two pinched fabric loops, knot, and hanging ribbons.
    for s in (-1,1):
        mesh('Back bow loop',[(s*.018,1.435,-.259),(s*.108,1.499,-.249),(s*.124,1.451,-.278),(s*.1,1.386,-.253),(s*.024,1.422,-.275),(s*.083,1.437,-.30)],[(0,1,5),(1,2,5),(2,3,5),(3,4,5),(4,0,5),(4,3,2,1,0)],'Cream','Hair')
        loft('Bow ribbon',[(s*.032,1.435,-.274,.018,.008),(s*.048,1.346,-.285,.03,.009),(s*.070,1.29,-.285,.031,.01),(s*.055,1.25,-.267,.018,.008)],'Cream','Hair',6)
    ellipsoid('Bow knot',(0,1.432,-.277),(.029,.029,.022),'Cream','Hair',8,6)
    
# Six-panel baseball cap with a real curved bill and restrained leaf embroidery.
caplevels=[]
for i in range(7):
    a=math.pi*.5*i/6
    caplevels.append((0,1.557+.23*math.sin(a),-.015,.267*max(.001,math.cos(a)),.231*max(.001,math.cos(a))))
loft('Six panel cap crown',caplevels,'Cream','Head',24,True)
rim=[(.267*math.sin(math.tau*i/32),1.557,-.015+.231*math.cos(math.tau*i/32)) for i in range(33)]
tube('Cap lower binding',rim,.009,'Sole','Head')
for k in range(6):
    a=k*math.tau/6
    points=[]
    for j in range(7):
        t=math.pi*.5*j/6;points.append((.268*math.cos(t)*math.sin(a),1.559+.232*math.sin(t),-.015+.232*math.cos(t)*math.cos(a)))
    tube('Cap panel seam',points,.0028,'Sole','Head',5)
ellipsoid('Cap top button',(0,1.791,-.015),(.024,.012,.024),'Cream','Head',10,6)
verts=[];segs=18
for row in range(4):
    f=row/3
    for i in range(segs+1):
        a=-1.18+2.36*i/segs
        x=math.sin(a)*(.237+.036*f)
        z=.01+math.cos(a)*.211 + f*.172*math.cos(a)
        y=1.566-.037*f-.036*(abs(math.sin(a))**2)
        verts.append((x,y,z))
faces=[]
for j in range(3):
    for i in range(segs):
        a=j*(segs+1)+i;faces.append((a,a+1,a+segs+2,a+segs+1))
bill=mesh('Curved baseball bill',verts,faces,'SageRib','Head',True)
bpy.context.view_layer.objects.active=bill;mod=bill.modifiers.new('Bill thickness','SOLIDIFY');mod.thickness=.014;bpy.ops.object.modifier_apply(modifier=mod.name)
tube('Bill bound edge',verts[-(segs+1):],.006,'Sage','Head',6)
for offset in (.042,.075):
    points=[(x,y+.007,z-offset*math.cos(-1.18+2.36*i/segs)) for i,(x,y,z) in enumerate(verts[-(segs+1):])]
    tube('Bill topstitch',points,.0018,'Sage','Head',4)
# Small round patch follows the front cap slope.
ellipsoid('Cap badge',(0,1.672,.186),(.052,.055,.008),'SageRib','Head',12,8)
tube('Badge stem',[(-.019,1.646,.205),(.016,1.690,.199)],.004,'Cream','Head',5)
for s,y in [(-1,1.67),(1,1.68)]:
    mesh('Badge leaf',[(0,y,.207),(s*.023,y+.003,.207),(s*.018,y+.019,.201)],[(0,1,2),(2,1,0)],'Cream','Head')

# Store editable parts under joint empties while preserving world matrices.
root=bpy.data.objects.new(ASSET_NAME,None);bpy.context.collection.objects.link(root)
joint_objs={}
for name,(parent,pos) in JOINTS.items():
    ob=bpy.data.objects.new(name,None);bpy.context.collection.objects.link(ob);ob.location=B(pos)
    ob.empty_display_size=.035;ob.empty_display_type='SPHERE';joint_objs[name]=ob
bpy.context.view_layer.update()
for name,(parent,pos) in JOINTS.items():
    ob=joint_objs[name];matrix=ob.matrix_world.copy();ob.parent=joint_objs[parent] if parent else root;ob.matrix_world=matrix
for ob in parts:
    matrix=ob.matrix_world.copy();ob.parent=joint_objs[ob['joint']];ob.matrix_world=matrix
bpy.context.view_layer.update()

# Native payload: every vertex has the evaluated Blender corner normal + UV.
payload={'version':1,'name':ASSET_NAME,'materials':[{'name':k,'color':v} for k,v in COLORS.items()],'joints':[], 'parts':[]}
for name,(parent,pos) in JOINTS.items():
    p=Vector(pos)-(Vector(JOINTS[parent][1]) if parent else Vector((0,0,0)))
    payload['joints'].append({'name':name,'parent':parent,'position':list(p)})
deps=bpy.context.evaluated_depsgraph_get();total=0
for ob in parts:
    obj=ob.evaluated_get(deps);data=obj.to_mesh();data.calc_loop_triangles()
    pivot=Vector(JOINTS[ob['joint']][1]);verts=[];normals=[];uv=[];idx=[]
    for tri in data.loop_triangles:
        for loop in tri.loops:
            vertex=data.vertices[data.loops[loop].vertex_index]
            v=Vector(U(obj.matrix_world@vertex.co))-pivot
            normal=obj.matrix_world.to_3x3()@data.corner_normals[loop].vector
            verts.extend(round(c,6) for c in v);normals.extend(U(normal.normalized()))
            uv.extend((round(v.x+.5,6),round(v.y,6)));idx.append(len(idx))
    # U is a proper rotation (determinant +1), not a reflection. Keep winding.
    payload['parts'].append({'name':ob.name,'joint':ob['joint'],'material':ob.data.materials[0].name,'vertices':verts,'normals':normals,'uv':uv,'triangles':idx})
    total+=len(idx)//3;obj.to_mesh_clear()
(OUT/(ASSET_NAME+'.meshdata.json')).write_text(json.dumps(payload,separators=(',',':')),encoding='utf-8')
print('CHARACTER TRIANGLES',total, 'PARTS',len(parts))

bpy.ops.object.select_all(action='DESELECT')
for ob in [root,*joint_objs.values(),*parts]:ob.select_set(True)
bpy.context.view_layer.objects.active=root
bpy.ops.export_scene.fbx(filepath=str(HERE/(ASSET_NAME+'.fbx')),use_selection=True,object_types={'EMPTY','MESH'},add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',use_mesh_modifiers=True)
bpy.ops.export_scene.gltf(filepath=str(HERE/(ASSET_NAME+'.glb')),use_selection=True,export_format='GLB')

# Studio for inspectable four-angle renders. Never exported as model geometry.
studio=bpy.data.collections.new('STUDIO - preview only');bpy.context.scene.collection.children.link(studio)
def to_studio(ob):
    for c in list(ob.users_collection):c.objects.unlink(ob)
    studio.objects.link(ob)
bpy.ops.mesh.primitive_plane_add(size=200);ground=bpy.context.object;ground.name='Studio floor';to_studio(ground)
floor=bpy.data.materials.new('Studio warm slate');floor.diffuse_color=(.14,.17,.16,1);ground.data.materials.append(floor)
ground.location.z=-.002
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=40
scene.cycles.use_denoising=True;scene.world.color=(.23,.23,.23)
def area(name,loc,power,size,color):
    bpy.ops.object.light_add(type='AREA',location=B(loc));ob=bpy.context.object;ob.name=name;to_studio(ob)
    ob.data.energy=power;ob.data.shape='DISK';ob.data.size=size;ob.data.color=color
    ob.rotation_euler=(B((0,.9,0))-ob.location).to_track_quat('-Z','Y').to_euler()
area('Large soft key',(-3,4,4),350,4,(1,.88,.72))
area('Cool fill',(3,2,2),210,3,(.77,.88,1))
area('Hair rim',(0,3,-3),400,2.5,(1,.88,.68))
bpy.ops.object.camera_add();cam=bpy.context.object;cam.name='Character portrait';to_studio(cam);scene.camera=cam
cam.data.type='ORTHO';cam.data.ortho_scale=2.14;cam.data.lens=70
scene.render.resolution_x=850;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
scene.view_settings.view_transform='AgX'
views=[] if '--no-render' in sys.argv else [('Hero',(2.6,1.95,5)),('Front',(0,1.28,5)),('Back',(0,1.4,-5)),('Side',(5,1.4,0))]
for name,loc in views:
    cam.location=B(loc);cam.rotation_euler=(B((0,.91,0))-cam.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=str(HERE/(ASSET_NAME+'_'+name+'.png'));bpy.ops.render.render(write_still=True)
if '--no-render' not in sys.argv:
    cam.data.ortho_scale=.78
    for name,loc in [('Face',(0,1.46,5)),('FaceThreeQuarter',(2.5,1.55,5)),('FaceOblique',(4,1.43,2.8))]:
        cam.location=B(loc);cam.rotation_euler=(B((0,1.43,0))-cam.location).to_track_quat('-Z','Y').to_euler()
        scene.render.filepath=str(HERE/(ASSET_NAME+'_'+name+'.png'));bpy.ops.render.render(write_still=True)
    # Diagnostic profile shows the mouth/chin curve without the long side locks.
    hidden=[]
    face_terms=('Face -','ear','Neck','pupil','iris','eye crescent','lid','lash','catchlight','eyebrow','nose','mouth','cheek blush')
    for ob in parts:
        if not any(term in ob.name for term in face_terms):
            hidden.append(ob);ob.hide_render=True
    cam.location=B((5,1.43,0));cam.rotation_euler=(B((0,1.43,0))-cam.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=str(HERE/(ASSET_NAME+'_ProfileStudy.png'));bpy.ops.render.render(write_still=True)
    for ob in hidden:ob.hide_render=False
cam.data.ortho_scale=2.14
cam.location=B((2.6,1.95,5));cam.rotation_euler=(B((0,.91,0))-cam.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/(ASSET_NAME+'.blend')))
(HERE/('model_stats_boy.json' if MALE else 'model_stats.json')).write_text(json.dumps({'triangles':total,'parts':len(parts),'joints':len(JOINTS),'materials':len(COLORS),'heightMetres':1.803,'source':'Matching boy companion of the approved cute girl' if MALE else 'Four user supplied reference views','rig':'Rigid joint hierarchy; not Humanoid skinned'},indent=2),encoding='utf-8')
