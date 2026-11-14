import bpy,math,json,bmesh,hashlib,sys
from pathlib import Path
from mathutils import Vector
P=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(P/'LayeredBob_FitReview.blend'))
s=bpy.context.scene;s.render.engine='CYCLES';s.cycles.samples=14;s.cycles.use_denoising=True;s.view_settings.view_transform='AgX';s.view_settings.look='AgX - Medium High Contrast';s.view_settings.exposure=0;s.view_settings.gamma=1
s.world.use_nodes=True;s.world.node_tree.nodes['Background'].inputs[0].default_value=(.30,.33,.39,1);s.world.node_tree.nodes['Background'].inputs[1].default_value=.65
for oldlight in [o for o in bpy.data.objects if o.type=='LIGHT']:bpy.data.objects.remove(oldlight,do_unlink=True)
for name,loc,power,size in [('LeftKey',(-2,-3,3.5),125,3),('RightKey',(2,-3,3.5),125,3),('Rim',(0,2,3),150,2)]:
 bpy.ops.object.light_add(type='AREA',location=loc);o=bpy.context.object;o.name=name;o.data.energy=power;o.data.shape='DISK';o.data.size=size;o.rotation_euler=(Vector((0,0,1.6))-o.location).to_track_quat('-Z','Y').to_euler()
s.render.resolution_x=760;s.render.resolution_y=760
for label in (['ThreeQuarter'] if '--quick' in sys.argv else ['Front','ThreeQuarter','Side','Back']):
 s.camera=bpy.data.objects['Review_'+label];s.render.filepath=str(P/('LayeredBob_'+label+'.png'));bpy.ops.render.render(write_still=True)
s.camera=bpy.data.objects['Review_ThreeQuarter']
for area in bpy.context.screen.areas:
 if area.type=='VIEW_3D':
  area.spaces.active.shading.type='RENDERED';area.spaces.active.overlay.show_overlays=False;area.spaces.active.region_3d.view_perspective='CAMERA'
bpy.ops.wm.save_as_mainfile(filepath=str(P/'LayeredBob_FitReview.blend'))
if '--quick' in sys.argv:raise SystemExit(0)
# Static turnaround is a real render of the exact same body/hair meshes, not image generation.
body=bpy.data.objects['FemaleBody'];coll=bpy.data.collections['Bob - continuous sculpt'];hair=[o for o in coll.objects if o.type=='MESH'];deps=bpy.context.evaluated_depsgraph_get()
for i,angle in enumerate([0,-math.pi/2,math.pi]):
 root=bpy.data.objects.new('Turnaround '+str(i),None);s.collection.objects.link(root)
 for original in [body]+hair:
  me=bpy.data.meshes.new_from_object(original.evaluated_get(deps),depsgraph=deps);ob=bpy.data.objects.new(original.name+' review '+str(i),me);s.collection.objects.link(ob);ob.matrix_world=original.matrix_world.copy();ob.parent=root
 root.rotation_euler.z=angle;root.location.x=(i-1)*.69
for ob in [body]+hair:ob.hide_render=True
bpy.ops.object.camera_add(location=(0,-5,1.655));c=bpy.context.object;c.rotation_euler=(Vector((0,0,1.64))-c.location).to_track_quat('-Z','Y').to_euler();c.data.type='ORTHO';c.data.ortho_scale=2.10;s.camera=c;s.render.resolution_x=1800;s.render.resolution_y=570;s.render.filepath=str(P/'LayeredBob_Review.png');bpy.ops.render.render(write_still=True)
print('ALL_RENDERS_COMPLETE',flush=True)

