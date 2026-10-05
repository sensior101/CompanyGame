import bpy,bmesh,json,os,math,sys,numpy as np
from mathutils import Vector
ROOT=r'C:/서현/프로젝트/companyGame';PROJ=ROOT+'/CompanyGame';OUT=PROJ+'/Assets/Art/Items/Furniture/Collections';PREVIEW=PROJ+'/Docs/FurnitureCollections'
os.makedirs(PREVIEW,exist_ok=True)
data=json.load(open(ROOT+'/ArtSource/FurnitureCollections/catalog.json',encoding='utf-8'))
files={'HomeOffice':'가구 모음_홈오피스템.fbx','Bedroom':'가구 모음_ 안방템.fbx','LivingRoom':'가구 모음_거실템.fbx','Kitchen':'가구 모음_주방템.fbx','Essentials':'가구 모음_필수가구템.fbx'}
for pack,filename in files.items():
 if '--' in sys.argv and pack!=sys.argv[sys.argv.index('--')+1]:continue
 bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath='C:/서현/3D 모델/가구/'+filename)
 src=next(o for o in bpy.context.scene.objects if o.type=='MESH');mesh=src.data
 parent=list(range(len(mesh.vertices)))
 def find(a):
  while parent[a]!=a:parent[a]=parent[parent[a]];a=parent[a]
  return a
 for e in mesh.edges:
  a,b=map(find,e.vertices)
  if a!=b:parent[b]=a
 groups={}
 for i in range(len(parent)):groups.setdefault(find(i),[]).append(i)
 groups=sorted(groups.values(),key=lambda g:-len(g));source_matrix=src.matrix_world.copy();src.hide_render=True;src.hide_set(True)
 scene=bpy.context.scene;scene.render.engine='BLENDER_WORKBENCH';scene.display.shading.light='STUDIO';scene.display.shading.color_type='SINGLE';scene.display.shading.single_color=(.65,.65,.65);scene.display.shading.show_shadows=True;scene.display.shading.show_cavity=True;scene.display.shading.cavity_type='BOTH';scene.display.shading.background_type='WORLD';scene.world=bpy.data.worlds.new('Preview');scene.world.color=(.11,.11,.11)
 scene.render.resolution_x=360;scene.render.resolution_y=360;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG'
 bpy.ops.object.camera_add();cam=bpy.context.object;scene.camera=cam;cam.data.type='ORTHO'
 thumbs=[]
 for entry in [r for r in data['items'] if r['pack']==pack]:
  keep=set(i for g in entry['parts'] for i in groups[g]);bm=bmesh.new();bm.from_mesh(mesh);bm.verts.ensure_lookup_table();bmesh.ops.delete(bm,geom=[v for v in bm.verts if v.index not in keep],context='VERTS');bmesh.ops.transform(bm,matrix=source_matrix,verts=list(bm.verts))
  if pack=='Kitchen' and entry['key']=='DiningTable':
   cut=bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),dist=.000001,plane_co=(0,0,-.0078),plane_no=(0,0,1),clear_outer=True,clear_inner=False)
   boundary=[e for e in bm.edges if e.is_boundary and all(abs(v.co.z+.0078)<.00005 for v in e.verts)]
   if boundary:bmesh.ops.holes_fill(bm,edges=boundary,sides=0)
   for face in bm.faces:
    if all(abs(v.co.z+.0078)<.00005 for v in face.verts):face.smooth=False
   bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
   entry['note']='상판 높이에서 장식을 절단하고 구멍을 메워 식탁만 보존';entry['height']=.75
  coords=[v.co for v in bm.verts];lo=Vector([min(c[j] for c in coords) for j in range(3)]);hi=Vector([max(c[j] for c in coords) for j in range(3)])
  pivot=Vector(((lo.x+hi.x)/2,(lo.y+hi.y)/2,lo.z));scale=entry['height']/(hi.z-lo.z) if entry['height'] else 1.6/(hi.x-lo.x)
  for v in bm.verts:v.co=(v.co-pivot)*scale
  dest=bpy.data.meshes.new(entry['key']);bm.to_mesh(dest);bm.free();dest.update()
  for mat in mesh.materials:dest.materials.append(mat)
  obj=bpy.data.objects.new(entry['key'],dest);scene.collection.objects.link(obj)
  bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
  folder=OUT+'/'+pack;os.makedirs(folder,exist_ok=True);path=folder+'/'+entry['key']+'.fbx'
  bpy.ops.export_scene.fbx(filepath=path,use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',add_leaf_bones=False,bake_anim=False,use_mesh_modifiers=True)
  entry['modelPath']=path[len(PROJ)+1:];entry['vertices']=len(dest.vertices);entry['triangles']=sum(len(p.vertices)-2 for p in dest.polygons);entry['sourceFile']=filename
  bpy.context.view_layer.update();dims=obj.dimensions;center=Vector((0,0,dims.z*.5));span=max(dims);cam.location=center+Vector((1.3,-2,1.2))*span;cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=span*1.55;scene.render.filepath=PREVIEW+'/'+pack+'_'+entry['key']+'.png';bpy.ops.render.render(write_still=True)
  img=bpy.data.images.load(scene.render.filepath);arr=np.empty(360*360*4,dtype=np.float32);img.pixels.foreach_get(arr);thumbs.append(arr.reshape((360,360,4)));bpy.data.images.remove(img)
  bpy.data.objects.remove(obj,do_unlink=True);bpy.data.meshes.remove(dest)
  print(pack,entry['key'],entry['vertices'],flush=True)
 rows=math.ceil(len(thumbs)/4);canvas=np.zeros((rows*360,4*360,4),dtype=np.float32);canvas[:,:,:3]=.11;canvas[:,:,3]=1
 for i,a in enumerate(thumbs):
  row=rows-1-i//4;col=i%4;canvas[row*360:(row+1)*360,col*360:(col+1)*360]=a
 img=bpy.data.images.new(pack+'_Contact',width=1440,height=rows*360);img.pixels.foreach_set(canvas.ravel());img.filepath_raw=PREVIEW+'/'+pack+'_Contact.png';img.file_format='PNG';img.save()
 with open(ROOT+'/ArtSource/FurnitureCollections/catalog.json','w',encoding='utf-8') as f:json.dump(data,f,ensure_ascii=False,indent=2)
print('COMPLETE',len(data['items']),flush=True)

