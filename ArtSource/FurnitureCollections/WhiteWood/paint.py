import bpy, bmesh, math, json, os, sys, numpy as np
from mathutils import Vector,Matrix
sys.path.insert(0,os.path.dirname(os.path.abspath(__file__)))
import boundary_nonoffice, boundary_seating_audio, boundary_appliances, boundary_patterns
ROOT=r'C:/서현/프로젝트/companyGame';PROJECT=ROOT+'/CompanyGame'
OUT=PROJECT+'/Assets/Art/Items/Furniture/Themes/WhiteWood';PREVIEW=PROJECT+'/Docs/FurnitureCollections/WhiteWood'
os.makedirs(PREVIEW,exist_ok=True)
PALETTE={'Oak':'C79760','OakLight':'DAB680','Ivory':'EEE4D2','Linen':'DCCDB2','Olive':'71794D','Terracotta':'BE683C','Mustard':'CDA345','Leaf':'4F7132','LeafLight':'6B8744','Soil':'49392A','Charcoal':'252723','Screen':'181B1C','Brass':'AD8750','Metal':'868A87','Paper':'E0D4B9','Glow':'FFF1C4','Mirror':'C3CEC9','Glass':'454F50'}
props={'Oak':(.45,0),'OakLight':(.5,0),'Ivory':(.32,0),'Linen':(.9,0),'Olive':(.9,0),'Terracotta':(.36,0),'Mustard':(.48,0),'Leaf':(.52,0),'LeafLight':(.52,0),'Soil':(1,0),'Charcoal':(.4,.2),'Screen':(.18,.15),'Brass':(.3,.72),'Metal':(.27,.85),'Paper':(.85,0),'Glow':(.25,0),'Mirror':(.06,.95),'Glass':(.15,.45)}
PALETTE.update(Orange='D87939',BulbWhite='FFFFFF')
props.update(Orange=(.36,0),BulbWhite=(.25,0))
# Semantic material masks in each model's local furniture coordinates. These same regions can be reused by later themes.
yaws={('HomeOffice','Desk'):12,('HomeOffice','LoungeChair'):-35,('Bedroom','ChestOfDrawers'):70,('LivingRoom','Armchair'):-20,('Kitchen','Sink'):12,('Kitchen','MicrowaveCabinet'):6,('Kitchen','DiningChair'):-10}
def region(pack,key,x,y,z,nx,ny,nz):
 ax=abs(x);ay=abs(y)
 # Shared standalone furniture families.
 if 'Frame' in key:
  if ax<.405 and .08<z<.92 and ny<-.3:
   # Abstract paper art: muted organic blocks, deliberately restricted to the inner panel.
   if ('B' in key or 'C' in key):
    if ((x+.05)/.29)**2+((z-.55)/.33)**2<1:return 'Terracotta' if 'B' in key else 'Olive'
   elif x < .18*math.sin(z*9)-.06 and .18<z<.82:return 'Terracotta' if z>.5 else 'Charcoal'
   return 'Paper'
  return 'Oak'
 if key in ['Plant','PlantLarge','PlantSmall','PlantTrailing','FlowerVase']:
  cutoff={'Plant':.33,'PlantLarge':.36,'PlantSmall':.51,'PlantTrailing':.64,'FlowerVase':.48}[key]
  if z>cutoff:return 'Ivory' if key=='FlowerVase' and z>.72 else 'Leaf'
  if key=='PlantTrailing' and (x<-.15 or y<-.30) and z>.16:return 'Leaf'
  if z>cutoff-.08 and nz>.65:return 'Soil'
  return 'Terracotta' if key=='FlowerVase' else 'Ivory'
 if key in ['OfficeChair','SwivelChair','Armchair','LoungeChair','Sofa','DiningChair','Stool']:
  office=key in ['OfficeChair','SwivelChair']; fabric='Olive' if office else 'Linen'
  if office and z<.28:return 'Charcoal'
  if not office and z<(.4 if key=='Stool' else .22):return 'Oak'
  if key=='Sofa':
   if .58<z<.84 and -.35<x<-.08 and -.12<y<.22:return 'Terracotta'
   return 'Linen'
  if key in ['Armchair','LoungeChair']:
   if ax>.38 or (y>.36 and z<.9) or z<.28:return 'Oak'
   if .57<z<.74 and ax<.18 and -.17<y<.1:return 'Terracotta'
  if key=='DiningChair' and z<.49:return 'Oak'
  return fabric
 if key in ['DeskLamp','TableLamp','FloorLamp']:
  if key=='FloorLamp':return 'Glow' if z>.63 else 'Brass'
  if key=='DeskLamp':return 'Glow' if z>.72 and nz<-.3 else 'Terracotta' if z<.2 or z>.72 else 'Brass'
  return 'Glow' if .45<z<.61 else 'Terracotta'
 if key in ['SpeakerA','SpeakerB']:
  if ny<-.25 and ax<.36 and .09<z<.91:
   return 'Charcoal' if ((x/.28)**2+((z-.33)/.24)**2<1 or (x/.2)**2+((z-.76)/.14)**2<1) else 'Oak'
  return 'Ivory'
 if key=='DiningTable':return 'OakLight'
 if key in ['CoffeeTableDecorated','SideTableDecorated']:
  top=.56 if key=='CoffeeTableDecorated' else .55
  if z<top:return 'OakLight' if key=='CoffeeTableDecorated' else 'Mustard'
  if z>.79:return 'Leaf' if z>.88 else 'Ivory'
  return 'Paper'
 if key=='Cushion':return 'Olive'
 if key=='Rug':return 'Terracotta' if (int((x+.5)*6)+int((y+.5)*4))%2==0 else 'Linen'
 if key=='Curtain':
  if z>.92 or ax>.43 or (ax<.16 and .12<z<.88):return 'Oak'
  return 'Linen'
 if key in ['FabricBox','WoodBox']:return 'Linen' if key=='FabricBox' else 'OakLight'
 if key in ['SquareBasket','RoundBasket','LaundryBasket']:
  if key=='LaundryBasket' and z>.7 and y<.1:return 'Olive'
  if z>.8 and ax<.32 and ay<.32:return 'Linen'
  return 'OakLight'
 if key=='Sculpture':return 'Paper' if z<.27 else 'Ivory'
 if key=='WallClock':
  if ax<.39 and .11<z<.9 and ny<-.2:return 'Ivory' if y>-.46 else 'Oak'
  return 'Oak'
 if key=='CoatRack':
  if ax>.17 and .38<z<.86:return 'Terracotta' if x<0 else 'Linen'
  return 'Oak'
 if key=='Washer':
  if ny<-.2 and .13<z<.73 and ((x+.02)/.35)**2+((z-.42)/.3)**2<1:return 'Glass' if ax<.24 else 'Metal'
  if ny<-.2 and .81<z<.91 and x>.03:return 'Screen'
  return 'Ivory'
 if key=='Fridge':return 'Brass' if y<-.455 and -.4<x<-.22 and .19<z<.91 else 'Ivory'
 if key=='Mirror':return 'Mirror' if ax<.37 and .12<z<.93 and (abs(nx)>.25 or abs(ny)>.25) else 'Oak'
 if key=='Bed':
  if z<.26:return 'Oak'
  if x>.06:return 'Terracotta' if x>.26 else 'Olive'
  return 'Linen'
 if key=='LoftBed':
  if z>.8 and ax<.36:return 'Mustard' if x>.05 else 'Linen'
  if y<-.25 and x<.12 and .14<z<.73:return 'Ivory'
  if x<-.25 and .35<z<.63:return 'Paper'
  return 'OakLight'
 if key=='ComputerSet':
  if x>.30 and y>.02 and z>.20:return 'Leaf' if z>.31 else 'Ivory'
  if z>.37:return 'Screen'
  if y<-.17 and z<.13:return 'Ivory' if nz>.3 else 'Oak'
  if .12<z<.33:return 'Ivory' if nz>.5 else 'Oak'
  if z<.37 and x>.33 and y>.02:return 'Leaf' if z>.23 else 'Ivory'
  return 'Charcoal'
 if key=='TVSet':
  if z>.48:
   if x<-.36:return 'Leaf'
   return 'Screen'
  if .32<z<.48:return 'Ivory'
  if z<.09:return 'Oak'
  return 'OakLight' if ny<-.35 else 'Ivory'
 if key=='Desk':
  if z<.57:return 'Ivory' if x>.2 and ny<-.4 and z>.08 else 'Oak'
  if z<.65:return 'Ivory'
  if x<-.33:return 'Leaf' if z>.76 else 'Ivory'
  if ax<.21:return 'Screen' if z>.73 else 'Metal'
  return 'Paper' if x<.33 else 'Terracotta'
 if key in ['Wardrobe','ChestOfDrawers','DrawerUnit','BedsideCabinet','ShoeCabinet','StorageShelf','Bookcase','WallCabinet']:
  if key=='DrawerUnit':
   if z<.075:return 'Charcoal'
   if z>.83:return 'Leaf' if x>.15 else 'OakLight'
   return 'Ivory'
  if key=='ChestOfDrawers' and x<-.30 and y<-.15 and z>.55:return 'Leaf'
  if key in ['Wardrobe','ChestOfDrawers']:
   if z<.08:return 'Oak'
   if z>.77 and key in ['ChestOfDrawers','DrawerUnit']:
    if x<-.15:return 'Leaf' if z>.88 else 'Ivory'
    return 'Paper' if ax<.25 else 'Terracotta'
   return 'Ivory' if ax<.435 and ny<-.35 and y>-.48 else 'Oak'
  if key=='BedsideCabinet':
   if z>.67:return 'Glow' if x<.08 else 'Leaf' if z>.85 else 'Ivory'
   return 'Ivory' if ny<-.4 and .12<z<.54 else 'Oak'
  if key=='ShoeCabinet':
   if z>.75:return 'Leaf' if x<-.23 else 'Glow' if x>.24 else 'Ivory'
   if z<.17:return 'Linen' if ax<.4 else 'Oak'
   return 'Oak' if ny<-.4 and .25<z<.7 else 'Ivory'
  if key=='WallCabinet':return 'Ivory' if x<.1 and ny<-.3 else 'OakLight'
  if key=='Bookcase' and x>.25 and z>.7 and y<-.12:return 'Leaf'
  if key=='StorageShelf' and x<-.1 and z>.45 and y<-.23:return 'Leaf'
  if ax>.4 or y>.3 or z<.05:return 'Oak'
  if ny<-.25 and z<.35:return 'Ivory'
  return 'Oak' if nz>.65 else 'Paper'
 if key=='Shelf':
  if pack=='Kitchen':
   if ax>.42:return 'Charcoal'
   if nz>.65:return 'OakLight'
   if z>.81 and x<-.12:return 'Leaf'
   return 'Ivory' if int(z*12)%3 else 'Oak'
  if x<-.2 and z>.63:return 'Leaf'
  if nz>.55 or ax>.43:return 'Oak'
  return 'Paper' if int(z*15)%3 else 'Ivory'
 if key=='WallOrganizer':
  if y>-.07:return 'Oak'
  if x<-.14 and z>.6:return 'Leaf'
  return 'Paper' if x>.05 else 'Ivory'
 if key=='Vanity':
  if z>.58:return 'Mirror' if ax<.24 else 'Leaf' if x>.28 and z>.76 else 'Brass'
  if z<.15:return 'Oak'
  return 'Linen' if y<-.12 else 'Ivory'
 if key=='CookingIsland':
  if z<.52:return 'Oak' if ny<-.35 else 'Ivory'
  if z<.63:return 'Ivory'
  if x>.27:return 'Leaf' if z>.8 else 'Ivory'
  if x<-.05 and z>.77:return 'Ivory'
  return 'Charcoal'
 if key=='MicrowaveCabinet':
  if z>.54:
   if x<-.28:return 'Leaf'
   if ny<-.25 and -.22<x<.26 and .66<z<.88:return 'Screen'
   return 'Ivory'
  if nz>.5 or ax>.42:return 'OakLight'
  return 'Ivory' if z>.24 else 'Oak'
 if key=='Sink':
  if z<.58:return 'Oak' if ny<-.3 else 'Ivory'
  if z<.67:return 'Metal' if ax<.2 and ay<.28 else 'Ivory'
  if x<-.31:return 'Leaf'
  return 'Brass' if ax<.1 else 'OakLight'
 if key=='DishRack':return 'Ivory' if ax>.26 or z<.1 else 'Olive' if int(x*24)%2 else 'OakLight'
 return 'Ivory'

def lin(h):
 v=[int(h[i:i+2],16)/255 for i in (0,2,4)];return tuple(a/12.92 if a<=.04045 else ((a+.055)/1.055)**2.4 for a in v)
data=json.load(open(ROOT+'/ArtSource/FurnitureCollections/catalog.json',encoding='utf-8'));themed=[]
selected_keys=set(sys.argv[sys.argv.index('--')+2:]) if '--' in sys.argv else set()
for pack in ['HomeOffice','Bedroom','LivingRoom','Kitchen','Essentials']:
 if '--' in sys.argv and pack!=sys.argv[sys.argv.index('--')+1]:continue
 thumbs=[]
 for row in [r for r in data['items'] if r['pack']==pack and (not selected_keys or r['key'] in selected_keys)]:
  bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=PROJECT+'/'+row['modelPath'])
  obj=next(o for o in bpy.context.scene.objects if o.type=='MESH');bpy.context.view_layer.objects.active=obj;obj.select_set(True);bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
  mesh=obj.data
  # Reviewed face assignments retain the original topology. Speakers alone are
  # cut exactly along their fitted driver circles and front-panel outline.
  if pack=='HomeOffice' and row['key'] in ('SpeakerA','SpeakerB'):
   boundary_seating_audio.preprocess(mesh,row['key'])
  boundary_appliances.preprocess(pack,row['key'],mesh)
  mesh.materials.clear();roles=list(PALETTE)
  for name,color in PALETTE.items():
   mat=bpy.data.materials.new('WW_'+name);mat.diffuse_color=(*lin(color),1);mat.use_nodes=True;bsdf=mat.node_tree.nodes.get('Principled BSDF');bsdf.inputs['Base Color'].default_value=mat.diffuse_color;bsdf.inputs['Roughness'].default_value=props[name][0];bsdf.inputs['Metallic'].default_value=props[name][1];mesh.materials.append(mat)
  rot=Matrix.Rotation(math.radians(-yaws.get((pack,row['key']),0)),3,'Z');coords=[rot@v.co for v in mesh.vertices];lo=Vector([min(c[j] for c in coords) for j in range(3)]);hi=Vector([max(c[j] for c in coords) for j in range(3)]);size=hi-lo
  mirror_normal=None
  if row['key']=='Mirror':
   cov=np.zeros((3,3))
   for f in mesh.polygons:
    nn=np.array(rot@f.normal);cov+=f.area*np.outer(nn,nn)
   mirror_normal=np.linalg.eigh(cov)[1][:,-1]
  initial=[];centres=[];normals=[]
  for face in mesh.polygons:
   c=rot@face.center;n=rot@face.normal;x=(c.x-lo.x)/max(size.x,.0001)-.5;y=(c.y-lo.y)/max(size.y,.0001)-.5;z=(c.z-lo.z)/max(size.z,.0001)
   role=region(pack,row['key'],x,y,z,*n)
   if mirror_normal is not None:role='Mirror' if abs(float(np.dot(np.array(n),mirror_normal)))>.975 else 'Oak'
   initial.append(role);centres.append((x,y,z));normals.append(tuple(n))
  mask_path=os.path.dirname(__file__)+'/Masks/'+pack+'_'+row['key']+'.npz'
  if os.path.exists(mask_path):
   reviewed=np.load(mask_path)
   if int(reviewed['face_count'])!=len(mesh.polygons):raise ValueError('Source topology changed: '+pack+'/'+row['key'])
   assigned=reviewed['roles'].tolist()
  else:
   assigned=boundary_nonoffice.classify(pack,row['key'],mesh,centres,normals,initial)
   assigned=boundary_seating_audio.classify(pack,row['key'],mesh,centres,normals,assigned)
   assigned=boundary_appliances.classify(pack,row['key'],mesh,centres,normals,assigned)
  for face,role in zip(mesh.polygons,assigned):face.material_index=roles.index(role)
  boundary_patterns.finish(pack,row['key'],mesh,roles,rot,lo,size)
  coords=[rot@v.co for v in mesh.vertices]
  # UVs are expressed in metres for consistent wood/linen detail density.
  uv=mesh.uv_layers.new(name='ThemeUV')
  for face in mesh.polygons:
   n=rot@face.normal;axis=max(range(3),key=lambda i:abs(n[i]));a,b=((1,2) if axis==0 else (0,2) if axis==1 else (0,1))
   for loop in face.loop_indices:
    co=coords[mesh.loops[loop].vertex_index];uv.data[loop].uv=(co[a]*3,co[b]*3)
  dest=OUT+'/Models/'+pack;os.makedirs(dest,exist_ok=True);path=dest+'/'+row['key']+'.fbx'
  bpy.ops.export_scene.fbx(filepath=path,use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',add_leaf_bones=False,bake_anim=False)
  entry=dict(row);entry['themeModelPath']=path[len(PROJECT)+1:];entry['materials']=['WW_'+roles[i] for i in sorted(set(f.material_index for f in mesh.polygons))];entry['triangles']=sum(len(f.vertices)-2 for f in mesh.polygons);themed.append(entry)
  scene=bpy.context.scene;scene.render.engine='BLENDER_WORKBENCH';scene.display.shading.light='STUDIO';scene.display.shading.color_type='MATERIAL';scene.display.shading.show_specular_highlight=False;scene.view_settings.view_transform='Standard';scene.view_settings.exposure=.3;scene.display.shading.show_shadows=False;scene.display.shading.show_cavity=True;scene.display.shading.cavity_type='BOTH';scene.display.shading.background_type='WORLD';scene.world=bpy.data.worlds.new('Preview');scene.world.color=(.22,.20,.17)
  bpy.ops.object.camera_add();cam=bpy.context.object;scene.camera=cam;cam.data.type='ORTHO';dims=obj.dimensions;center=Vector((0,0,dims.z*.5));span=max(dims);cam.location=center+Vector((1.3,-2,1.2))*span;cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=span*1.4
  scene.render.resolution_x=900;scene.render.resolution_y=900;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG';scene.render.filepath=PREVIEW+'/'+pack+'_'+row['key']+'.png';bpy.ops.render.render(write_still=True)
  img=bpy.data.images.load(scene.render.filepath);img.scale(360,360);arr=np.empty(360*360*4,dtype=np.float32);img.pixels.foreach_get(arr);thumbs.append(arr.reshape((360,360,4)))
  if pack=='HomeOffice':
   for view,offset in [('Front',(0,-3,.45)),('Back',(-1.3,2,1.2))]:
    cam.location=center+Vector(offset)*span;cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler();scene.render.filepath=PREVIEW+'/'+pack+'_'+row['key']+'_'+view+'.png';bpy.ops.render.render(write_still=True)
  print(pack,row['key'],entry['materials'],flush=True)
 if selected_keys:continue
 rows=math.ceil(len(thumbs)/4);canvas=np.ones((rows*360,1440,4),dtype=np.float32);canvas[:,:,:3]=(.22,.20,.17)
 for i,a in enumerate(thumbs):
  rr=rows-1-i//4;cc=i%4;canvas[rr*360:(rr+1)*360,cc*360:(cc+1)*360]=a
 image=bpy.data.images.new(pack+'_Contact',width=1440,height=rows*360);image.pixels.foreach_set(canvas.ravel());image.filepath_raw=PREVIEW+'/'+pack+'_Contact.png';image.file_format='PNG';image.save()
# Merge selective reruns into the full catalog.
manifest=ROOT+'/ArtSource/FurnitureCollections/WhiteWood/catalog.json'
if '--' in sys.argv and os.path.exists(manifest):
 old=json.load(open(manifest,encoding='utf-8'))['items'];ids={(e['pack'],e['key']) for e in themed};themed=[e for e in old if (e['pack'],e['key']) not in ids]+themed
json.dump(dict(items=themed,palette=[dict(name='WW_'+k,hex=v,roughness=props[k][0],metallic=props[k][1]) for k,v in PALETTE.items()]),open(manifest,'w',encoding='utf-8'),ensure_ascii=False,indent=2)



