import bpy, json, os
from mathutils import Vector
base=os.path.dirname(os.path.abspath(__file__))
bpy.ops.wm.open_mainfile(filepath=os.path.join(base,'Supplied','Library_V4.blend'))
out=os.path.join(base,'Export'); os.makedirs(out,exist_ok=True)
data={'objects':[], 'materials':[]}
for o in bpy.data.objects:
    data['objects'].append({'name':o.name,'type':o.type,'parent':o.parent.name if o.parent else None,'position':list(o.matrix_world.translation),'rotation':list(o.matrix_world.to_euler()),'dimensions':list(o.dimensions),'properties':{k:str(v) for k,v in o.items()}})
for m in bpy.data.materials:
    p=next((n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED'),None) if m.use_nodes else None
    data['materials'].append({'name':m.name,'color':list(p.inputs['Base Color'].default_value) if p else list(m.diffuse_color),'metallic':p.inputs['Metallic'].default_value if p else 0,'roughness':p.inputs['Roughness'].default_value if p else .6,'alpha':p.inputs['Alpha'].default_value if p else 1,'transmission':p.inputs['Transmission Weight'].default_value if p else 0,'images':[n.image.name for n in m.node_tree.nodes if n.type=='TEX_IMAGE' and n.image] if m.use_nodes else []})
with open(os.path.join(out,'inventory.json'),'w',encoding='utf8') as f: json.dump(data,f,ensure_ascii=False,indent=2)
bpy.ops.export_scene.fbx(filepath=os.path.join(out,'Library_V4.fbx'),use_selection=False,use_visible=False,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',global_scale=1,apply_unit_scale=True,use_mesh_modifiers=True,use_custom_props=True,add_leaf_bones=False,bake_anim=False)
print('V4_EXPORT_OK',len(data['objects']),len(data['materials']))
