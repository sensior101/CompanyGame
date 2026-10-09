import bpy,json,os
from pathlib import Path
base=Path(__file__).resolve().parent
rows=[]
for j in json.loads((base/'jobs.json').read_text(encoding='utf8')):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    try:
        if j['input'].endswith('.blend'):bpy.ops.wm.open_mainfile(filepath=j['input'],load_ui=False)
        else:bpy.ops.import_scene.fbx(filepath=j['input'],use_image_search=True)
        objs=[o for o in bpy.context.scene.objects if o.type=='MESH']
        row=dict(j,meshes=[{'name':o.name,'vertices':len(o.data.vertices),'polygons':len(o.data.polygons),'location':list(o.matrix_world.translation),'dimensions':list(o.dimensions)} for o in objs],images=[{'name':i.name,'path':bpy.path.abspath(i.filepath),'packed':bool(i.packed_file)} for i in bpy.data.images])
        rows.append(row)
        print('INSPECT',j['id'],len(objs),flush=True)
    except Exception as e:rows.append(dict(j,error=str(e)))
    (base/'inspection.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2),encoding='utf8')
