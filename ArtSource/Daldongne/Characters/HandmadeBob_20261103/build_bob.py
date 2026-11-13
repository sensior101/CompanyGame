"""Continuous sculpted bob, built and reviewed entirely in Blender.
The mesh is a single connected hair shell, with flowing UVs and integral tapered ends.
The existing female body is opened read-only as a fitting reference.
"""
import bpy,bmesh,math,random,json
from pathlib import Path
from mathutils import Vector
P=Path(__file__).resolve().parent
SOURCE=P.parent/'MeshyArticulated_20261028/FemaleArticulated.blend'
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
body=bpy.data.objects['FemaleBody'];rig=bpy.data.objects['FemaleRig']
for ob in list(bpy.data.objects):
 if ob not in [body,rig]:bpy.data.objects.remove(ob,do_unlink=True)

def clamp(x,a=0,b=1):return max(a,min(b,x))
def smooth(a,b,x):
 t=clamp((x-a)/(b-a));return t*t*(3-2*t)
def linear(c):return c/12.92 if c<.04045 else ((c+.055)/1.055)**2.4
def rgb(h):return tuple(linear(int(h[i:i+2],16)/255) for i in [1,3,5])+(1,)

# Hand-shaped silhouette samples. Interpolated curves replace assembled strand solids.
profile=[(1.420,.203,.169),(1.455,.221,.184),(1.505,.234,.199),(1.565,.239,.215),(1.635,.233,.222),(1.700,.215,.209),(1.755,.181,.175),(1.795,.132,.127),(1.823,.068,.065),(1.840,0,0)]
def shape(z,axis):
 z=clamp(z,profile[0][0],profile[-1][0])
 if z>=1.795:return (.182 if axis==1 else .175)*math.sqrt(max(0,1-((z-1.695)/.145)**2))
 for j in range(len(profile)-1):
  if z<=profile[j+1][0]:break
 p0=profile[max(0,j-1)][axis];p1=profile[j][axis];p2=profile[j+1][axis];p3=profile[min(len(profile)-1,j+2)][axis]
 t=(z-profile[j][0])/(profile[j+1][0]-profile[j][0]);return max(0,.5*((2*p1)+(-p0+p2)*t+(2*p0-5*p1+4*p2-p3)*t*t+(-p0+3*p1-3*p2+p3)*t*t*t))

rng=random.Random(717)
count=28;per=6;halfweights=[rng.uniform(.83,1.18) for _ in range(count//2)];positive=[0.0]
for w in halfweights:positive.append(positive[-1]+math.pi*w/sum(halfweights))
bounds=[-v for v in reversed(positive[1:])]+positive
# A unified loft follows each flow line all the way from crown to cut edge.
rows=[.015,.04,.08,.14,.22,.32,.43,.55,.66,.76,.84,.90,.95,.98,1.0]
V=[];UV=[];F=[];N=count*(per+1)
for ri,t in enumerate(rows):
 for k in range(count):
  for j in range(per+1):
   q=j/per;u=(k+q)/count;a=bounds[k]*(1-q)+bounds[k+1]*q
   front=1-smooth(.68,1.02,abs(a));back=max(0,-math.cos(a))**2
   # A nearly level fringe with millimetre-scale feathering, not broad lobes.
   fringe_end=1.621-.003*smooth(.25,.68,abs(a))+.0006*math.cos(26*a)
   for cut in [.14,.31,.49,.62]:fringe_end+=.0018*math.exp(-((abs(a)-cut)/.014)**2)
   rear_end=1.437+.018*back+.003*math.cos(7*a)
   notch=(math.exp(-(q/.10)**2)+math.exp(-((1-q)/.10)**2))
   end=fringe_end*front+(rear_end+.008*notch)*(1-front)
   z=1.840+(end-1.840)*t
   sweep=.018*math.sin(2*a)*math.sin(math.pi*t*.8)
   width=1-.72*(1-front)*smooth(.94,1,t)
   qa=.5+(q-.5)*width
   aa=bounds[k]*(1-qa)+bounds[k+1]*qa+sweep+.018*math.sin(3*a)*math.sin(2*math.pi*t)*t*(1-front)
   cx=0.0;cy=.026-.008*(1-t)
   rx=shape(z,1);ry=shape(z,2)*(1-.105*max(0,math.cos(aa))**2)
   # shallow, non-uniform ridges and grooves sculpt flow through one surface
   strength=smooth(.07,.40,t)
   wave=math.sin(math.pi*q)**1.4
   relief=(.0019*wave-.0012*notch)*strength*(1-.8*front)
   relief+=.0005*math.cos(aa*3)*math.sin(4*t)*strength*(1-front)
   # slight inward curl, especially at the nape and cheek framing ends
   curl=.006*smooth(.83,1,t)*(1-front*.6)
   x=cx+(rx+relief-curl)*math.sin(aa)
   y=cy-(ry+relief-curl)*math.cos(aa)-.018*front*max(0,math.cos(aa))**4*smooth(.60,1,t)
   V.append((x,y,z));UV.append((u,t))
for r in range(len(rows)-1):
 for k in range(count):
  for c in range(per):
   a=r*N+k*(per+1)+c;b=a+1;d=a+N;e=b+N
   F.append((a,b,e,d))
V.append((0,.018,1.841));UV.append((.5,0));pole=len(V)-1
for k in range(count):
 for c in range(per):F.append((pole,k*(per+1)+c+1,k*(per+1)+c))
# Lower only the crown, with a smooth transition into the unchanged fringe and side lengths.
rounded=[]
for x,y,z in V:
 lowered=z-.028*smooth(1.710,1.841,z)
 radius2=(x/.205)**2+((y-.026)/.195)**2
 rounded_cap=1.813-.034*radius2-.024*radius2*radius2
 blend=smooth(1.730,1.785,lowered)
 rounded.append((x,y,min(z,lowered*(1-blend)+rounded_cap*blend)))
V=rounded
me=bpy.data.meshes.new('Bob continuous flow topology');me.from_pydata(V,[],F);me.update()

ob=bpy.data.objects.new('SoftBob_Hair',me);bpy.context.collection.objects.link(ob)
uv=me.uv_layers.new(name='HairFlow')
for poly in me.polygons:
 coords=[UV[me.loops[i].vertex_index] for i in poly.loop_indices];us=[u for u,v in coords if v!=0];seam=bool(us and max(us)-min(us)>.5)
 for li,(u,v) in zip(poly.loop_indices,coords):
  if v==0:u=sum((x+1 if seam and x<.5 else x) for x in us)/len(us)
  elif seam and u<.5:u+=1
  uv.data[li].uv=(u,v)
 poly.use_smooth=True
bm=bmesh.new();bm.from_mesh(me);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.000001);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(me);bm.free()
# Procedural strand shading follows the mesh UV flow; it does not disguise separate primitives.
mat=bpy.data.materials.new('Ash brown - flowing hair');mat.diffuse_color=(.29,.20,.16,1);mat.use_nodes=True
nd=mat.node_tree.nodes;ln=mat.node_tree.links;nd.clear();out=nd.new('ShaderNodeOutputMaterial');bs=nd.new('ShaderNodeBsdfPrincipled');ln.new(bs.outputs['BSDF'],out.inputs['Surface']);bs.inputs['Roughness'].default_value=.53
if 'Anisotropic IOR Level' in bs.inputs:bs.inputs['Anisotropic IOR Level'].default_value=.60
if 'Anisotropic Rotation' in bs.inputs:bs.inputs['Anisotropic Rotation'].default_value=.25
bs.inputs['Specular IOR Level'].default_value=.27
uvn=nd.new('ShaderNodeUVMap');uvn.uv_map='HairFlow';se=nd.new('ShaderNodeSeparateXYZ');ln.new(uvn.outputs['UV'],se.inputs[0])
root=nd.new('ShaderNodeValToRGB');root.color_ramp.elements[0].position=.06;root.color_ramp.elements[0].color=rgb('#30282A');root.color_ramp.elements[1].position=.96;root.color_ramp.elements[1].color=rgb('#81665C');el=root.color_ramp.elements.new(.36);el.color=rgb('#59443E');ln.new(se.outputs['Y'],root.inputs['Fac'])
# Longitudinal fibre variation, with very small relief rather than embossed grooves.
angle=nd.new('ShaderNodeMath');angle.operation='MULTIPLY';angle.inputs[1].default_value=math.tau;ln.new(se.outputs['X'],angle.inputs[0])
coords=nd.new('ShaderNodeCombineXYZ')
for trig,socket in [('SINE','X'),('COSINE','Y')]:
 n=nd.new('ShaderNodeMath');n.operation=trig;ln.new(angle.outputs[0],n.inputs[0]);mul=nd.new('ShaderNodeMath');mul.operation='MULTIPLY';mul.inputs[1].default_value=40;ln.new(n.outputs[0],mul.inputs[0]);ln.new(mul.outputs[0],coords.inputs[socket])
zscale=nd.new('ShaderNodeMath');zscale.operation='MULTIPLY';zscale.inputs[1].default_value=3.5;ln.new(se.outputs['Y'],zscale.inputs[0]);ln.new(zscale.outputs[0],coords.inputs['Z'])
noise=nd.new('ShaderNodeTexNoise');noise.inputs['Scale'].default_value=1;noise.inputs['Detail'].default_value=2;noise.inputs['Roughness'].default_value=.5;ln.new(coords.outputs['Vector'],noise.inputs['Vector'])
fibre=nd.new('ShaderNodeValToRGB');fibre.color_ramp.elements[0].position=.20;fibre.color_ramp.elements[0].color=(.48,.48,.48,1);fibre.color_ramp.elements[1].position=.78;fibre.color_ramp.elements[1].color=(1,1,1,1);ln.new(noise.outputs['Fac'],fibre.inputs[0]);mix=nd.new('ShaderNodeMixRGB');mix.blend_type='MULTIPLY';mix.inputs[0].default_value=.45;ln.new(root.outputs['Color'],mix.inputs[1]);ln.new(fibre.outputs['Color'],mix.inputs[2]);ln.new(mix.outputs['Color'],bs.inputs['Base Color'])
bump=nd.new('ShaderNodeBump');bump.inputs['Strength'].default_value=.13;bump.inputs['Distance'].default_value=.00018;ln.new(noise.outputs['Fac'],bump.inputs['Height']);ln.new(bump.outputs['Normal'],bs.inputs['Normal'])
tan=nd.new('ShaderNodeTangent');tan.direction_type='UV_MAP';tan.uv_map='HairFlow';ln.new(tan.outputs['Tangent'],bs.inputs['Tangent'])
me.materials.append(mat)
bpy.ops.object.select_all(action='DESELECT');ob.select_set(True);bpy.context.view_layer.objects.active=ob
sol=ob.modifiers.new('Fine inward edge thickness','SOLIDIFY');sol.thickness=.0015;sol.offset=-1;sol.use_even_offset=False;bpy.ops.object.modifier_apply(modifier=sol.name)
# Keep the lower cut rim sharp while the main volume carries soft normals.
for p in ob.data.polygons:
 if len(p.vertices)==4 and max(ob.data.vertices[v].co.z for v in p.vertices)-min(ob.data.vertices[v].co.z for v in p.vertices)<.004:p.use_smooth=False
# Collapse redundant interior and crown topology while preserving the flowing UVs.
opt=ob.modifiers.new('Game mesh reduction','DECIMATE');opt.ratio=.45;opt.use_collapse_triangulate=True;bpy.ops.object.modifier_apply(modifier=opt.name)
# Enforce exact left-right agreement after polygon reduction.
mirror=ob.modifiers.new('Exact centre mirror','MIRROR');mirror.use_axis=(True,False,False);mirror.use_bisect_axis=(True,False,False);mirror.use_clip=True;mirror.use_mirror_merge=True;mirror.merge_threshold=.000001;bpy.ops.object.modifier_apply(modifier=mirror.name)
pivot=rig.matrix_world@rig.data.bones['Head'].head_local
for v in ob.data.vertices:v.co-=pivot
ob.location=pivot
coll=bpy.data.collections.new('Bob - continuous sculpt');bpy.context.scene.collection.children.link(coll)
for c in list(ob.users_collection):c.objects.unlink(ob)
coll.objects.link(ob)
rootob=bpy.data.objects.new('Bob attachment - Head',None);coll.objects.link(rootob);rootob.location=pivot;bpy.context.view_layer.update();world=ob.matrix_world.copy();ob.parent=rootob;ob.matrix_world=world
ob['design']='Symmetrical bob with fine level fringe and connected flow surface';ob['unity_imported']=False
s=bpy.context.scene;s.render.engine='BLENDER_WORKBENCH';s.view_settings.view_transform='Standard';s.view_settings.look='None';s.view_settings.exposure=0;s.view_settings.gamma=1;s.display.shading.light='STUDIO';s.display.shading.color_type='MATERIAL';s.display.shading.show_cavity=False;s.display.shading.show_shadows=True;s.display.shading.background_type='WORLD';s.world=bpy.data.worlds.new('Review world');s.world.color=(.23,.25,.29);s.render.resolution_x=800;s.render.resolution_y=800;s.render.resolution_percentage=100;s.render.image_settings.file_format='PNG';s.render.film_transparent=False
for label,a in [('Front',0),('Side',math.pi/2),('Back',math.pi),('ThreeQuarter',.55)]:
 target=Vector((0,.025,1.64));bpy.ops.object.camera_add(location=target+Vector((math.sin(a)*4,-math.cos(a)*4,.08)));c=bpy.context.object;c.name='Review_'+label;c.rotation_euler=(target-c.location).to_track_quat('-Z','Y').to_euler();c.data.type='ORTHO';c.data.ortho_scale=.64;s.camera=c;s.render.filepath=str(P/('Draft_'+label+'.png'));bpy.ops.render.render(write_still=True)
s.camera=bpy.data.objects['Review_ThreeQuarter'];bpy.ops.object.select_all(action='DESELECT');ob.select_set(True);bpy.context.view_layer.objects.active=ob
for ar in bpy.context.screen.areas:
 if ar.type=='VIEW_3D':
  ar.spaces.active.region_3d.view_distance=.85;ar.spaces.active.region_3d.view_location=(0,0,1.64);ar.spaces.active.shading.type='SOLID'
bpy.ops.wm.save_as_mainfile(filepath=str(P/'LayeredBob_FitReview.blend'))
print('CONTINUOUS_BOB',len(ob.data.vertices),sum(len(p.vertices)-2 for p in ob.data.polygons),flush=True)

