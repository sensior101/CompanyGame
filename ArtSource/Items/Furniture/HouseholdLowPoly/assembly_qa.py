"""Validate sink cutout and render assembled kitchen + actual character scale.
Imports final GLB files, independent from the procedural construction recipe.
"""
import bpy,json
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
HERE=Path(__file__).resolve().parent
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene;scene.unit_settings.system='METRIC'
def load(id,offset=(0,0,0)):
 before=set(bpy.data.objects)
 bpy.ops.import_scene.gltf(filepath=str(HERE/'Exports'/id/(id+'_Cream.glb')))
 objects=[o for o in bpy.data.objects if o not in before]
 for o in objects:
  if o.parent not in objects:o.location+=Vector(offset)
 bpy.context.view_layer.update()
 return objects
base=load('SinkBase');sink=load('Sink',(0,0,.90))
sink_meshes=[o for o in sink if o.type=='MESH']
under=[o.matrix_world@v.co for o in sink_meshes for v in o.data.vertices if (o.matrix_world@v.co).z<.89999]
for o in sink_meshes:
 for edge in o.data.edges:
  a,b=[o.matrix_world@o.data.vertices[i].co for i in edge.vertices]
  for z in (.899999,.88,.86):
   if min(a.z,b.z)<z<max(a.z,b.z):under.append(a+(b-a)*((z-a.z)/(b.z-a.z)))
assert under and all(abs(v.x)<.29 and abs(v.y)<.205 for v in under),'Sink bowl does not fit cutout'
side_clearance=min(.29-max(abs(v.x) for v in under),.205-max(abs(v.y) for v in under))
counter=[o for o in base if o.type=='MESH' and 'CutoutCounter' in o.name]
assert len(counter)==4,'Expected four solid counter rails around the hole'
verts=[];faces=[]
for o in counter:
 start=len(verts);verts.extend([o.matrix_world@v.co for v in o.data.vertices]);faces.extend([tuple(start+i for i in p.vertices) for p in o.data.polygons])
bvh=BVHTree.FromPolygons(verts,faces)
for x in (-.27,0,.27):
 for y in (-.19,0,.19):
  hit=bvh.ray_cast(Vector((x,y,1.0)),Vector((0,0,-1)),.20)
  assert hit[0] is None,('Countertop blocks hole',x,y)
load('KitchenBase',(-.83,0,0));load('Induction',(-.83,0,.90))
load('KitchenUpper',(.0,.13,1.45));load('RangeHood',(-.83,.05,1.40))
load('Fridge',( .90,.0,0))
report={'sink_insertion_z':.90,'sink_below_counter_min_clearance_m':side_clearance,'counter_hole_rays_passed':9,'source':'Final GLB exports','UnityPlayModeVerified':False}
(HERE/'QA/AssemblyReport.json').write_text(json.dumps(report,indent=2),encoding='utf8')

def mat(name,color):
 m=bpy.data.materials.new(name);m.diffuse_color=(*color,1);m.use_nodes=True;m.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(*color,1);return m
def block(name,size,loc,material):
 bpy.ops.mesh.primitive_cube_add(size=1,location=loc);o=bpy.context.object;o.name=name;o.scale=size;o.data.materials.append(material)
block('PresentationFloor',(200,200,.04),(0,0,-.022),mat('Floor',(.52,.50,.46)))
block('PresentationWall',(4.5,.05,2.5),(0,.42,1.25),mat('Wall',(.72,.70,.66)))
world=bpy.data.worlds.new('World');world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.4,.4,.4,1);scene.world=world
for loc,power,size in [((-3,-4,5),900,4),((4,-2,4),650,3)]:
 d=bpy.data.lights.new('Softbox','AREA');d.energy=power;d.size=size;o=bpy.data.objects.new('Softbox',d);scene.collection.objects.link(o);o.location=loc;o.rotation_euler=(Vector((0,0,1))-o.location).to_track_quat('-Z','Y').to_euler()
camera=bpy.data.objects.new('Camera',bpy.data.cameras.new('Camera'));scene.collection.objects.link(camera);scene.camera=camera;camera.data.type='ORTHO'
camera.location=(3,-5,3.4);camera.rotation_euler=(Vector((0,0,1.1))-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=4.3
scene.render.engine='CYCLES';scene.cycles.samples=24;scene.cycles.use_denoising=True
p=bpy.context.preferences.addons['cycles'].preferences;p.compute_device_type='OPTIX';p.get_devices()
for d in p.devices:d.use=d.type=='OPTIX'
scene.cycles.device='GPU';scene.render.threads_mode='FIXED';scene.render.threads=4
scene.render.resolution_x=1400;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
scene.view_settings.view_transform='AgX';scene.render.image_settings.file_format='PNG'
scene.render.filepath=str(HERE/'QA/KitchenAssembly.png');bpy.ops.render.render(write_still=True)
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'QA/KitchenAssembly.blend'))
print('ASSEMBLY_QA_PASS',json.dumps(report),flush=True)
