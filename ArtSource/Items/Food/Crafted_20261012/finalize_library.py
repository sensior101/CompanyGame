"""Frame the Blender library for inspection; individual exports stay at origin."""
import bpy,json
from mathutils import Vector
from pathlib import Path
ROOT=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'CraftedFood_134.blend'))
items=json.loads((ROOT/'catalog.json').read_text(encoding='utf8'))['items']
row=0;categories=list(dict.fromkeys(x['category'] for x in items))
for category in categories:
    group=[x for x in items if x['category']==category]
    for i,item in enumerate(group):
        obj=bpy.data.objects['Food_'+item['id']]
        obj.location=((i%10)*.43,(row+i//10)*.43,0)
    row+=(len(group)+9)//10+1
center=Vector((9*.43/2,(row-2)*.43/2,.07))
cam=bpy.context.scene.camera;scale=14
cam.location=center+Vector((.10*scale,-.4*scale,1.0*scale));cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=row*.43*1.13
for obj in bpy.data.collections['Studio'].objects:
    if obj.type=='LIGHT':obj.location=obj.location*scale+center;obj.data.energy*=scale*scale;obj.data.size*=scale;obj.rotation_euler=(center-obj.location).to_track_quat('-Z','Y').to_euler()
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            space=area.spaces.active;space.region_3d.view_location=center;space.region_3d.view_distance=row*.43;space.region_3d.view_rotation=cam.rotation_euler.to_quaternion();space.region_3d.view_perspective='ORTHO'
            space.shading.type='SOLID';space.shading.color_type='MATERIAL';space.shading.show_cavity=True;space.overlay.show_floor=False
bpy.ops.object.select_all(action='DESELECT')
bpy.context.scene.render.resolution_x=1800;bpy.context.scene.render.resolution_y=2400;bpy.context.scene.render.filepath=str(ROOT/'Previews/Library.png')
bpy.context.preferences.filepaths.save_version=0
bpy.data.orphans_purge(do_recursive=True)
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'CraftedFood_134.blend'))
bpy.ops.render.render(write_still=True)
print('LIBRARY_FINALIZED',len(items),flush=True)
