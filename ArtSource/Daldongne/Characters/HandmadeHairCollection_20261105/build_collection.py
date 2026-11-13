"""Editable Blender hairstyles fitted to the existing female player.

Reuses the reviewed bob's fitted crown, head attachment, HairFlow shader and player.
New surface paths are specific to long / gathered / half-up hair; no game code.
Run in Blender with -- --build, --preview or --final (optionally --style NAME).
For the current scoped revision use -- --refine --refined-final.
For the later ponytail ear/nape adjustment use -- --pony-ear-fit.
For fuller ear wisps on that saved result use -- --pony-wisp-density --pony-wisp-final.
For restrained secondary motion use -- --pony-soft-rig --pony-soft-demo --pony-soft-render.
For the new variants use -- --variants-build, --variants-render,
--variants-walk and --variants-validate (optionally --variant NAME).
"""
import bpy, bmesh, math, json, sys, hashlib, struct, random
from pathlib import Path
from mathutils import Vector, Matrix, Quaternion
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree
from mathutils.geometry import barycentric_transform

P = Path(__file__).resolve().parent
SOURCE = P.parent / 'HandmadeBob_20261103/LayeredBob_FitReview.blend'
STYLES = ('LongLayers', 'Ponytail', 'HalfUp')
TITLES = {'LongLayers': 'LONG / soft layered waves', 'Ponytail': 'PONYTAIL / swept gathering', 'HalfUp': 'HALF UP / soft gathered layers',
          'Bob':'BOB / original fitted silhouette','BobHalfUp':'SHORT HALF UP / bob with a small gathered tail',
          'LongNoBangs':'LONG / open forehead','BobNoBangs':'BOB / open forehead','TwinTails':'TWIN TAILS / mirrored gathered hair'}

def smooth(a, b, x):
    t = max(0., min(1., (x-a)/(b-a)))
    return t*t*(3-2*t)

def sample(points, t):
    """Clamped Catmull-Rom spline, including widths and cross-section depths."""
    f = min(.999999, max(0., t)) * (len(points)-1)
    j = int(f); u = f-j
    p0, p1, p2, p3 = [points[max(0, min(len(points)-1, k))] for k in (j-1,j,j+1,j+2)]
    return [ .5*(2*b+(-a+c)*u+(2*a-5*b+4*c-d)*u*u+(-a+3*b-3*c+d)*u*u*u) for a,b,c,d in zip(p0,p1,p2,p3)]

def activate(ob):
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True); bpy.context.view_layer.objects.active = ob

def apply(ob, modifier):
    activate(ob); bpy.ops.object.modifier_apply(modifier=modifier.name)

def world_bvh(ob):
    return BVHTree.FromPolygons([ob.matrix_world @ v.co for v in ob.data.vertices], [p.vertices[:] for p in ob.data.polygons])

def mesh(name, verts, faces, uvs, material, collection):
    me = bpy.data.meshes.new(name+' topology'); me.from_pydata(verts, [], faces); me.update()
    ob = bpy.data.objects.new(name, me); collection.objects.link(ob)
    uv = me.uv_layers.new(name='HairFlow')
    for face in me.polygons:
        face.use_smooth = True
        for li in face.loop_indices: uv.data[li].uv = uvs[me.loops[li].vertex_index]
    bm=bmesh.new(); bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=0.0000001)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bmesh.ops.triangulate(bm, faces=list(bm.faces), quad_method='BEAUTY', ngon_method='BEAUTY')
    bm.to_mesh(me); bm.free(); me.materials.append(material)
    return ob

def mirror_pair(ob, collection):
    other=ob.copy(); other.data=ob.data.copy(); collection.objects.link(other)
    other.name=ob.name.replace(' R ', ' L ')
    for v in other.data.vertices: v.co.x *= -1
    bm=bmesh.new(); bm.from_mesh(other.data); bmesh.ops.reverse_faces(bm, faces=list(bm.faces)); bm.to_mesh(other.data); bm.free()
    return other

def leaf(name, points, width_axis, material, collection, rows=24, cross=8, uv_start=.12, projection=None, v_start=.12, uv_sampler=None):
    """Thin closed flowing hair layer: broad lens section, never a tube/cone."""
    verts=[]; uvs=[]; faces=[]; stride=cross+1; sheet=(rows+1)*stride
    for side in (1,-1):
        for i in range(rows+1):
            t=i/rows; p=sample(points,t); center=Vector(p[:3]); width=max(.00035,p[3]); depth=max(.0002,p[4])
            tangent=Vector(sample(points,min(1,t+.002))[:3])-Vector(sample(points,max(0,t-.002))[:3])
            tangent.normalize(); axis=Vector(width_axis); axis=(axis-tangent*axis.dot(tangent)).normalized()
            normal=axis.cross(tangent).normalized()
            # Surface orientation follows the supplied outward normal hint.
            hint=Vector((center.x,center.y-.026,0))
            if normal.dot(hint)<0: normal=-normal
            for j in range(cross+1):
                q=2*j/cross-1
                relief=(depth*(1-q*q)**.8 if side==1 else -.0010*(1-q*q))
                relief += (0.00065*math.cos(3*math.pi*q)*math.sin(math.pi*t)**.5 if side==1 else 0)
                v=center+axis*(width*q)+normal*relief
                if projection:
                    hit,n,idx,d=projection.find_nearest(v)
                    if hit is not None and (v-hit).dot(n)<.0013:
                        v=hit+n*.0013
                verts.append(tuple(v)); uvs.append(uv_sampler(v) if uv_sampler else (uv_start+(q+1)*.022, v_start+(1-v_start)*t))
    for side in range(2):
        off=side*sheet
        for i in range(rows):
            for j in range(cross):
                a=off+i*stride+j; faces.append((a,a+1,a+1+stride,a+stride))
    for i in range(rows):
        for j in (0,cross):
            a=i*stride+j; b=(i+1)*stride+j; faces.append((a,b,b+sheet,a+sheet))
    for i in (0,rows):
        for j in range(cross):
            a=i*stride+j; faces.append((a,a+sheet,a+sheet+1,a+1))
    return mesh(name,verts,faces,uvs,material,collection)

def deform_core(v, style):
    x,y,z=v; a=math.atan2(x,.026-y); aa=abs(a)
    front=1-smooth(.68,1.02,aa); back=max(0,-math.cos(a))**2; side=math.sin(a)**2
    lower=smooth(0,.215,1.65-z)*(1-front)
    if style in ('LongLayers','HalfUp'):
        z-=(.365+.105*back)*lower
        t=max(0,min(1,(1.63-z)/.66))
        x*=1+(.05*math.sin(t*math.tau)-.15*t*t)*(1-front)
        # Below the ears, bring the side curtain behind the shoulders.
        y+=.205*side*smooth(0,.31,1.49-z)*(1-front)
        y+=.021*math.sin(t*math.tau+.4)*lower
        x+=math.sin(a)*.014*math.sin(t*math.tau)*lower
        if style=='HalfUp': x*=1-.035*lower
    else:
        z+=.135*side*lower
        x*=1-.125*lower
        y=.026+(y-.026)*(1-.10*lower)
    # Small geometric flow ridges follow the longitudinal hair mass.
    flow=a+.035*math.sin((1.8-z)*9)*math.sin(2*a)
    relief=(.00125*math.cos(flow*46)+.00045*math.cos(flow*92))*smooth(0,.13,1.80-z)
    x+=math.sin(a)*relief; y-=math.cos(a)*relief
    return Vector((x,y,z))

def make_core(style, collection):
    ob=bpy.data.objects['SoftBob_Hair']
    world=[ob.matrix_world @ v.co for v in ob.data.vertices]
    ob.parent=None; ob.matrix_world=Matrix.Identity(4)
    for v,p in zip(ob.data.vertices,world):v.co=p
    ob.name=style+' | continuous fitted foundation'
    for c in list(ob.users_collection):c.objects.unlink(ob)
    collection.objects.link(ob)
    sub=ob.modifiers.new('Flow sampling', 'SUBSURF'); sub.subdivision_type='SIMPLE'; sub.levels=1; apply(ob,sub)
    for v in ob.data.vertices:v.co=deform_core(v.co,style)
    # Preserve outer silhouette; spend the remaining budget on actual layered surfaces.
    ratio={'LongLayers':.78,'Ponytail':.53,'HalfUp':.66}[style]
    mod=ob.modifiers.new('Foundation budget','DECIMATE'); mod.ratio=ratio; mod.use_collapse_triangulate=True; apply(ob,mod)
    mod=ob.modifiers.new('Matched sides','MIRROR'); mod.use_bisect_axis=(True,False,False); mod.merge_threshold=.000001; apply(ob,mod)
    return ob

def add_long_layers(style, core, material, collection):
    bv=world_bvh(core)
    # A small number of broad overlapping flow surfaces, with individually tapered hems.
    for k,a in enumerate((1.13,1.62,2.08,2.54,2.96)):
        pts=[]; top=1.785-.012*k; bottom=1.08-.024*k
        for i in range(9):
            t=i/8; z=top+(bottom-top)*t
            angle=a+.055*math.sin(t*math.tau)*math.sin(a)
            outward=Vector((math.sin(angle),-math.cos(angle),0))
            origin=Vector((outward.x*.65,.026+outward.y*.65,z))
            hit,n,idx,dist=bv.ray_cast(origin,-outward,1.3)
            if hit is None:
                hit=Vector((.17*math.sin(angle),.20-.07*math.cos(angle),z));n=outward
            lift=.001+.007*math.sin(math.pi*t)**2+.009*smooth(.76,1,t)
            center=hit+n*lift
            width=(.004+.031*math.sin(math.pi*min(1,t/.97))**.65)*(1-.88*smooth(.82,1,t))
            pts.append((*center,width,.0032+.001*math.sin(math.pi*t)))
        ob=leaf(style+' R flowing layer '+str(k+1),pts,(math.cos(a),math.sin(a),0),material,collection,rows=26,cross=8,uv_start=.12+.083*k)
        mirror_pair(ob,collection)

def add_fringe(core, material, collection, style):
    # The reviewed lowered fringe remains underneath; individual thin surfaces soften its edge.
    bv=world_bvh(core)
    for k in range(3):
        x=.026+.048*k
        pts=[]
        for i in range(6):
            t=i/5; z=1.768-(.151+.004*k)*t; xx=x*(.45+.55*t)
            hit,n,_,_=bv.ray_cast(Vector((xx,-.6,z)),Vector((0,1,0)),1.2)
            y=hit.y-.002 if hit is not None else -.187
            pts.append((xx,y,z,.003+.022*math.sin(math.pi*t)**.4,.0018))
        ob=leaf(style+' R feathered fringe '+str(k+1),pts,(1,0,0),material,collection,rows=16,cross=6,uv_start=.23+.075*k)
        mirror_pair(ob,collection)

def add_gathering(style, core, material, collection):
    bv=world_bvh(core)
    for k in range(3):
        # Surface paths sweep from temples over/behind ears into the attachment.
        pts=[(.151-.016*k,-.100+.025*k,1.729+.016*k,.003,.002),
             (.199-.014*k,-.016+.018*k,1.711+.028*k,.026,.004),
             (.190-.021*k,.105,1.698+.026*k,.031,.004),
             (.118-.016*k,.209,1.680+.017*k,.027,.004),
             (.022,.250,1.676+.008*k,.008,.002)]
        ob=leaf(style+' R swept gathering '+str(k+1),pts,(0,0,1),material,collection,rows=26,cross=8,uv_start=.28+.105*k,projection=bv)
        mirror_pair(ob,collection)
    # Face-framing wisps are flat, curved blades with very thin edges.
    pts=[(.161,-.111,1.704,.003,.002),(.202,-.117,1.625,.017,.003),(.202,-.128,1.527,.018,.003),(.182,-.131,1.449,.010,.002),(.169,-.115,1.415,.0004,.0003)]
    ob=leaf(style+' R face framing strand',pts,(1,0,0),material,collection,rows=26,cross=8,uv_start=.59)
    mirror_pair(ob,collection)

TAIL_PROFILES={
    'HalfUp':[(0,.251,1.687,.032,.028),(0,.299,1.624,.064,.046),(0,.323,1.505,.066,.045),(0,.303,1.379,.049,.033),(0,.288,1.273,.020,.014),(0,.302,1.237,.004,.004)],
    'Ponytail':[(0,.253,1.694,.037,.030),(0,.320,1.653,.067,.050),(0,.378,1.528,.100,.062),(0,.372,1.357,.095,.057),(0,.323,1.173,.072,.043),(0,.348,1.054,.026,.020),(0,.369,1.025,.004,.004)]}

def add_tail(style, material, collection):
    path=TAIL_PROFILES['HalfUp' if style=='HalfUp' else 'Ponytail']
    count=84; rows=34; verts=[];uvs=[];faces=[]
    # One continuous, fluted tail with split pointed tips, following a curved center line.
    for i in range(rows+1):
        t=i/rows
        for j in range(count+1):
            u=j/count; a=math.tau*u; local=abs(math.sin(a*6))
            tt=t*(1-.052*smooth(.80,1,t)*local)
            p=sample(path,tt); center=Vector(p[:3])
            tangent=(Vector(sample(path,min(1,tt+.003))[:3])-Vector(sample(path,max(0,tt-.003))[:3])).normalized()
            axis=Vector((1,0,0)); normal=axis.cross(tangent).normalized()
            flute=1+.050*math.cos(a*12)*(.85+.15*math.cos(t*5))+.017*math.cos(a*24)
            dx=max(.0003,p[3])*math.sin(a)*flute
            dy=max(.0003,p[4])*math.cos(a)*flute
            v=center+axis*dx+normal*dy
            verts.append(tuple(v));uvs.append((u,.22+.78*t))
    stride=count+1
    for i in range(rows):
        for j in range(count):
            a=i*stride+j;faces.append((a,a+1,a+1+stride,a+stride))
    # Close root and final ring with consistently wound fans.
    for i in (0,rows):
        ids=list(range(i*stride,i*stride+count))
        center=sum((Vector(verts[k]) for k in ids),Vector())/count
        idx=len(verts);verts.append(tuple(center));uvs.append((.5,i/rows))
        for j in range(count):faces.append((idx,ids[j],ids[(j+1)%count]))
    ob=mesh(style+' | continuous flowing tail',verts,faces,uvs,material,collection)
    # A few thin exterior locks break up the tail silhouette naturally.
    for k,angle in enumerate((.78,1.64,2.50)):
        pts=[]
        for i in range(8):
            t=i/7; p=sample(path,t*.96); c=Vector(p[:3])
            tangent=(Vector(sample(path,min(1,t*.96+.003))[:3])-Vector(sample(path,max(0,t*.96-.003))[:3])).normalized()
            axis=Vector((1,0,0)); normal=axis.cross(tangent).normalized()
            c+=axis*(p[3]*math.sin(angle))+normal*(p[4]*math.cos(angle))
            c+=Vector((math.sin(angle),.4,0))*(.001+.006*math.sin(math.pi*t)**2)
            width=(.003+.017*math.sin(math.pi*t)**.6)*(1-.85*smooth(.80,1,t))
            pts.append((*c,width,.0025))
        layer=leaf(style+' R tail veil '+str(k+1),pts,(math.cos(angle),math.sin(angle),0),material,collection,rows=23,cross=6,uv_start=.16+.16*k)
        mirror_pair(layer,collection)
    # Narrow textile binding shaped along the actual tail root (no primitive torus).
    tie=bpy.data.materials.new(style+' | cocoa textile tie');tie.diffuse_color=(.052,.026,.018,1);tie.use_nodes=True
    bs=next(n for n in tie.node_tree.nodes if n.type=='BSDF_PRINCIPLED');bs.inputs['Base Color'].default_value=(.052,.026,.018,1);bs.inputs['Roughness'].default_value=.84
    vv=[];ff=[];uu=[];n=48
    for i,t in enumerate((.032,.064,.088)):
        p=sample(path,t); c=Vector(p[:3]); tan=(Vector(sample(path,t+.003)[:3])-Vector(sample(path,t-.003)[:3])).normalized();normal=Vector((1,0,0)).cross(tan).normalized()
        for j in range(n):
            a=math.tau*j/n;v=c+Vector(((p[3]+.002)*math.sin(a),0,0))+normal*((p[4]+.002)*math.cos(a));vv.append(tuple(v));uu.append((j/n,i/2))
    for i in range(2):
        for j in range(n):ff.append((i*n+j,i*n+(j+1)%n,(i+1)*n+(j+1)%n,(i+1)*n+j))
    band=mesh(style+' | fitted textile binding',vv,ff,uu,tie,collection)
    sol=band.modifiers.new('Binding thickness','SOLIDIFY');sol.thickness=.001;apply(band,sol)
    return ob

def fit_and_attach(objects, body, rig):
    bodytree=world_bvh(body)
    # Resolve sub-millimetre fit conflicts using the actual source body surface.
    for ob in objects:
        for v in ob.data.vertices:
            hit,n,idx,d=bodytree.find_nearest(v.co)
            if hit is not None and (v.co-hit).dot(n)<.003:
                v.co=hit+n*.003
        ob.data.update()
    pivot=rig.matrix_world @ rig.data.bones['Head'].head_local
    root=bpy.data.objects.new('Attachment | FemaleRig.Head',None);bpy.context.scene.collection.objects.link(root);root.location=pivot
    root['source_bone']='FemaleRig.Head';root['source_asset']=str(SOURCE.relative_to(P.parent));root['rest_pose_fit_only']=True
    bpy.context.view_layer.update()
    for ob in objects:
        for v in ob.data.vertices:v.co-=pivot
        ob.parent=root;ob.matrix_world=Matrix.Translation(pivot)
        mod=ob.modifiers.new('Final triangles','TRIANGULATE');apply(ob,mod)
        ob['authored_surface']='Flowing fitted hair surface; closed thin layers';ob['unity_imported']=False
    return root

def inspect(style, objects, body, destination=None):
    bodytree=world_bvh(body); bpy.context.view_layer.update(); report=[]
    for ob in objects:
        bm=bmesh.new();bm.from_mesh(ob.data)
        vworld=[ob.matrix_world @ v.co for v in ob.data.vertices]
        hits=bodytree.overlap(world_bvh(ob))
        report.append({'object':ob.name,'vertices':len(bm.verts),'triangles':len(bm.faces),'boundary_edges':sum(e.is_boundary for e in bm.edges),'nonmanifold_edges':sum(not e.is_manifold for e in bm.edges),'degenerate_faces':sum(f.calc_area()<1e-12 for f in bm.faces),'body_intersection_pairs':len(hits),'non_triangles':sum(len(f.verts)!=3 for f in bm.faces)})
        if hits:print('CONTACT',ob.name,[(tuple(ob.matrix_world@ob.data.polygons[j].center)) for i,j in hits[:15]],flush=True)
        bm.free()
    vertices=[ob.matrix_world@v.co for ob in objects for v in ob.data.vertices]
    tree=KDTree(len(vertices))
    for i,v in enumerate(vertices):tree.insert(v,i)
    tree.balance()
    result={'style':style,'triangles':sum(x['triangles'] for x in report),'vertices':len(vertices),'hair_top_z':max(v.z for v in vertices),'hair_bottom_z':min(v.z for v in vertices),'head_clearance_at_crown_m':max(v.z for v in vertices)-1.800000548,'symmetry_max_error_m':max(tree.find(Vector((-v.x,v.y,v.z)))[2] for v in vertices),'body_intersection_pairs':sum(x['body_intersection_pairs'] for x in report),'nonmanifold_edges':sum(x['nonmanifold_edges'] for x in report),'boundary_edges':sum(x['boundary_edges'] for x in report),'non_triangles':sum(x['non_triangles'] for x in report),'objects':report,'unity_imported':False,'animation_validated':False,'materials':'Reused HairFlow procedural Blender material, not baked for Unity'}
    (Path(destination or P)/(style+'_validation.json')).write_text(json.dumps(result,indent=2),encoding='utf-8')
    print('QA',json.dumps({k:v for k,v in result.items() if k!='objects'}),flush=True)
    return result

def setup_stage(style):
    s=bpy.context.scene;s.render.engine='CYCLES';s.cycles.samples=24;s.cycles.use_denoising=True
    s.view_settings.view_transform='AgX';s.view_settings.look='AgX - Medium High Contrast';s.view_settings.exposure=0
    s.world.use_nodes=True;s.world.node_tree.nodes['Background'].inputs[0].default_value=(.19,.215,.26,1);s.world.node_tree.nodes['Background'].inputs[1].default_value=.65
    for old in [o for o in bpy.data.objects if o.type in ('LIGHT','CAMERA')]:bpy.data.objects.remove(old,do_unlink=True)
    target=Vector((0,.075,1.425))
    for name,loc,power,size in [('Key L',(-1.7,-2.5,3.3),110,2),('Key R',(1.7,-2.5,3.3),110,2),('Rim L',(-1.2,2.2,2.5),160,1.4),('Rim R',(1.2,2.2,2.5),160,1.4)]:
        bpy.ops.object.light_add(type='AREA',location=loc);ob=bpy.context.object;ob.name=name;ob.data.energy=power;ob.data.shape='DISK';ob.data.size=size;ob.rotation_euler=(target-ob.location).to_track_quat('-Z','Y').to_euler()
    for label,a in [('Front',0),('ThreeQuarter',.60),('Side',math.pi/2),('Back',math.pi),('BackThreeQuarter',2.52)]:
        bpy.ops.object.camera_add(location=target+Vector((4*math.sin(a),-4*math.cos(a),.04)));c=bpy.context.object;c.name='Review_'+label;c.rotation_euler=(target-c.location).to_track_quat('-Z','Y').to_euler();c.data.type='ORTHO';c.data.ortho_scale=.96
    s.camera=bpy.data.objects['Review_ThreeQuarter'];s.render.resolution_x=900;s.render.resolution_y=1000;s.render.resolution_percentage=100;s.render.image_settings.file_format='PNG';s.render.film_transparent=False
    for area in bpy.context.screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.shading.type='MATERIAL';area.spaces.active.overlay.show_overlays=False;area.spaces.active.region_3d.view_perspective='CAMERA'
    s['hairstyle']=style;s['title']=TITLES.get(style,'Three hairstyle collection');s['source_player']='FemaleArticulated.blend';s['status']='Blender-only rest-pose asset'

def build(style):
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
    hairworld=bpy.data.objects['SoftBob_Hair'].matrix_world.copy()
    for old in [o for o in bpy.data.objects if o.type=='EMPTY']:bpy.data.objects.remove(old,do_unlink=True)
    bpy.data.objects['SoftBob_Hair'].matrix_world=hairworld
    collection=bpy.data.collections.new(style+' | editable hair layers');bpy.context.scene.collection.children.link(collection)
    body=bpy.data.objects['FemaleBody'];rig=bpy.data.objects['FemaleRig']
    core=make_core(style,collection); material=core.data.materials[0]
    bs=material.node_tree.nodes.get('Principled BSDF');bs.inputs['Roughness'].default_value=.59;bs.inputs['Specular IOR Level'].default_value=.23
    if style in ('LongLayers','HalfUp'):add_long_layers(style,core,material,collection)
    add_fringe(core,material,collection,style)
    if style!='LongLayers':
        add_gathering(style,core,material,collection);add_tail(style,material,collection)
    objects=[o for o in collection.objects if o.type=='MESH'];fit_and_attach(objects,body,rig)
    setup_stage(style); report=inspect(style,objects,body)
    activate(core);bpy.ops.wm.save_as_mainfile(filepath=str(P/(style+'_FitReview.blend')))
    for ob in (body,rig):bpy.data.objects.remove(ob,do_unlink=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(P/(style+'_Editable.blend')))
    return report

def render(style, final=False, angles=None, output_prefix=''):
    bpy.ops.wm.open_mainfile(filepath=str(P/(style+'_FitReview.blend')))
    s=bpy.context.scene
    if not final:s.cycles.samples=12;s.render.resolution_percentage=65
    for angle in (angles or (('Front','ThreeQuarter','Side','Back','BackThreeQuarter') if final else ('ThreeQuarter','BackThreeQuarter'))):
        s.camera=bpy.data.objects['Review_'+angle];s.render.filepath=str(P/(output_prefix+style+'_'+angle+'.png'));bpy.ops.render.render(write_still=True)
    print('RENDER_COMPLETE',style,flush=True)

def geometry_signature(ob,include_transform=True):
    h=hashlib.sha256()
    for v in ob.data.vertices:h.update(struct.pack('<3f',*v.co))
    for p in ob.data.polygons:
        h.update(struct.pack('<I',len(p.vertices)))
        h.update(struct.pack('<'+'I'*len(p.vertices),*p.vertices))
    for layer in ob.data.uv_layers:
        for loop in layer.data:h.update(struct.pack('<2f',*loop.uv))
    if include_transform:
        for row in ob.matrix_world:h.update(struct.pack('<4f',*row))
    return h.hexdigest()

def surface_reader(ob):
    tree=world_bvh(ob);coords=[ob.matrix_world@v.co for v in ob.data.vertices]
    uv=ob.data.uv_layers.active
    def read_uv(point,index):
        face=ob.data.polygons[index];li=list(face.loop_indices)[:3]
        xyz=[coords[ob.data.loops[j].vertex_index] for j in li]
        tex=[Vector((*uv.data[j].uv,0)) for j in li]
        r=barycentric_transform(point,*xyz,*tex)
        return (r.x,r.y)
    def nearest_uv(point):
        p,n,i,d=tree.find_nearest(point)
        return read_uv(p,i)
    return tree,read_uv,nearest_uv

def enlarged_fringe(style,core,material,collection):
    """Six existing fringe sections enlarged onto the actual forehead boundary."""
    tree,read_uv,unused=surface_reader(core)
    def front_surface(x,z):
        p,n,i,d=tree.ray_cast(Vector((x,-.6,z)),Vector((0,1,0)),1.2)
        if p is not None and p.y<-.055 and n.y<-.10:
            return p.y,read_uv(p,i)
        # The strand extends a few millimetres below the foundation's hem.
        # Continue its front curve rather than hitting the opposite back surface.
        samples=[]
        for zz in (1.635,1.660):
            h,hn,hi,hd=tree.ray_cast(Vector((x,-.6,zz)),Vector((0,1,0)),1.2)
            if h is not None and h.y<-.055 and hn.y<-.10:samples.append((zz,h.y,read_uv(h,hi)))
        if len(samples)==2:
            slope=max(-.6,min(.6,(samples[1][1]-samples[0][1])/.025))
            yy=samples[0][1]+slope*(z-samples[0][0])
            uu=samples[0][2][0];vv=min(1.,samples[0][2][1]+(samples[0][0]-z)/.22)
            return yy,(uu,vv)
        return -.199+.060*(x/.18)**2,(.5+math.asin(max(-.99,min(.99,x/.235)))/math.tau,1.)
    result=[];rows=28;cross=8;stride=cross+1;sheet=(rows+1)*stride
    for k,end_x in enumerate((.029,.087,.145)):
        vv=[];ff=[];uvs=[]
        for shell in (0,1):
            for i in range(rows+1):
                t=i/rows;z=1.790+(1.614-.002*k-1.790)*t
                center_x=end_x*(.43+.57*smooth(0,1,t))
                width=.006+.028*smooth(0,.23,t)-.003*smooth(.80,1,t)
                for j in range(cross+1):
                    q=2*j/cross-1
                    x=center_x+width*q
                    zz=z+.0015*q*q*smooth(.84,1,t)
                    y,tex=front_surface(x,zz)
                    offset=-.0010+.0033*smooth(.05,.40,t)
                    bulge=.0007*(1-q*q)*math.sin(math.pi*t)
                    y-=offset+bulge
                    if shell:y+=.0009
                    vv.append((x,y,zz));uvs.append(tex)
        for shell in (0,1):
            off=shell*sheet
            for i in range(rows):
                for j in range(cross):
                    a=off+i*stride+j;ff.append((a,a+1,a+stride+1,a+stride))
        for i in range(rows):
            for j in (0,cross):
                a=i*stride+j;b=a+stride;ff.append((a,b,b+sheet,a+sheet))
        for i in (0,rows):
            for j in range(cross):
                a=i*stride+j;ff.append((a,a+sheet,a+sheet+1,a+1))
        ob=mesh(style+' R feathered fringe '+str(k+1),vv,ff,uvs,material,collection)
        result.extend((ob,mirror_pair(ob,collection)))
    return result

def side_profile(z):
    # Uniform 10cm height stations: one gentle backward fall past the shoulders.
    stations=[(.215,.209,.026),(.235,.210,.028),(.232,.180,.070),
              (.227,.130,.145),(.218,.085,.200),(.210,.065,.205),
              (.200,.055,.200),(.190,.050,.190),(.180,.045,.190)]
    return sample(stations,(1.70-z)/.80)

def relax_side_foundation(core):
    # UV flow labels identify the original left/right side region even where the
    # previous nearest-body projection bent a strand across the neck.
    uv=core.data.uv_layers.active;labels=[[] for v in core.data.vertices]
    for loop,tex in zip(core.data.loops,uv.data):labels[loop.vertex_index].append(tex.uv.copy())
    rng=random.Random(717);weights=[rng.uniform(.83,1.18) for i in range(14)];positive=[0.]
    for w in weights:positive.append(positive[-1]+math.pi*w/sum(weights))
    bounds=[-a for a in reversed(positive[1:])]+positive
    matrix=core.matrix_world.copy();inverse=matrix.inverted();changed=0;unchanged=[];max_move=0.
    for v,values in zip(core.data.vertices,labels):
        co=matrix@v.co
        if not values or co.z>=1.69:unchanged.append((v.index,tuple(v.co)));continue
        u=sum(p.x for p in values)/len(values);t=sum(p.y for p in values)/len(values)
        k=min(27,max(0,int(u*28)));q=max(0,min(1,u*28-k))
        a=abs(bounds[k]*(1-q)+bounds[k+1]*q)
        weight=smooth(.78,1.12,a)*(1-smooth(2.08,2.60,a))*smooth(0,.14,1.69-co.z)
        if weight<.000001:unchanged.append((v.index,tuple(v.co)));continue
        qa=.5+(q-.5)*(1-.72*smooth(.94,1,t))
        a=abs(bounds[k]*(1-qa)+bounds[k+1]*qa)
        rx,ry,cy=side_profile(co.z);radial=Vector((co.x,co.y-.026,0))
        thickness=.0012 if v.normal.dot(radial)<0 else 0.
        ridge=.0007*math.cos(a*46)
        sign=1 if co.x>=0 else -1
        target=Vector((sign*(rx+ridge-thickness)*math.sin(a),cy-(ry+ridge-thickness)*math.cos(a),co.z))
        moved=co.lerp(target,weight)
        if moved.z<1.43:
            moved.y+=max(0,.145-moved.y)*smooth(0,.09,1.43-moved.z)
        max_move=max(max_move,(moved-co).length)
        v.co=inverse@moved;changed+=1
    core.data.update();bpy.context.view_layer.update()
    assert all(tuple(core.data.vertices[i].co)==co for i,co in unchanged)
    return {'changed_side_vertices':changed,'unchanged_vertices':len(unchanged),'max_side_movement_m':max_move,'crown_and_front_foundation_unchanged':True}

def relaxed_side_layers(style,core,material,collection):
    tree,read_uv,nearest_uv=surface_reader(core);result=[]
    for k,a in enumerate((1.13,1.62,2.08)):
        pts=[];top=1.785-.012*k;bottom=1.08-.024*k;outward=Vector((math.sin(a),-math.cos(a),0))
        for i in range(17):
            t=i/16;z=top+(bottom-top)*t;rx,ry,cy=side_profile(z)
            origin=Vector((outward.x*.65,cy+outward.y*.65,z))
            hit,n,idx,d=tree.ray_cast(origin,-outward,1.3)
            if hit is None or hit.x<0 or d>.65:
                hit=Vector((rx*math.sin(a),cy-ry*math.cos(a),z));n=outward
            ideal=Vector((rx*math.sin(a),cy-ry*math.cos(a),z))
            hit=hit.lerp(ideal,smooth(0,.13,1.55-z))
            lift=-.001+.002*math.sin(math.pi*t)**2+.004*smooth(.84,1,t)
            center=hit+n*lift
            width=(.003+.027*math.sin(math.pi*t)**.65)*(1-.90*smooth(.82,1,t))
            pts.append((*center,width,.0004+.0025*math.sin(math.pi*t)))
        ob=leaf(style+' R flowing layer '+str(k+1),pts,(math.cos(a),math.sin(a),0),material,collection,rows=26,cross=8,uv_start=.12+.083*k,uv_sampler=nearest_uv)
        result.extend((ob,mirror_pair(ob,collection)))
    return result

def refine_requested_regions(style):
    baseline=P/'_History'/'BeforeFringeSide_20261105'/(style+'_FitReview.blend')
    bpy.ops.wm.open_mainfile(filepath=str(baseline));bpy.context.view_layer.update()
    core=bpy.data.objects[style+' | continuous fitted foundation']
    collection=core.users_collection[0];material=core.data.materials[0]
    body=bpy.data.objects['FemaleBody'];rig=bpy.data.objects['FemaleRig']
    root=core.parent;pivot=root.location.copy()
    all_meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
    before={o.name:geometry_signature(o) for o in all_meshes}
    target_names={o.name for o in all_meshes if 'feathered fringe' in o.name}
    if style!='Ponytail':
        target_names.add(core.name)
        target_names.update(o.name for o in all_meshes if 'flowing layer' in o.name and o.name.rsplit(' ',1)[-1] in ('1','2','3'))
    for name in sorted(target_names):
        if name!=core.name:bpy.data.objects.remove(bpy.data.objects[name],do_unlink=True)
    side_result=relax_side_foundation(core) if style!='Ponytail' else {'side_hair_unchanged':True}
    new_objects=enlarged_fringe(style,core,material,collection)
    if style!='Ponytail':new_objects+=relaxed_side_layers(style,core,material,collection)
    for ob in new_objects:
        for v in ob.data.vertices:v.co-=pivot
        ob.parent=root;ob.matrix_world=Matrix.Translation(pivot)
        ob['revision']='Fringe fit and relaxed side flow only, 2026-11-05'
    bpy.context.view_layer.update()
    preserved={name:before[name]==geometry_signature(bpy.data.objects[name]) for name in before if name not in target_names}
    assert all(preserved.values()), 'Unrequested object changed'
    tree=world_bvh(body);updated=[]
    for ob in new_objects+([core] if style!='Ponytail' else []):
        bm=bmesh.new();bm.from_mesh(ob.data)
        updated.append({'object':ob.name,'triangles':sum(len(f.verts)-2 for f in bm.faces),'body_intersections':len(tree.overlap(world_bvh(ob))),'boundary_edges':sum(e.is_boundary for e in bm.edges),'nonmanifold_edges':sum(not e.is_manifold for e in bm.edges)})
        bm.free()
    report={'style':style,'baseline':str(baseline),'preserved_objects':preserved,'scope':side_result,'updated_objects':updated,'unity_imported':False}
    assert all(x['body_intersections']==0 and x['boundary_edges']==0 and x['nonmanifold_edges']==0 for x in updated if x['object']!=core.name), 'New strands must be closed and clear of the body'
    (P/(style+'_refine_validation.json')).write_text(json.dumps(report,indent=2),encoding='utf-8')
    inspect(style,[o for o in collection.objects if o.type=='MESH'],body)
    activate(core);bpy.ops.wm.save_as_mainfile(filepath=str(P/(style+'_FitReview.blend')))
    for ob in (body,rig):bpy.data.objects.remove(ob,do_unlink=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(P/(style+'_Editable.blend')))
    print('SCOPED_REFINEMENT',json.dumps(report),flush=True)

def pony_ear_fit():
    """Fill the existing ponytail nape, bring sides in, replace the two wide wisps."""
    style='Ponytail';baseline=P/'_History'/'BeforePonyEarFit_20261105'/'Ponytail_FitReview.blend'
    bpy.ops.wm.open_mainfile(filepath=str(baseline));bpy.context.view_layer.update()
    core=bpy.data.objects[style+' | continuous fitted foundation'];collection=core.users_collection[0]
    body=bpy.data.objects['FemaleBody'];rig=bpy.data.objects['FemaleRig'];root=core.parent;pivot=root.location.copy()
    bodytree=world_bvh(body);material=core.data.materials[0]
    before={o.name:geometry_signature(o) for o in bpy.context.scene.objects if o.type=='MESH'}
    targets={core.name}
    targets.update(o.name for o in collection.objects if 'swept gathering' in o.name or 'face framing strand' in o.name)
    before_contacts={name:set(bodytree.overlap(world_bvh(bpy.data.objects[name]))) for name in targets if 'face framing strand' not in name}
    old_wisps={o.name:{'min_z':min((o.matrix_world@v.co).z for v in o.data.vertices),'max_z':max((o.matrix_world@v.co).z for v in o.data.vertices),'max_abs_x':max(abs((o.matrix_world@v.co).x) for v in o.data.vertices)} for o in collection.objects if 'face framing strand' in o.name}
    # Correspondence to the original cap's flow labels; the upper crown/fringe
    # are outside the editable side and nape interval.
    labels=[[] for v in core.data.vertices]
    for loop,uv in zip(core.data.loops,core.data.uv_layers.active.data):labels[loop.vertex_index].append(uv.uv.copy())
    rng=random.Random(717);weights=[rng.uniform(.83,1.18) for _ in range(14)];positive=[0.]
    for w in weights:positive.append(positive[-1]+math.pi*w/sum(weights))
    bounds=[-a for a in reversed(positive[1:])]+positive
    hem=[(.75,1.610),(1.05,1.530),(1.30,1.578),(1.55,1.590),(1.75,1.588),(1.95,1.470),(2.20,1.425),(2.60,1.420),(math.pi,1.425)]
    def desired_hem(a):
        if a<=hem[0][0]:return hem[0][1]
        for (a0,z0),(a1,z1) in zip(hem,hem[1:]):
            if a<=a1:return z0+(z1-z0)*smooth(a0,a1,a)
        return hem[-1][1]
    def original_hem(a,q):
        front=1-smooth(.68,1.02,a);back=max(0,-math.cos(a))**2
        fringe=1.621-.003*smooth(.25,.68,a)+.0006*math.cos(26*a)
        for cut in (.14,.31,.49,.62):fringe+=.0018*math.exp(-((a-cut)/.014)**2)
        notch=math.exp(-(q/.10)**2)+math.exp(-((1-q)/.10)**2)
        end=fringe*front+(1.437+.018*back+.003*math.cos(7*a)+.008*notch)*(1-front)
        return end+.135*math.sin(a)**2*smooth(0,.215,1.65-end)*(1-front)
    def envelope_radius(direction,z):
        center=Vector((0,.026,z));radius=0.
        for sign in (1,-1):
            d=Vector((sign*abs(direction.x),direction.y,0))
            hit,n,idx,dist=bodytree.ray_cast(center+d*.5,-d,1.)
            if hit is not None:radius=max(radius,(hit-center).length)
        return radius
    def close_to_head(co,normal,extra=0.):
        a=abs(math.atan2(co.x,.026-co.y));sign=1 if co.x>=0 else -1
        angular=smooth(.76,1.04,a);height=1-smooth(1.56,1.735,co.z)
        side=math.sin(a)**2
        strength=angular*height*(.90*side+.85*(1-side)*(1-smooth(1.44,1.62,co.z)))
        if strength<.000001:return co
        front_turn=.035*strength*(1-smooth(1.20,1.70,a))
        aa=a-front_turn;direction=Vector((sign*math.sin(aa),-math.cos(aa),0))
        center=Vector((0,.026,co.z));r=(co-center).length;skin=envelope_radius(direction,co.z)
        if skin<=0:return co
        outer=normal.dot(co-center)>=0
        clearance=(.011 if outer else .0095)+extra
        fullness=.008*(1-smooth(1.40,1.49,co.z))
        wanted=skin+clearance+fullness
        radius=max(skin+clearance,r*(1-strength)+wanted*strength)
        return center+direction*radius
    matrix=core.matrix_world.copy();inverse=matrix.inverted();unchanged=[];changed=[];max_lower=0.;max_inward=0.
    for v,uvs in zip(core.data.vertices,labels):
        co=matrix@v.co
        if not uvs or co.z>=1.735:unchanged.append((v.index,tuple(v.co)));continue
        u=sum(p.x for p in uvs)/len(uvs);t=sum(p.y for p in uvs)/len(uvs)
        k=min(27,max(0,int(u*28)));q=max(0,min(1,u*28-k));a=abs(bounds[k]*(1-q)+bounds[k+1]*q)
        angular=smooth(.75,1.02,a)
        if angular<.000001:unchanged.append((v.index,tuple(v.co)));continue
        moved=co.copy()
        moved.z+=(desired_hem(a)-original_hem(a,q))*smooth(.52,1,t)*angular
        tip_blend=smooth(.92,1,t)*angular
        fine_tip=.0024*(math.exp(-(q/.12)**2)+math.exp(-((1-q)/.12)**2))-.0012
        moved.z=moved.z*(1-tip_blend)+(desired_hem(a)+fine_tip)*tip_blend
        moved=close_to_head(moved,v.normal)
        max_lower=max(max_lower,co.z-moved.z);max_inward=max(max_inward,abs(co.x)-abs(moved.x))
        v.co=inverse@moved;changed.append(v.index)
    core.data.update();bpy.context.view_layer.update()
    assert all(tuple(core.data.vertices[i].co)==co for i,co in unchanged)
    # Keep the existing gathering layers attached while the underlying sides move.
    for ob in collection.objects:
        if 'swept gathering' not in ob.name:continue
        mat=ob.matrix_world.copy();inv=mat.inverted()
        for v in ob.data.vertices:
            co=mat@v.co
            if co.z<1.735:v.co=inv@close_to_head(co,v.normal,extra=.003)
        ob.data.update()
    for name in list(targets):
        if 'face framing strand' in name:bpy.data.objects.remove(bpy.data.objects[name],do_unlink=True)
    bpy.context.view_layer.update();cap_tree,read_uv,nearest_uv=surface_reader(core)
    primary=[(.183,-.030,1.595,.0010,.0005),(.182,-.042,1.558,.0040,.00065),(.176,-.047,1.518,.0035,.0006),(.160,-.043,1.478,.0018,.0004),(.153,-.031,1.458,.0003,.0002)]
    secondary=[(.188,-.012,1.580,.0007,.0004),(.190,-.025,1.548,.0020,.0005),(.183,-.034,1.514,.0015,.0004),(.174,-.025,1.486,.0003,.0002)]
    new=[]
    for j,points in enumerate((primary,secondary),1):
        # Root starts on the cap near the front rim of the ear, not on the crown.
        for i in (0,):
            point=Vector(points[i][:3]);hit,n,idx,d=cap_tree.find_nearest(point)
            if hit is not None and d<.05:points[i]=(*(hit+n*.0006),*points[i][3:])
        ob=leaf(style+' R ear wisp '+str(j),points,(1,0,0),material,collection,rows=24,cross=6,uv_start=.61+.035*j,v_start=.68)
        new.extend((ob,mirror_pair(ob,collection)))
    for ob in new:
        for v in ob.data.vertices:v.co-=pivot
        ob.parent=root;ob.matrix_world=Matrix.Translation(pivot)
        ob['revision']='Thin ear-front wisps; lower side and nape coverage, 2026-11-05'
    bpy.context.view_layer.update()
    preserved={name:geometry_signature(bpy.data.objects[name])==value for name,value in before.items() if name not in targets}
    assert all(preserved.values()),'Unrequested ponytail mesh changed'
    updated=[]
    for ob in [core]+new+[o for o in collection.objects if 'swept gathering' in o.name]:
        bm=bmesh.new();bm.from_mesh(ob.data);points=[ob.matrix_world@v.co for v in ob.data.vertices]
        contacts=set(bodytree.overlap(world_bvh(ob)))
        added=contacts-before_contacts.get(ob.name,set())
        updated.append({'object':ob.name,'triangles':sum(len(f.verts)-2 for f in bm.faces),'body_intersections':len(contacts),'new_body_intersections':len(added),'boundary_edges':sum(e.is_boundary for e in bm.edges),'nonmanifold_edges':sum(not e.is_manifold for e in bm.edges),'min_world_z':min(p.z for p in points),'max_world_z':max(p.z for p in points),'max_abs_world_x':max(abs(p.x) for p in points)})
        bm.free()
    assert all(x['new_body_intersections']==0 for x in updated), 'The ear revision introduced body intersections'
    assert all(x['boundary_edges']==0 and x['nonmanifold_edges']==0 for x in updated if 'ear wisp' in x['object'])
    report={'style':style,'baseline':str(baseline),'preserved_objects':preserved,'changed_foundation_vertices':len(changed),'unchanged_foundation_vertices':len(unchanged),'max_lowering_m':max_lower,'max_inward_m':max_inward,'old_wisps':old_wisps,'updated_objects':updated,'unity_imported':False}
    (P/'Ponytail_ear_fit_validation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    inspect(style,[o for o in collection.objects if o.type=='MESH'],body)
    activate(core);bpy.ops.wm.save_as_mainfile(filepath=str(P/'Ponytail_FitReview.blend'))
    for ob in (body,rig):bpy.data.objects.remove(ob,do_unlink=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(P/'Ponytail_Editable.blend'))
    print('PONY_EAR_FIT',json.dumps(report),flush=True)

def pony_wisp_density():
    """Add two fine ear-front wisps per side without moving the reviewed hair."""
    style='Ponytail';baseline=P/'_History'/'BeforePonyWispDensity_20261105'/'Ponytail_FitReview.blend'
    bpy.ops.wm.open_mainfile(filepath=str(baseline));bpy.context.view_layer.update()
    core=bpy.data.objects[style+' | continuous fitted foundation'];collection=core.users_collection[0]
    body=bpy.data.objects['FemaleBody'];rig=bpy.data.objects['FemaleRig'];root=core.parent;pivot=root.location.copy()
    before={o.name:geometry_signature(o) for o in bpy.context.scene.objects if o.type=='MESH'}
    bodytree=world_bvh(body);cap_tree=world_bvh(core);material=core.data.materials[0]
    # Stagger the ends and keep the extra strands inside the existing ear-front
    # cluster, with one finer strand extending slightly toward the cheek.
    paths=[
        [(.181,-.039,1.591,.0008,.0004),(.178,-.051,1.554,.0026,.00055),(.171,-.055,1.516,.0023,.0005),(.156,-.046,1.478,.0013,.00035),(.151,-.036,1.463,.00035,.0002)],
        [(.187,-.023,1.585,.0007,.0004),(.185,-.036,1.551,.0022,.0005),(.180,-.041,1.519,.0018,.00045),(.166,-.033,1.490,.0008,.0003),(.162,-.023,1.477,.00035,.0002)],
    ]
    added=[]
    for j,points in enumerate(paths,3):
        point=Vector(points[0][:3]);hit,n,index,distance=cap_tree.find_nearest(point)
        assert hit is not None and distance<.05, 'Wisp root is outside the ear-front cap'
        points[0]=(*(hit+n*.0006),*points[0][3:])
        ob=leaf(style+' R ear wisp '+str(j),points,(1,0,0),material,collection,rows=24,cross=6,uv_start=.61+.035*j,v_start=.68)
        added.extend((ob,mirror_pair(ob,collection)))
    for ob in added:
        for v in ob.data.vertices:v.co-=pivot
        ob.parent=root;ob.matrix_world=Matrix.Translation(pivot)
        ob['revision']='Additional fine ear-front wisps, 2026-11-05'
    bpy.context.view_layer.update()
    preserved={name:geometry_signature(bpy.data.objects[name])==value for name,value in before.items()}
    assert all(preserved.values()), 'Existing reviewed geometry changed'
    checks=[]
    for ob in added:
        bm=bmesh.new();bm.from_mesh(ob.data)
        checks.append({'object':ob.name,'triangles':sum(len(f.verts)-2 for f in bm.faces),
            'body_intersections':len(bodytree.overlap(world_bvh(ob))),
            'boundary_edges':sum(e.is_boundary for e in bm.edges),
            'nonmanifold_edges':sum(not e.is_manifold for e in bm.edges),
            'degenerate_faces':sum(f.calc_area()<1e-12 for f in bm.faces)})
        bm.free()
    assert all(not any(x[k] for k in ('body_intersections','boundary_edges','nonmanifold_edges','degenerate_faces')) for x in checks), checks
    total=inspect(style,[o for o in collection.objects if o.type=='MESH'],body)
    expected={o.name:geometry_signature(o) for o in collection.objects if o.type=='MESH'}
    activate(core);bpy.ops.wm.save_as_mainfile(filepath=str(P/'Ponytail_FitReview.blend'))
    for ob in (body,rig):bpy.data.objects.remove(ob,do_unlink=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(P/'Ponytail_Editable.blend'))
    for filename in ('Ponytail_FitReview.blend','Ponytail_Editable.blend'):
        bpy.ops.wm.open_mainfile(filepath=str(P/filename));bpy.context.view_layer.update()
        assert all(geometry_signature(bpy.data.objects[name])==value for name,value in expected.items()),filename
    other_hashes=json.loads((baseline.parent/'other_styles_sha256.json').read_text(encoding='utf-8-sig'))
    assert all(hashlib.sha256((P/name).read_bytes()).hexdigest().lower()==value.lower() for name,value in other_hashes.items())
    report={'style':style,'baseline':str(baseline),'ear_wisps_per_side_before':2,'ear_wisps_per_side_after':4,
        'preserved_objects':preserved,'added_objects':checks,'triangles':total['triangles'],
        'saved_files_reopened_and_geometry_matched':True,'other_style_files_unchanged':len(other_hashes),
        'unity_imported':False,'animation_validated':False,
        'existing_unmodified_foundation_issues_remain':True}
    (P/'Ponytail_wisp_density_validation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print('PONY_WISP_DENSITY',json.dumps(report),flush=True)

def evaluated_surface(ob):
    evaluated=ob.evaluated_get(bpy.context.evaluated_depsgraph_get())
    me=evaluated.to_mesh()
    points=[evaluated.matrix_world@v.co for v in me.vertices]
    faces=[tuple(p.vertices) for p in me.polygons]
    evaluated.to_mesh_clear()
    return points,BVHTree.FromPolygons(points,faces)

def pony_soft_rig(style='Ponytail',baseline=None,destination=None):
    """Skin the reviewed hair without changing any authored vertex, UV or face."""
    baseline=Path(baseline) if baseline else P/'_History'/'BeforeSubtleMotion_20261105'/'Ponytail_FitReview.blend'
    destination=Path(destination) if destination else P
    bpy.ops.wm.open_mainfile(filepath=str(baseline));bpy.context.view_layer.update()
    core=bpy.data.objects[style+' | continuous fitted foundation'];collection=core.users_collection[0]
    source=bpy.data.objects['FemaleRig'];head=source.pose.bones['Head']
    objects=[o for o in collection.objects if o.type=='MESH']
    before={o.name:geometry_signature(o,include_transform=False) for o in bpy.context.scene.objects if o.type=='MESH'}
    rest={o.name:evaluated_surface(o)[0] for o in objects}
    data=bpy.data.armatures.new(style+' subtle motion bones')
    rig=bpy.data.objects.new(style+'_SoftMotionRig',data);collection.objects.link(rig)
    rig.show_in_front=True;data.display_type='STICK'
    activate(rig);bpy.ops.object.mode_set(mode='EDIT')
    root=data.edit_bones.new('Hair_Head');root.head=core.parent.location;root.tail=root.head+Vector((0,0,.10))
    chains=[];bindings={}
    def chain(label,points,names,kind,levels=None):
        previous=root;bone_names=[]
        for i,(a,b) in enumerate(zip(points,points[1:])):
            bone=data.edit_bones.new(label+'_'+str(i+1).zfill(2));bone.head=a;bone.tail=b
            bone.parent=previous;bone.use_connect=i>0;bone.align_roll(Vector((0,1,0)))
            previous=bone;bone_names.append(bone.name)
        chains.append({'label':label,'bones':bone_names,'objects':names,'kind':kind,'anchor':list(points[0]),'levels':levels})
        for name in names:bindings[name]=chains[-1]
    tail_names=[o.name for o in objects if 'continuous flowing tail' in o.name or 'tail veil' in o.name]
    if style=='Ponytail':
        chain('Hair_Tail',[(0,.320,1.650),(0,.378,1.530),(0,.373,1.390),(0,.338,1.240),(0,.335,1.110),(0,.369,1.023)],tail_names,'tail')
    elif style=='HalfUp':
        chain('Hair_Tail',[(0,.280,1.65),(0,.320,1.53),(0,.310,1.40),(0,.293,1.30),(0,.302,1.237)],tail_names,'tail',[1.68,1.59,1.47,1.35,1.25])
    elif style=='BobHalfUp':
        chain('Hair_Tail',[(0,.265,1.67),(0,.287,1.60),(0,.287,1.54),(0,.283,1.485)],tail_names,'tail',[1.67,1.60,1.54,1.485])
    elif style=='TwinTails':
        for side,sign in (('R',1),('L',-1)):
            names=[o.name for o in objects if o.get('motion_family')=='tail_'+side]
            points=[(sign*x,y,z) for x,y,z in [( .199,.142,1.663),(.263,.196,1.56),(.286,.229,1.40),(.276,.234,1.25),(.269,.223,1.14),(.279,.220,1.085)]]
            chain('Hair_Tail_'+side,points,names,'tail',[1.61,1.55,1.45,1.32,1.20,1.09])
    if style in ('Bob','BobHalfUp','BobNoBangs','LongNoBangs','HalfUp'):
        names=[o.name for o in objects if o==core or 'flowing layer' in o.name or 'parted sweep' in o.name]
        short=style in ('Bob','BobHalfUp','BobNoBangs')
        points=[(0,.15,1.59),(0,.18,1.53),(0,.20,1.48),(0,.20,1.43)] if short else [(0,.15,1.56),(0,.19,1.39),(0,.23,1.20),(0,.24,.98)]
        levels=[1.59,1.54,1.49,1.44] if short else [1.58,1.48,1.28,1.02]
        chain('Hair_Curtain',points,names,'curtain',levels)
    for ob in objects:
        if not any(word in ob.name for word in ('ear wisp','feathered fringe','face framing strand')):continue
        points=rest[ob.name];top=max(p.z for p in points);bottom=min(p.z for p in points)
        def center(z):
            nearest=sorted(points,key=lambda p:abs(p.z-z))[:max(6,len(points)//30)]
            return sum(nearest,Vector())/len(nearest)
        side='R' if ' R ' in ob.name else 'L';index=ob.name.rsplit(' ',1)[-1]
        if 'ear wisp' in ob.name or 'face framing strand' in ob.name:
            chain('Hair_Wisp_'+side+index,[center(top),center((top+bottom)*.5),center(bottom)],[ob.name],'wisp')
        else:
            chain('Hair_Fringe_'+side+index,[center(1.665),center(bottom)],[ob.name],'fringe')
    bpy.ops.object.mode_set(mode='OBJECT')
    constraint=rig.constraints.new('CHILD_OF');constraint.name='Follow existing FemaleRig.Head'
    constraint.target=source;constraint.subtarget='Head'
    constraint.inverse_matrix=(source.matrix_world@head.matrix).inverted()
    bpy.context.view_layer.update()
    # The armature object transform is not a skin deformation by itself.
    # Move the actual mesh parent with it, preserving the reviewed rest pose.
    attachment=core.parent;attachment_world=attachment.matrix_world.copy()
    attachment.parent=rig;attachment.matrix_parent_inverse=rig.matrix_world.inverted()
    attachment.matrix_world=attachment_world
    bpy.context.view_layer.update()
    for ob in objects:
        groups={name:ob.vertex_groups.new(name=name) for name in ['Hair_Head']+(bindings[ob.name]['bones'] if ob.name in bindings else [])}
        binding=bindings.get(ob.name)
        for v,p in zip(ob.data.vertices,rest[ob.name]):
            weights={'Hair_Head':1.}
            if binding:
                names=['Hair_Head']+binding['bones']
                if binding.get('levels'):levels=binding['levels']
                elif binding['kind']=='tail':levels=[1.670,1.610,1.490,1.350,1.200,1.055]
                elif binding['kind']=='wisp':
                    z0=max(q.z for q in rest[ob.name]);z1=min(q.z for q in rest[ob.name]);span=z0-z1
                    levels=[z0-.008,z0-.45*span,z1+.008]
                else:levels=[1.655,1.612]
                if p.z<=levels[-1]:weights={names[-1]:1.}
                else:
                    for j in range(len(levels)-1):
                        if levels[j]>=p.z>=levels[j+1]:
                            t=smooth(levels[j+1],levels[j],p.z)
                            weights={names[j]:t,names[j+1]:1-t};break
            for name,weight in weights.items():
                if weight>1e-8:groups[name].add([v.index],weight,'REPLACE')
        mod=ob.modifiers.new('Subtle hair bone deformation','ARMATURE');mod.object=rig;mod.use_deform_preserve_volume=True
    settings={'tail':{'frequency_hz':2.6,'damping_ratio':.92,'acceleration_gain':.14,'angular_drag':.045,'limit_degrees':2.2,'distribution':[.30,.27,.23,.17,.12]},
              'wisp':{'frequency_hz':3.6,'damping_ratio':.96,'acceleration_gain':.07,'angular_drag':.018,'limit_degrees':2.0,'distribution':[.60,.55]},
              'fringe':{'frequency_hz':4.5,'damping_ratio':1.0,'acceleration_gain':.025,'angular_drag':.008,'limit_degrees':.35,'distribution':[.70]}}
    rig['soft_motion_chains']=json.dumps(chains);rig['soft_motion_settings']=json.dumps(settings)
    if style!='Ponytail':
        settings['curtain']={'frequency_hz':3.2,'damping_ratio':.97,'acceleration_gain':.06,'angular_drag':.015,'limit_degrees':.8,'distribution':[.40,.35,.25]}
        if style=='HalfUp':
            settings['tail']['distribution']=[.36,.31,.23,.15]
            settings['wisp'].update(acceleration_gain=.035,angular_drag=.009,limit_degrees=.9)
        if style=='BobHalfUp':settings['tail']['distribution']=[.45,.35,.25]
        rig['soft_motion_settings']=json.dumps(settings)
    rig['hairstyle']=style
    rig['motion_notes']='Low-amplitude inertial spring bake; fixed roots, no idle wind. Re-bake after changing source animation.'
    rig['unity_runtime_connected']=False
    rig['source_bone']='Head';rig['source_rig']='FemaleRig'
    core.parent['rest_pose_fit_only']=False
    core.parent['deformation_binding']='Mesh parent AND '+style+'_SoftMotionRig follow FemaleRig.Head together'
    for pb in rig.pose.bones:pb.rotation_mode='QUATERNION'
    bpy.context.view_layer.update()
    max_error=max((a-b).length for ob in objects for a,b in zip(rest[ob.name],evaluated_surface(ob)[0]))
    preserved={name:geometry_signature(bpy.data.objects[name],include_transform=False)==value for name,value in before.items()}
    weight_error=max(abs(sum(g.weight for g in v.groups)-1) for ob in objects for v in ob.data.vertices)
    assert all(preserved.values()) and max_error<.00001 and weight_error<1e-6,(max_error,weight_error)
    bpy.context.scene['hair_motion_status']='Rigged. Neutral rest pose. See '+('Ponytail_SubtleMotion_Demo.blend' if style=='Ponytail' else style+'_Walk.blend')+' for baked secondary motion.'
    activate(core);bpy.ops.wm.save_as_mainfile(filepath=str(destination/(style+'_FitReview.blend')))
    rigid_world=rig.matrix_world.copy();rig.constraints.remove(constraint);rig.matrix_world=rigid_world
    for ob in (bpy.data.objects['FemaleBody'],source):bpy.data.objects.remove(ob,do_unlink=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(destination/(style+'_Editable.blend')))
    report={'baseline':str(baseline),'mesh_geometry_preserved':preserved,'bones':len(data.bones),
            'rest_deformation_max_error_m':max_error,'weight_sum_max_error':weight_error,'settings':settings,
            'unity_imported':False,'runtime_game_physics_connected':False}
    (destination/(style+'_soft_motion_rig_validation.json')).write_text(json.dumps(report,indent=2),encoding='utf-8')
    print('SOFT_RIG_READY',json.dumps({k:v for k,v in report.items() if k!='mesh_geometry_preserved'}),flush=True)

def bake_soft_hair(start,end,style='Ponytail'):
    """Solve inertia from the actual animated Head transform and bake bounded springs.

    The saved clip needs no Python handler. This can be rerun on another source
    animation; there is no idle sine/wind forcing and no game-side implementation.
    """
    s=bpy.context.scene;rig=bpy.data.objects[style+'_SoftMotionRig'];body=bpy.data.objects.get('FemaleBody')
    chains=json.loads(rig['soft_motion_chains']);settings=json.loads(rig['soft_motion_settings'])
    rig.animation_data_clear();dt=s.render.fps_base/s.render.fps
    states={c['label']:{'angle':Vector(),'velocity':Vector(),'position':None,'previous_velocity':Vector(),'filtered_acceleration':Vector()} for c in chains}
    moving=[bpy.data.objects[name] for c in chains for name in c['objects']]
    matrices={pb.name:pb.bone.matrix_local.to_quaternion().inverted() for pb in rig.pose.bones}
    report={'start_frame':start,'end_frame':end,'fps':s.render.fps,'samples':[], 'collision_limited_frames':[],
            'max_displacement_m':{'tail':0.,'wisp':0.,'fringe':0.},'max_root_displacement_m':0.,'new_body_intersections':0}
    if any(c['kind']=='curtain' for c in chains):report['max_displacement_m']['curtain']=0.
    previous_rotation=None
    source=bpy.data.objects.get('FemaleRig');core=bpy.data.objects[style+' | continuous fitted foundation']
    reference_head=None;reference_cap=None
    report['max_head_attachment_error_m']=0.
    for frame in range(start,end+1):
        s.frame_set(frame)
        for pb in rig.pose.bones:pb.rotation_quaternion=Quaternion()
        bpy.context.view_layer.update()
        if source:
            head_world=source.matrix_world@source.pose.bones['Head'].matrix
            cap_points=evaluated_surface(core)[0]
            if reference_head is None:reference_head=head_world.copy();reference_cap=cap_points
            head_delta=head_world@reference_head.inverted()
            error=max((point-head_delta@reference).length for point,reference in zip(cap_points,reference_cap))
            report['max_head_attachment_error_m']=max(report['max_head_attachment_error_m'],error)
            assert error<.00001,('Hair mesh detached from Head',frame,error)
        transform=rig.matrix_world.copy();rotation=transform.to_quaternion().normalized()
        if previous_rotation is None:angular=Vector()
        else:
            delta=rotation@previous_rotation.inverted()
            if delta.w<0:delta.negate()
            angular=delta.axis*delta.angle/dt
        previous_rotation=rotation
        rigid={ob.name:evaluated_surface(ob) for ob in moving}
        bodytree=evaluated_surface(body)[1] if body else None
        baseline_contacts={name:set(bodytree.overlap(surface[1])) if bodytree else set() for name,surface in rigid.items()}
        poses={}
        for chain in chains:
            config=settings[chain['kind']];state=states[chain['label']]
            point=transform@Vector(chain['anchor'])
            velocity=(point-state['position'])/dt if state['position'] is not None else Vector()
            acceleration=(velocity-state['previous_velocity'])/dt if state['position'] is not None else Vector()
            state['position']=point;state['previous_velocity']=velocity
            state['filtered_acceleration']=state['filtered_acceleration'].lerp(acceleration,.45)
            a=rotation.inverted()@state['filtered_acceleration'];w=rotation.inverted()@angular
            target=Vector((-a.y,a.x,0))*config['acceleration_gain']-w*config['angular_drag']
            limit=math.radians(config['limit_degrees'])
            if target.length>limit:target*=limit/target.length
            omega=math.tau*config['frequency_hz'];h=dt/8
            for unused in range(8):
                state['velocity']+=((target-state['angle'])*omega*omega-state['velocity']*(2*config['damping_ratio']*omega))*h
                state['angle']+=state['velocity']*h
            if state['angle'].length>limit:state['angle']*=limit/state['angle'].length
            for name,factor in zip(chain['bones'],config['distribution']):poses[name]=matrices[name]@(state['angle']*factor)
        # Compare against rigid hair at this exact player pose, so pre-existing
        # skin contacts are not misreported as new dynamic penetrations.
        evaluated={}
        for name,angle in poses.items():
            rig.pose.bones[name].rotation_quaternion=Quaternion(angle.normalized(),angle.length) if angle.length>1e-9 else Quaternion()
        bpy.context.view_layer.update()
        # Resolve each independent chain separately. A tiny fringe contact must
        # never make the entire ponytail snap back to its rigid pose.
        for chain in chains:
            accepted_scale=1.;added_contacts=0
            for scale in (1.,.5,.25,0.):
                if scale!=1.:
                    for name in chain['bones']:
                        angle=poses[name]
                        rig.pose.bones[name].rotation_quaternion=Quaternion(angle.normalized(),angle.length*scale) if angle.length>1e-9 else Quaternion()
                    bpy.context.view_layer.update()
                added_contacts=0
                for name in chain['objects']:
                    points,tree=evaluated_surface(bpy.data.objects[name]);evaluated[name]=points
                    if bodytree:added_contacts+=len(set(bodytree.overlap(tree))-baseline_contacts[name])
                if added_contacts==0:accepted_scale=scale;break
            assert added_contacts==0,(frame,chain['label'],added_contacts)
            if accepted_scale<1.:report['collision_limited_frames'].append({'frame':frame,'chain':chain['label'],'scale':accepted_scale})
        maxima={kind:0. for kind in report['max_displacement_m']}
        for chain in chains:
            for name in chain['objects']:
                ob=bpy.data.objects[name];root_index=ob.vertex_groups['Hair_Head'].index
                for v,a,b in zip(ob.data.vertices,rigid[name][0],evaluated[name]):
                    distance=(a-b).length;maxima[chain['kind']]=max(maxima[chain['kind']],distance)
                    if any(g.group==root_index and g.weight>.999999 for g in v.groups):report['max_root_displacement_m']=max(report['max_root_displacement_m'],distance)
        for kind,value in maxima.items():report['max_displacement_m'][kind]=max(report['max_displacement_m'][kind],value)
        report['samples'].append({'frame':frame,'max_displacement_m':maxima})
        for name in poses:rig.pose.bones[name].keyframe_insert(data_path='rotation_quaternion',frame=frame,group=name)
        if (frame-start)%24==0:print('SOFT_BAKE_FRAME',frame,json.dumps(maxima),flush=True)
    rig.animation_data.action.name=style+' | subtle inertia baked from Head'
    rig.animation_data.action.use_fake_user=True
    report['final_displacement_m']=report['samples'][-1]['max_displacement_m']
    assert report['max_root_displacement_m']<.00001
    assert report['max_displacement_m']['tail']<.035 and report['max_displacement_m']['wisp']<.008
    return report

def apply_project_walk(source):
    """Reuse the project's FemaleWalk FBX, keeping its bone poses and cadence."""
    s=bpy.context.scene;source.animation_data_clear();existing=set(bpy.data.objects)
    path=P.parent/'MeshyArticulated_20261028/FemaleWalk.fbx'
    bpy.ops.import_scene.fbx(filepath=str(path))
    imported=[o for o in bpy.data.objects if o not in existing]
    walking=next(o for o in imported if o.type=='ARMATURE');action=walking.animation_data.action
    first,last=map(int,action.frame_range);fps=s.render.fps
    names=[b.name for b in walking.pose.bones if b.name in source.pose.bones]
    rest_error=max(abs(walking.data.bones[name].matrix_local[i][j]-source.data.bones[name].matrix_local[i][j]) for name in names for i in range(4) for j in range(4))
    assert rest_error<.001,('Walk skeleton does not match player',rest_error)
    samples=[]
    for frame in range(first,last+1):
        s.frame_set(frame);bpy.context.view_layer.update()
        samples.append({'world':walking.matrix_world.copy(),'hip':walking.matrix_world@walking.pose.bones['Hips'].head,
                        'poses':{name:walking.pose.bones[name].matrix_basis.copy() for name in names}})
    for ob in imported:bpy.data.objects.remove(ob,do_unlink=True)
    source.rotation_mode='QUATERNION'
    for name in names:source.pose.bones[name].rotation_mode='QUATERNION'
    period=last-first;hold=round(fps*.4);cycles=3;end=fps*6;stop=hold+cycles*period+1
    drift=samples[-1]['hip']-samples[0]['hip'];drift.z=0
    for frame in range(1,end+1):
        index=0 if frame<=hold else ((frame-hold-1)%period if frame<stop else period)
        sample_pose=samples[index];world=sample_pose['world'].copy();world.translation-=drift*(index/period)
        source.matrix_world=world
        for channel in ('location','rotation_quaternion','scale'):source.keyframe_insert(data_path=channel,frame=frame,group='Project Walk source')
        for name,basis in sample_pose['poses'].items():
            bone=source.pose.bones[name];bone.matrix_basis=basis
            for channel in ('location','rotation_quaternion','scale'):bone.keyframe_insert(data_path=channel,frame=frame,group=name)
    source.animation_data.action.name='FemaleWalk | original clip poses, in-place review'
    s.frame_start=1;s.frame_end=end;s.render.fps=fps
    return {'source_clip':str(path),'source_sha256':hashlib.sha256(path.read_bytes()).hexdigest(),
            'source_frames':[first,last],'source_fps':fps,'matched_bones':len(names),'rest_matrix_max_error':rest_error,
            'cycles':cycles,'horizontal_cycle_drift_removed_m':list(drift),'input_stops_seconds':(stop-1)/fps}

def pony_soft_demo():
    bpy.ops.wm.open_mainfile(filepath=str(P/'Ponytail_FitReview.blend'))
    s=bpy.context.scene;source=bpy.data.objects['FemaleRig'];walk=apply_project_walk(source)
    report=bake_soft_hair(s.frame_start,s.frame_end)
    report['walk_source']=walk;report['input_stops_seconds']=walk['input_stops_seconds'];report['preview_only']=True;report['unity_runtime_connected']=False
    report['static_initial_hold_max_m']=max(max(x['max_displacement_m'].values()) for x in report['samples'] if x['frame']<=round(s.render.fps*.4))
    assert report['static_initial_hold_max_m']<.00001
    assert max(report['final_displacement_m'].values())<.0001
    s.camera=bpy.data.objects['Review_ThreeQuarter'];s.render.resolution_x=640;s.render.resolution_y=880;s.render.resolution_percentage=100;s.cycles.samples=12
    target=Vector((0,.065,.94));s.camera.location=target+Vector((4*math.sin(.55),-4*math.cos(.55),.08));s.camera.rotation_euler=(target-s.camera.location).to_track_quat('-Z','Y').to_euler();s.camera.data.ortho_scale=2.12
    s['hair_motion_status']='Head attachment fixed and verified on project FemaleWalk. Mesh parent and hair rig follow the same Head transform.'
    s['motion_review_slug']='Ponytail_WalkAttached'
    # Keep the generator available for an explicit re-bake, without enabling
    # automatic script execution or changing Blender security preferences.
    text=bpy.data.texts.get('HairMotion_tools.py') or bpy.data.texts.new('HairMotion_tools.py')
    text.clear();text.write(Path(__file__).read_text(encoding='utf-8').replace("P = Path(__file__).resolve().parent","P = Path(bpy.data.filepath).resolve().parent").replace("if __name__=='__main__':","if False:  # Explicit functions only inside this .blend"))
    s.frame_set(1);bpy.ops.wm.save_as_mainfile(filepath=str(P/'Ponytail_SubtleMotion_Demo.blend'))
    bpy.ops.wm.save_as_mainfile(filepath=str(P/'Ponytail_WalkAttached_Demo.blend'))
    (P/'Ponytail_soft_motion_validation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    (P/'Ponytail_WalkAttached_validation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print('SOFT_MOTION_READY',json.dumps({k:v for k,v in report.items() if k!='samples'}),flush=True)

def render_soft_motion(encode_only=False):
    bpy.ops.wm.open_mainfile(filepath=str(P/'Ponytail_SubtleMotion_Demo.blend'))
    s=bpy.context.scene;slug=s.get('motion_review_slug','Ponytail_SubtleMotion');frames=P/(slug+'_Frames');frames.mkdir(exist_ok=True)
    s.render.engine='BLENDER_EEVEE';s.eevee.taa_render_samples=128
    text=bpy.data.texts.get('HairMotion_tools.py')
    if text:
        text.clear();text.write(Path(__file__).read_text(encoding='utf-8').replace("P = Path(__file__).resolve().parent","P = Path(bpy.data.filepath).resolve().parent").replace("if __name__=='__main__':","if False:  # Explicit functions only inside this .blend"))
    bpy.ops.wm.save_as_mainfile(filepath=str(P/'Ponytail_SubtleMotion_Demo.blend'))
    if slug!='Ponytail_SubtleMotion':bpy.ops.wm.save_as_mainfile(filepath=str(P/(slug+'_Demo.blend')))
    print('MOTION_RENDER_ENGINE',s.render.engine,flush=True)
    s.render.image_settings.media_type='IMAGE';s.render.image_settings.file_format='PNG';s.render.filepath=str(frames/'frame_')
    if not encode_only:
        bpy.ops.render.render(animation=True)
        print('SOFT_MOTION_FRAMES_COMPLETE',flush=True)
    # Encode the actual Blender frames through Blender's bundled FFmpeg.
    s.sequence_editor_create();strips=s.sequence_editor.strips
    paths=sorted(frames.glob('frame_*.png'));assert len(paths)==s.frame_end-s.frame_start+1,len(paths)
    strip=strips.new_image('Subtle hair preview',str(paths[0]),channel=1,frame_start=1)
    for path in paths[1:]:strip.elements.append(path.name)
    strip.frame_final_duration=len(paths);s.render.use_sequencer=True
    s.view_settings.view_transform='Standard';s.view_settings.look='None';s.view_settings.exposure=0;s.view_settings.gamma=1
    s.render.image_settings.media_type='VIDEO';s.render.image_settings.file_format='FFMPEG';s.render.ffmpeg.format='MPEG4';s.render.ffmpeg.codec='H264';s.render.ffmpeg.constant_rate_factor='HIGH'
    s.render.filepath=str(P/(slug+'.mp4'));bpy.ops.render.render(animation=True)
    print('SOFT_MOTION_VIDEO_COMPLETE',flush=True)

VARIANT_STYLES=('Bob','BobHalfUp','LongNoBangs','BobNoBangs','TwinTails')
VARIANT_DIR=P/'FiveStyles_20261105'

def loft_open_forehead(core,style,material,collection):
    """Resample the reviewed bob silhouette into a clean, continuous open hairline."""
    # Read the existing fitted bob as the surface guide, including its sculpted
    # crown. The source file and source object remain unmodified.
    guide=None
    if style=='BobNoBangs':tree=world_bvh(core)
    else:
        with bpy.data.libraries.load(str(SOURCE),link=False) as (available,loaded):
            loaded.objects=['SoftBob_Hair']
        guide=loaded.objects[0]
        pivot=bpy.data.objects['FemaleRig'].matrix_world@bpy.data.objects['FemaleRig'].data.bones['Head'].head_local
        tree=BVHTree.FromPolygons([v.co+pivot for v in guide.data.vertices],[tuple(p.vertices) for p in guide.data.polygons])
    vertices=[];uvs=[];faces=[];columns=224;rows=50
    for row in range(1,rows+1):
        t=row/rows
        for col in range(columns):
            a=-math.pi+math.tau*col/columns;aa=abs(a)
            fringe=1-smooth(.45,1.10,aa);back=max(0,-math.cos(a))**2
            hem=(1.708+.004*math.sin(3*aa))*fringe+(1.437+.018*back+.003*math.cos(a*14)**8)*(1-fringe)
            z=1.813+(hem-1.813)*t
            direction=Vector((math.sin(aa),-math.cos(a),0));center=Vector((0,.026,max(z,1.51)))
            hit=None
            for lift in (0,.003,.008,.015):
                origin=center+direction*.45+Vector((0,0,lift))
                hit,normal,index,distance=tree.ray_cast(origin,-direction,.45)
                if hit is not None:break
            if hit is None:
                radius=.205*math.sqrt(max(0,(1.813-z)/.12))
                hit=center+direction*min(.235,radius)
            if z<1.51:
                radial=hit-Vector((0,.026,hit.z))
                hit=Vector((0,.026,z))+radial*(.88+.12*smooth(1.425,1.51,z))
            hit.z=z
            # Taper the last row of each lock while retaining one continuous rim.
            tip=smooth(.92,1,t)*(1-fringe)
            hit-=direction*(.002*tip)
            if a<0:hit.x*=-1
            if style=='LongNoBangs':
                hit=deform_core(hit,'LongLayers')
                hit.y+=.15*math.sin(a)**2*smooth(1.57,1.34,hit.z)
                hit.y+=max(0.,.155-hit.y)*smooth(1.58,1.39,hit.z)
                # Route the side curtain around the ear before it moves behind
                # the shoulders, rather than passing straight through the ear.
                if abs(hit.x)>.10:
                    ear_width=.255*math.sqrt(max(0.,1-((hit.y-.026)/.215)**2))
                    blend=smooth(1.68,1.60,hit.z)*smooth(1.37,1.46,hit.z)
                    hit.x=math.copysign(abs(hit.x)+max(0.,ear_width-abs(hit.x))*blend,hit.x)
            vertices.append(tuple(hit));uvs.append((col/columns,t))
    for row in range(rows-1):
        for col in range(columns):
            a=row*columns+col;b=row*columns+(col+1)%columns
            faces.append((a,b,b+columns,a+columns))
    pole=len(vertices);vertices.append((0,.018,1.813));uvs.append((.5,0))
    for col in range(columns):faces.append((pole,(col+1)%columns,col))
    result=mesh(style+' open forehead surface',vertices,faces,uvs,material,collection)
    mod=result.modifiers.new('Fine inward edge thickness','SOLIDIFY');mod.thickness=.0015;mod.offset=-1;mod.use_even_offset=False;apply(result,mod)
    mod=result.modifiers.new('Final triangles','TRIANGULATE');apply(result,mod)
    old=core.data;core.data=result.data
    bpy.data.objects.remove(result,do_unlink=True)
    if guide:bpy.data.objects.remove(guide,do_unlink=True)
    if old.users==0:bpy.data.meshes.remove(old)
    return core

def parted_sweeps(style,core,material,collection):
    """Flow the existing continuous surface away from its central part."""
    uv=core.data.uv_layers.active
    for loop,tex in zip(core.data.loops,uv.data):
        co=core.data.vertices[loop.vertex_index].co
        weight=smooth(1.56,1.74,co.z)
        # Constant-u fibres sweep sideways from the part; below the temple
        # transition they retain the original longitudinal UV flow.
        swept_u=.50+(co.y-.026)*1.4+.10*(abs(co.x)/.24)**1.3
        swept_v=.04+.64*min(1,abs(co.x)/.235)
        tex.uv=(tex.uv.x*(1-weight)+swept_u*weight,tex.uv.y*(1-weight)+swept_v*weight)
    for vertex in core.data.vertices:
        co=vertex.co;weight=smooth(1.60,1.75,co.z)
        phase=(co.y-.026)*44+abs(co.x)*5
        co.z+=.00045*math.sin(phase*math.tau)*weight*(1-math.exp(-(co.x/.010)**2))
    core.data.update()

def gathered_surface(name,points,core,material,collection):
    """A single conforming outer ribbon, then real thickness; no coincident sheets."""
    tree=world_bvh(core);vertices=[];faces=[];uvs=[];rows=32;cross=8
    for row in range(rows+1):
        t=row/rows;p=sample(points,t);center=Vector(p[:3])
        for col in range(cross+1):
            q=2*col/cross-1;point=center+Vector((0,0,p[3]*q))
            hit,n,index,d=tree.find_nearest(point)
            if n.dot(hit-Vector((0,.026,1.60)))<0:n=-n
            vertices.append(tuple(hit+n*(.0026+.0010*(1-q*q))))
            uvs.append((.28+.045*(q+1),.30+.28*t))
    for row in range(rows):
        for col in range(cross):
            a=row*(cross+1)+col;faces.append((a,a+1,a+cross+2,a+cross+1))
    ob=mesh(name,vertices,faces,uvs,material,collection)
    modifier=ob.modifiers.new('Thin gathered surface','SOLIDIFY');modifier.thickness=.0007;modifier.offset=-1;apply(ob,modifier)
    mirror_pair(ob,collection)
    return ob

def make_twin_tails(core,material,collection):
    source_path=TAIL_PROFILES['Ponytail']
    target=[(.199,.142,1.663,.027,.024),(.251,.179,1.600,.048,.037),(.285,.217,1.470,.057,.043),(.287,.236,1.315,.050,.037),(.268,.230,1.175,.036,.028),(.272,.219,1.105,.010,.010),(.279,.220,1.085,.002,.002)]
    existing=set(collection.objects);add_tail('Ponytail',material,collection)
    created=[o for o in collection.objects if o not in existing]
    centers=[Vector(sample(source_path,i/600)[:3]) for i in range(601)]
    tree=KDTree(len(centers))
    for i,p in enumerate(centers):tree.insert(p,i)
    tree.balance();veil_index=0
    for ob in created:
        is_band='binding' in ob.name
        if is_band:suffix='tail band'
        elif 'continuous flowing' in ob.name:suffix='tail core'
        else:veil_index+=1;suffix='tail veil '+str(veil_index)
        ob.name='TwinTails R '+suffix
        for vertex in ob.data.vertices:
            point,index,distance=tree.find(vertex.co);t=index/600
            src=sample(source_path,t);dst=sample(target,t)
            src_tangent=(Vector(sample(source_path,min(1,t+.002))[:3])-Vector(sample(source_path,max(0,t-.002))[:3])).normalized()
            dst_tangent=(Vector(sample(target,min(1,t+.002))[:3])-Vector(sample(target,max(0,t-.002))[:3])).normalized()
            axis=Vector((1,0,0));src_normal=axis.cross(src_tangent).normalized()
            dst_axis=(axis-dst_tangent*axis.dot(dst_tangent)).normalized();dst_normal=dst_axis.cross(dst_tangent).normalized()
            offset=vertex.co-Vector(src[:3]);rx=max(.45,min(.85,dst[3]/max(.001,src[3])));ry=max(.45,min(.85,dst[4]/max(.001,src[4])))
            vertex.co=Vector(dst[:3])+dst_axis*(offset.dot(axis)*rx)+dst_normal*(offset.dot(src_normal)*ry)+dst_tangent*(offset.dot(src_tangent)*.85)
        ob.data.update();other=mirror_pair(ob,collection);other.name='TwinTails L '+suffix
        if not is_band:ob['motion_family']='tail_R';other['motion_family']='tail_L'
    for k in range(2):
        points=[(.012,.025+.05*k,1.804-.004*k,.002),(.085,.060+.04*k,1.785-.01*k,.011),(.160,.105+.025*k,1.735-.015*k,.012),(.200,.143,1.670,.003)]
        gathered_surface('TwinTails R swept gathering '+str(k+1),points,core,material,collection)

def make_bob_half_up(core,material,collection):
    """Reuse the gathered half-up construction at the existing bob length."""
    existing=set(collection.objects);add_tail('HalfUp',material,collection)
    for ob in list(collection.objects):
        if ob in existing:continue
        ob.name=ob.name.replace('HalfUp','BobHalfUp')
        for vertex in ob.data.vertices:
            vertex.co.x*=.65
            vertex.co.y=.251+(vertex.co.y-.251)*.70
            vertex.co.z=1.687+(vertex.co.z-1.687)*.45
        ob.data.update()
    points=[(.150,-.100,1.745,.002),(.211,.015,1.730,.020),(.157,.170,1.706,.021),(.025,.227,1.677,.006)]
    gathered_surface('BobHalfUp R swept gathering 1',points,core,material,collection)

def build_variant(style):
    VARIANT_DIR.mkdir(parents=True,exist_ok=True)
    source_name={'Bob':'Bob','BobHalfUp':'Bob','BobNoBangs':'Bob','LongNoBangs':'LongLayers','HalfUp':'HalfUp','TwinTails':'Ponytail'}[style]
    source_path=SOURCE if source_name=='Bob' else P/(source_name+'_FitReview.blend')
    bpy.ops.wm.open_mainfile(filepath=str(source_path));bpy.context.view_layer.update()
    body=bpy.data.objects['FemaleBody'];source=bpy.data.objects['FemaleRig'];body_signature=geometry_signature(body)
    objects=[o for o in bpy.context.scene.objects if o.type=='MESH' and (o.name=='SoftBob_Hair' or o.name.startswith(source_name))]
    collection=bpy.data.collections.new(style+' | editable hair');bpy.context.scene.collection.children.link(collection)
    core=None
    for ob in objects:
        world=[ob.matrix_world@v.co for v in ob.data.vertices]
        ob.parent=None;ob.matrix_world=Matrix.Identity(4)
        for modifier in list(ob.modifiers):ob.modifiers.remove(modifier)
        for vertex,co in zip(ob.data.vertices,world):vertex.co=co
        for group in list(ob.vertex_groups):ob.vertex_groups.remove(group)
        for c in list(ob.users_collection):c.objects.unlink(ob)
        collection.objects.link(ob)
        if ob.name=='SoftBob_Hair' or 'continuous fitted foundation' in ob.name:
            ob.name=style+' | continuous fitted foundation';core=ob
        else:ob.name=ob.name.replace(source_name,style,1)
    for ob in list(bpy.data.objects):
        if ob.type=='EMPTY' or (ob.type=='ARMATURE' and ob!=source):bpy.data.objects.remove(ob,do_unlink=True)
    assert core is not None
    if source_name=='Bob':
        modifier=core.modifiers.new('Smooth deformation sampling','SUBSURF');modifier.subdivision_type='SIMPLE';modifier.levels=1;apply(core,modifier)
        modifier=core.modifiers.new('Triangles','TRIANGULATE');apply(core,modifier)
    material=core.data.materials[0]
    if style in ('LongNoBangs','BobNoBangs'):
        for ob in list(collection.objects):
            if 'feathered fringe' in ob.name or (style=='LongNoBangs' and ob!=core):
                bpy.data.objects.remove(ob,do_unlink=True)
        loft_open_forehead(core,style,material,collection);parted_sweeps(style,core,material,collection)
    if style=='TwinTails':
        for ob in list(collection.objects):
            if any(word in ob.name for word in ('flowing tail','tail veil','textile binding','swept gathering')):bpy.data.objects.remove(ob,do_unlink=True)
        make_twin_tails(core,material,collection)
    if style=='BobHalfUp':make_bob_half_up(core,material,collection)
    for ob in collection.objects:
        if ob.type=='MESH' and any(len(p.vertices)!=3 for p in ob.data.polygons):
            modifier=ob.modifiers.new('Final triangles','TRIANGULATE');apply(ob,modifier)
    pivot=source.matrix_world@source.data.bones['Head'].head_local
    attachment=bpy.data.objects.new(style+' attachment | Head',None);collection.objects.link(attachment);attachment.location=pivot
    bpy.context.view_layer.update()
    for ob in collection.objects:
        if ob.type!='MESH':continue
        for vertex in ob.data.vertices:vertex.co-=pivot
        ob.parent=attachment;ob.matrix_parent_inverse=Matrix.Identity(4);ob.matrix_world=Matrix.Translation(pivot);ob['unity_imported']=False
    bpy.context.view_layer.update();assert geometry_signature(body)==body_signature
    crown=max((core.matrix_world@v.co).z for v in core.data.vertices)
    assert 1.79<crown<1.84,('Head attachment shifted the authored shape',style,crown)
    setup_stage(style);s=bpy.context.scene;s.render.engine='BLENDER_EEVEE';s.eevee.taa_render_samples=128
    s.render.resolution_x=720;s.render.resolution_y=800;s.camera=bpy.data.objects['Review_ThreeQuarter']
    report=inspect(style,[o for o in collection.objects if o.type=='MESH'],body,destination=VARIANT_DIR)
    bpy.ops.wm.save_as_mainfile(filepath=str(VARIANT_DIR/(style+'_Shape.blend')))
    pony_soft_rig(style,VARIANT_DIR/(style+'_Shape.blend'),VARIANT_DIR)
    print('VARIANT_BUILT',style,report['triangles'],flush=True)

def render_variant(style):
    bpy.ops.wm.open_mainfile(filepath=str(VARIANT_DIR/(style+'_FitReview.blend')))
    s=bpy.context.scene;s.render.engine='BLENDER_EEVEE';s.eevee.taa_render_samples=128
    for angle in ('Front','Side','Back','ThreeQuarter'):
        s.camera=bpy.data.objects['Review_'+angle];s.render.filepath=str(VARIANT_DIR/(style+'_'+angle+'.png'));bpy.ops.render.render(write_still=True)
    print('VARIANT_RENDERED',style,flush=True)

def animate_variant(style):
    bpy.ops.wm.open_mainfile(filepath=str(VARIANT_DIR/(style+'_FitReview.blend')))
    s=bpy.context.scene;walk=apply_project_walk(bpy.data.objects['FemaleRig'])
    report=bake_soft_hair(s.frame_start,s.frame_end,style)
    report['walk_source']=walk;report['unity_imported']=False
    assert max(report['final_displacement_m'].values())<.0001
    s.frame_set(1);s['hairstyle']=style;s['hair_motion_status']='Actual FemaleWalk bake; mesh and rig follow Head together.'
    bpy.ops.wm.save_as_mainfile(filepath=str(VARIANT_DIR/(style+'_Walk.blend')))
    (VARIANT_DIR/(style+'_walk_validation.json')).write_text(json.dumps(report,indent=2),encoding='utf-8')
    print('VARIANT_WALK_VALID',style,report['max_head_attachment_error_m'],flush=True)

def validate_variants(styles):
    """Reopen all delivered files and independently check the actual root vertices."""
    reports=[]
    for style in styles:
        record={'style':style,'files':[],'root_attachment_max_error_m':0.,'editable_rest_max_error_m':0.}
        signatures=None;reference=None;root_indices=None;reference_head=None
        for suffix in ('FitReview','Editable','Walk'):
            path=VARIANT_DIR/(style+'_'+suffix+'.blend');bpy.ops.wm.open_mainfile(filepath=str(path))
            rig=bpy.data.objects[style+'_SoftMotionRig'];core=bpy.data.objects[style+' | continuous fitted foundation']
            objects=[o for o in core.users_collection[0].objects if o.type=='MESH']
            assert core.parent.parent==rig
            for ob in objects:
                assert any(m.type=='ARMATURE' and m.object==rig for m in ob.modifiers)
                assert max(abs(sum(g.weight for g in v.groups)-1) for v in ob.data.vertices)<1e-6
            actual={o.name:geometry_signature(o,include_transform=False) for o in objects}
            if signatures is None:
                signatures=actual;reference={o.name:evaluated_surface(o)[0] for o in objects}
                root_indices={o.name:[v.index for v in o.data.vertices if any(g.group==o.vertex_groups['Hair_Head'].index and g.weight>.999999 for g in v.groups)] for o in objects}
                source=bpy.data.objects['FemaleRig'];reference_head=source.matrix_world@source.pose.bones['Head'].matrix
                record['root_vertices_checked']=sum(len(v) for v in root_indices.values())
            else:assert actual==signatures,(style,suffix,'Saved mesh changed')
            if suffix=='Editable':
                error=max((a-b).length for o in objects for a,b in zip(reference[o.name],evaluated_surface(o)[0]))
                record['editable_rest_max_error_m']=error;assert error<1e-5
                assert not rig.constraints
            if suffix=='Walk':
                source=bpy.data.objects['FemaleRig'];scene=bpy.context.scene
                for frame in (1,13,25,49,73,97,121,180):
                    scene.frame_set(frame);bpy.context.view_layer.update()
                    delta=(source.matrix_world@source.pose.bones['Head'].matrix)@reference_head.inverted()
                    for ob in objects:
                        live=evaluated_surface(ob)[0]
                        error=max(((live[i]-delta@reference[ob.name][i]).length for i in root_indices[ob.name]),default=0.)
                        record['root_attachment_max_error_m']=max(record['root_attachment_max_error_m'],error)
                        assert error<1e-5,(style,frame,error)
                scene.frame_set(49);scene.render.engine='BLENDER_EEVEE';scene.eevee.taa_render_samples=128
                scene.camera=bpy.data.objects['Review_ThreeQuarter'];scene.render.filepath=str(VARIANT_DIR/(style+'_WalkPose.png'));bpy.ops.render.render(write_still=True)
            record['files'].append({'name':path.name,'geometry_sha256_equal':True})
        reports.append(record);print('SAVED_VARIANT_VERIFIED',json.dumps(record),flush=True)
    expected=json.loads((P/'_History/BeforeFiveStyles_20261105/source_hashes.json').read_text(encoding='utf-8-sig'))
    unchanged={name:hashlib.sha256(Path(name).read_bytes()).hexdigest().upper()==value.upper() for name,value in expected.items()}
    assert all(unchanged.values()),unchanged
    (VARIANT_DIR/'saved_files_validation.json').write_text(json.dumps({'styles':reports,'existing_sources_unchanged':unchanged,'unity_imported':False},indent=2),encoding='utf-8')

def render_lineup():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for i,style in enumerate(STYLES):
        with bpy.data.libraries.load(str(P/(style+'_FitReview.blend')),link=False) as (src,dst):
            dst.objects=[x for x in src.objects if x=='FemaleBody' or x.startswith(style)]
        root=bpy.data.objects.new('Display '+style,None);bpy.context.scene.collection.objects.link(root)
        for ob in dst.objects:
            if ob is None or ob.type!='MESH':continue
            bpy.context.scene.collection.objects.link(ob);world=ob.matrix_world.copy();ob.parent=root;ob.matrix_world=world
            # Source armature is a dependency only; use authored rest geometry.
            for m in list(ob.modifiers):ob.modifiers.remove(m)
        root.rotation_euler.z=-.45;root.location.x=(i-1)*.74
    setup_stage('Collection');s=bpy.context.scene
    target=Vector((0,.07,1.43));c=bpy.data.objects['Review_Front'];c.location=target+Vector((0,-5,.02));c.rotation_euler=(target-c.location).to_track_quat('-Z','Y').to_euler();c.data.ortho_scale=2.42;s.camera=c
    s.render.resolution_x=2100;s.render.resolution_y=930;s.cycles.samples=24;s.render.filepath=str(P/'Hairstyles_ThreeQuarter_Collection.png');bpy.ops.render.render(write_still=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(P/'Hairstyles_Collection.blend'))
    for ob in bpy.data.objects:
        if ob.name.startswith('Display '):ob.rotation_euler.z=math.pi+.48
    s.render.filepath=str(P/'Hairstyles_Back_Collection.png');bpy.ops.render.render(write_still=True)
    print('COLLECTION_COMPLETE',flush=True)

if __name__=='__main__':
    args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else ['--build']
    chosen=[args[args.index('--style')+1]] if '--style' in args else STYLES
    if '--build' in args:
        for style in chosen:build(style)
    if '--refine' in args:
        for style in chosen:refine_requested_regions(style)
    if '--pony-ear-fit' in args:pony_ear_fit()
    if '--pony-wisp-density' in args:pony_wisp_density()
    if '--pony-soft-rig' in args:pony_soft_rig()
    if '--pony-soft-demo' in args:pony_soft_demo()
    if '--pony-soft-render' in args:render_soft_motion()
    if '--pony-soft-encode' in args:render_soft_motion(encode_only=True)
    if '--pony-wisp-preview' in args:render('Ponytail',False,('ThreeQuarter','Side'),'FullerWisps_')
    if '--pony-wisp-final' in args:render('Ponytail',True,('Front','Side','Back','ThreeQuarter','BackThreeQuarter'),'FullerWisps_')
    if '--pony-ear-preview' in args:render('Ponytail',False,('Front','Side','Back','ThreeQuarter'),'EarFit_')
    if '--pony-ear-final' in args:render('Ponytail',True,('Front','Side','Back','ThreeQuarter','BackThreeQuarter'),'EarFit_')
    if '--refined-preview' in args:
        for style in chosen:render(style,False,('Front','Side'),'Refined_')
    if '--refined-final' in args:
        for style in chosen:render(style,True,('Front','Side','Back'),'Refined_')
    if '--preview' in args or '--final' in args:
        for style in chosen:render(style,'--final' in args)
    if '--lineup' in args:render_lineup()
    variants=[args[args.index('--variant')+1]] if '--variant' in args else VARIANT_STYLES
    if '--variants-build' in args:
        for style in variants:build_variant(style)
    if '--variants-render' in args:
        for style in variants:render_variant(style)
    if '--variants-walk' in args:
        for style in variants:animate_variant(style)
    if '--variants-validate' in args:validate_variants(variants)
