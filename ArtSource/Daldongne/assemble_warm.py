from pathlib import Path
import ast,json
p=Path(__file__).parent
base=(p/'build_map.py').read_text(encoding='utf-8')
source='import bpy, math, random\nfrom mathutils import Vector\nM={m.name:m for m in bpy.data.materials}\n'+base[base.index('cache={}'):base.index('def build():')]
for f in ('props.py','warm_environment.py','warm_landmarks.py','warm_routes.py'):source+='\n'+(p/f).read_text(encoding='utf-8')
source+='\nsetup_warm_palette()\nreplace_landmarks()\ndress_neighborhood()\nadd_warm_street_life()\nroute_cuts=build_clear_routes()\nbpy.context.view_layer.update()\nremoved_props=clear_route_props()\nwarm_lighting()\n'
source+='result={"objects":len(bpy.data.objects),"route_cuts":route_cuts,"stairs":len(ROUTES),"flat_connections":len(FLATS),"removed_obstructions":removed_props,"triangles":sum(len(p.vertices)-2 for o in bpy.data.objects if o.type=="MESH" for p in o.data.polygons)}\n'
source+='\n'+(p/'warm_finish.py').read_text(encoding='utf-8')
source+='\n'+(p/'warm_edge_clearance.py').read_text(encoding='utf-8')
(p/'DaldongneWarm_Edit.py').write_text(source,encoding='utf-8')
reset='import bpy\nfor o in list(bpy.data.objects): bpy.data.objects.remove(o,do_unlink=True)\nfor m in list(bpy.data.materials): bpy.data.materials.remove(m)\n'
(p/'DaldongneWarm_FullSource.py').write_text(reset+(p/'DaldongneTown_Source.py').read_text(encoding='utf-8')+'\n'+source,encoding='utf-8')
tree=ast.parse((p/'warm_routes.py').read_text(encoding='utf-8'));routes=[]
for node in tree.body:
    if isinstance(node,ast.Assign) and node.targets[0].id in ('ROUTES','FLATS'):
        for n,a,b,w in ast.literal_eval(node.value):routes.append({'name':n,'a':a,'b':b,'width':w,'kind':'stair' if node.targets[0].id=='ROUTES' else 'flat'})
(p/'warm_routes.json').write_text(json.dumps({'radius':.35,'height':1.8,'routes':routes},indent=2),encoding='utf-8')
print(f'Assembled {len(source)} characters and {len(routes)} routes.')
