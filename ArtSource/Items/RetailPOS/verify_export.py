"""Read-only FBX round-trip check in a fresh Blender process."""
import bpy,json
from pathlib import Path
base=Path(__file__).resolve().parent
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=str(base/'Exports/RetailPOS.fbx'),anim_offset=0)
scene=bpy.context.scene;scene.render.fps=30
screens={}
for name in ['Screen_Main','Screen_Customer','Screen_Card']:
    obj=bpy.data.objects.get(name);assert obj and obj.type=='MESH',name
    assert len(obj.data.uv_layers)>0
    values={tuple(round(v,4) for v in u.uv) for u in obj.data.uv_layers[0].data}
    assert values=={(0,0),(1,0),(1,1),(0,1)},values
    assert obj.data.materials and obj.data.materials[0].name.startswith('POS_Screen_')
    screens[name]={'material':obj.data.materials[0].name,'uvFullImage':True}
drawer=bpy.data.objects.get('CashDrawerSlide');assert drawer and drawer.animation_data and drawer.animation_data.action
positions=[]
for f in (1,31,61,91):
    scene.frame_set(f);bpy.context.view_layer.update();positions.append(list(drawer.matrix_world.translation))
assert abs(positions[1][1]-positions[0][1]+.24)<.0001,positions
assert max(abs(a-b) for a,b in zip(positions[0],positions[-1]))<.0001
assert max(abs(a-b) for a,b in zip(positions[1],positions[2]))<.0001
assert not [o for o in bpy.data.objects if o.type in ['CAMERA','LIGHT']]
assert not any(o.name.startswith('Studio_') for o in bpy.data.objects)
for name in ['CashDrawerHousing_Mesh','CashDrawerSlide_Mesh','MainMonitor_Mesh']:
    assert bpy.data.objects.get(name),name
report={'pass':True,'screens':screens,'drawerPositions':positions,'drawerTravelMetres':.24,'studioExcluded':True,'meshCount':sum(o.type=='MESH' for o in bpy.data.objects),'animationActions':len(bpy.data.actions)}
qa=base/'QA';qa.mkdir(exist_ok=True);(qa/'ExportCheck.json').write_text(json.dumps(report,indent=2),encoding='utf8')
print('RETAIL_POS_FBX_VERIFIED',json.dumps(report),flush=True)
