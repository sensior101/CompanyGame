"""Rebuild selected menu IDs in the existing library without touching other model files."""
import bpy,sys,json,importlib.util
from pathlib import Path
ROOT=Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location('food_builder',ROOT/'build_crafted.py');builder=importlib.util.module_from_spec(spec);spec.loader.exec_module(builder)
ids=set(sys.argv[sys.argv.index('--')+1:])
items=[x for x in builder.MENU if x['id'] in ids]
families={x['builder'] for x in items if x['builder'] in builder.SHARED}
items=[x for x in builder.MENU if x['id'] in ids or x['builder'] in families]
assert items,'Pass at least one exact menu ID'
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'CraftedFood_134.blend'))
catalog=json.loads((ROOT/'catalog.json').read_text(encoding='utf8'));rows={x['id']:x for x in catalog['items']}
for obj in list(bpy.data.collections['Studio'].objects):bpy.data.objects.remove(obj,do_unlink=True)
bpy.data.collections.remove(bpy.data.collections['Studio']);builder.setup_studio()
for obj in bpy.data.objects:
    if obj.name.startswith('Food_'):obj.hide_render=True
cache={}
for item in items:
    name='Food_'+item['id'];old=bpy.data.objects[name];position=old.location.copy();bpy.data.objects.remove(old,do_unlink=True)
    family=item['builder'] if item['builder'] in builder.SHARED else item['id']
    if family not in cache:
        geo=builder.Geo();getattr(builder,'build_'+item['builder'])(geo,item['style']);builder.finish_food(geo,item)
        scale=.105 if item['builder'] in ['latte','drink','smoothie','ade','softserve'] else (.12 if item['builder']=='tea' else .13)
        cache[family]=builder.mesh_from_geo(geo,'Shared_'+family,scale)
    mesh,keys=cache[family];obj=bpy.data.objects.new(name,mesh);bpy.data.collections[item['category']].objects.link(obj)
    obj['menu_name']=item['name'];obj['mesh_family']=family;obj['recipe_reference']=item['recipe']
    f,fd=builder.flavor_colors(item)
    for slot,key in zip(obj.material_slots,keys):slot.link='OBJECT';slot.material=builder.material(key,f if key=='flavor' else fd if key=='flavordark' else None)
    row=rows[item['id']];row.update(vertices=len(mesh.vertices),triangles=len(mesh.polygons),geometryHash=builder.geometry_hash(mesh),materialSlots=len(keys))
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
    bpy.ops.export_scene.gltf(filepath=str(ROOT/row['glb']),export_format='GLB',use_selection=True,export_yup=True,export_apply=False)
    bpy.ops.export_scene.fbx(filepath=str(ROOT/row['fbx']),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,add_leaf_bones=False,bake_anim=False,path_mode='COPY',embed_textures=True)
    builder.render_item(obj,ROOT/row['render']);obj.hide_render=True;obj.location=position
for obj in bpy.data.objects:
    if obj.name.startswith('Food_'):obj.hide_render=False
bpy.context.preferences.filepaths.save_version=0
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'CraftedFood_134.blend'))
(ROOT/'catalog.json').write_text(json.dumps(catalog,ensure_ascii=False,indent=2),encoding='utf8')
print('REFRESH_COMPLETE',len(items),flush=True)
