import bpy,json,math,os,sys,numpy as np
from mathutils import Vector,Matrix
from bpy_extras.object_utils import world_to_camera_view
from pathlib import Path
R=str(Path(__file__).resolve().parents[3]/'CompanyGame').replace('\\','/');OUT=R+'/Temp/BoundaryAudit';os.makedirs(OUT,exist_ok=True)
targets=[('Bedroom', 'ChestOfDrawers'), ('Bedroom', 'StorageShelf'), ('Bedroom', 'Vanity'), ('Essentials', 'FlowerVase'), ('Essentials', 'PlantLarge'), ('Essentials', 'PlantSmall'), ('Essentials', 'PlantTrailing'), ('Essentials', 'ShoeCabinet'), ('HomeOffice', 'OfficeChair'), ('Kitchen', 'DiningChair'), ('Kitchen', 'DishRack'), ('Kitchen', 'MicrowaveCabinet'), ('Kitchen', 'Stool'), ('Kitchen', 'WallCabinet'), ('LivingRoom', 'Armchair'), ('LivingRoom', 'CoffeeTableDecorated'), ('LivingRoom', 'Plant'), ('LivingRoom', 'SideTableDecorated'), ('LivingRoom', 'SwivelChair'), ('LivingRoom', 'TVSet')]
for pack,key in targets:
 bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=R+'/Assets/Art/Items/Furniture/Collections/'+pack+'/'+key+'.fbx')
 obj=next(o for o in bpy.context.scene.objects if o.type=='MESH');bpy.context.view_layer.objects.active=obj;bpy.ops.object.transform_apply(location=True,rotation=True,scale=True);m=obj.data
 yaw={('LivingRoom','Armchair'):-20,('Kitchen','DiningChair'):-10,('Bedroom','ChestOfDrawers'):70}.get((pack,key),0)
 rot=Matrix.Rotation(math.radians(-yaw),3,'Z');verts=np.array([tuple(rot@v.co) for v in m.vertices]);lo=verts.min(0);size=np.ptp(verts,axis=0);fc=np.array([tuple(rot@f.center) for f in m.polygons]);nn=np.array([tuple(rot@f.normal) for f in m.polygons]);coord=(fc-lo)/size-np.array([.5,.5,0]);faces=np.array([list(f.vertices) for f in m.polygons]);ar=np.array([f.area for f in m.polygons]);m.materials.clear();mat=bpy.data.materials.new('Neutral');mat.diffuse_color=(.65,.6,.52,1);m.materials.append(mat)
 scene=bpy.context.scene;scene.render.engine='BLENDER_WORKBENCH';scene.display.shading.light='STUDIO';scene.display.shading.color_type='MATERIAL';scene.display.shading.show_cavity=True;scene.display.shading.cavity_type='BOTH';scene.display.shading.show_shadows=True;scene.display.shading.show_specular_highlight=False;scene.world=bpy.data.worlds.new('World');scene.world.color=(.15,.15,.15);scene.display.shading.background_type='WORLD';scene.view_settings.view_transform='Standard';scene.render.resolution_x=900;scene.render.resolution_y=900;scene.render.resolution_percentage=100
 bpy.ops.object.camera_add();cam=bpy.context.object;scene.camera=cam;cam.data.type='ORTHO';dims=obj.dimensions;center=Vector((0,0,dims.z*.5));span=max(dims);cam.data.ortho_scale=span*1.35
 arrays=dict(vertices=verts,faces=faces,coords=coord,normals=nn,areas=ar,centers=fc,size=size,lo=lo)
 for view,offset in [('Front',(0,-3,.45)),('Angle',(1.3,-2,1.2))]:
  cam.location=center+Vector(offset)*span;cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler();scene.render.filepath=OUT+'/'+pack+'_'+key+'_'+view+'.png';bpy.context.view_layer.update();bpy.ops.render.render(write_still=True)
  arrays[view]=np.array([tuple(world_to_camera_view(scene,cam,obj.matrix_world@f.center)) for f in m.polygons])
 np.savez_compressed(OUT+'/'+pack+'_'+key+'.npz',**arrays);print('DONE',pack,key,flush=True)
