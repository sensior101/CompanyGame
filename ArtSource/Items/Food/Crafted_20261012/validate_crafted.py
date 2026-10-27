"""Blender validation of authored meshes and independent FBX/GLB round trips."""
import bpy, json, math, hashlib, struct, statistics, sys, importlib.util
from io_scene_fbx import parse_fbx as fbx_parse
from pathlib import Path
ROOT=Path(__file__).resolve().parent
catalog=json.loads((ROOT/'catalog.json').read_text(encoding='utf8'))
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'CraftedFood_134.blend'))
errors=[]; details=[];expected={};groups={}
for item in catalog['items']:
    obj=bpy.data.objects['Food_'+item['id']];mesh=obj.data
    coordinates=[tuple(v.co) for v in mesh.vertices]
    dimensions=[max(v[k] for v in coordinates)-min(v[k] for v in coordinates) for k in range(3)]
    expected[obj.name]=(len(mesh.polygons),dimensions)
    if not mesh.uv_layers:problems_uv=True
    else:problems_uv=False
    problems=[]
    if problems_uv:problems.append('missing UV')
    image_count=0
    for slot in obj.material_slots:
        for node in slot.material.node_tree.nodes:
            if node.type=='TEX_IMAGE':
                image_count+=1
                if node.image is None or not node.image.packed_file:problems.append('unpacked source texture')
    if not all(math.isfinite(c) for v in coordinates for c in v):problems.append('non-finite position')
    if min(dimensions)<=0:problems.append('zero size')
    if min(v[2] for v in coordinates)<-1e-7:problems.append('below origin')
    if any(len(p.vertices)!=3 for p in mesh.polygons):problems.append('non-triangle')
    degenerate=sum(p.area<1e-14 for p in mesh.polygons)
    if degenerate:problems.append('degenerate faces: '+str(degenerate))
    for slot in obj.material_slots:
        bsdf=next(n for n in slot.material.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
        if bsdf.inputs['Roughness'].default_value<.87:problems.append('non-matte material')
        if bsdf.inputs['Metallic'].default_value!=0:problems.append('metallic material')
    if item['family'] in ('pasta','latte','bingsu','softserve'):groups.setdefault(item['family'],[]).append(obj)
    details.append({'id':item['id'],'triangles':len(mesh.polygons),'dimensionsMeters':dimensions,'degenerateFaces':degenerate,'textureNodes':image_count,'uvLayers':len(mesh.uv_layers)})
    errors.extend(obj.name+': '+p for p in problems)
sharing={}
for family,objects in groups.items():
    same=len({o.data.as_pointer() for o in objects})==1
    color_sets={tuple(tuple(slot.material.diffuse_color) for slot in o.material_slots) for o in objects}
    sharing[family]={'menus':len(objects),'sameMeshDatablock':same,'distinctColorSets':len(color_sets)}
    if not same or len(color_sets)!=len(objects):errors.append(f'{family}: sharing/color validation')
views=[]
if '--views' in sys.argv:
    spec=importlib.util.spec_from_file_location('food_builder',ROOT/'build_crafted.py');builder=importlib.util.module_from_spec(spec);spec.loader.exec_module(builder)
    target_ids=['PorkCutlet','DimSum','Kebab','Naan','Water','GreenTea','Pretzel','StrawberryBingsu','VanillaSoftServe','StrawberryLatte','SpringRolls','Samgyetang']
    if '--view-id' in sys.argv:target_ids=[sys.argv[sys.argv.index('--view-id')+1]]
    if '--view-ids' in sys.argv:target_ids=sys.argv[sys.argv.index('--view-ids')+1].split(',')
    prior=ROOT/'QA/views.json'
    if prior.exists():views=[x for x in json.loads(prior.read_text(encoding='utf8')) if x['id'] not in target_ids]
    for o in bpy.data.objects:
        if o.name.startswith('Food_'):o.hide_render=True
    folder=ROOT/'QA/Views';folder.mkdir(exist_ok=True)
    for name in target_ids:
        o=bpy.data.objects['Food_'+name];position=o.location.copy();o.location=(0,0,0);o.hide_render=False
        for label,angle in [('side',math.pi/2),('back',math.pi)]:
            file=folder/(name+'_'+label+'.png');builder.render_item(o,file,angle);views.append({'id':name,'angle':label,'render':str(file.relative_to(ROOT)).replace('\\','/')})
        o.hide_render=True;o.location=position
    (ROOT/'QA/views.json').write_text(json.dumps(views,indent=2),encoding='utf8')
for obj in list(bpy.data.objects):bpy.data.objects.remove(obj,do_unlink=True)
roundtrips=[];portable=[]
def contents(elem):
    result=[]
    if elem.id==b'Content' and elem.props and isinstance(elem.props[0],bytes):result.append(len(elem.props[0]))
    for child in elem.elems:result.extend(contents(child))
    return result
for item in catalog['items']:
    name='Food_'+item['id'];tris,dimensions=expected[name]
    for fmt in ('glb','fbx'):
        path=ROOT/item[fmt]
        if not path.exists():errors.append('Missing '+str(path));continue
        if fmt=='glb':
            raw=path.read_bytes();length=struct.unpack_from('<I',raw,12)[0];doc=json.loads(raw[20:20+length]);images=doc.get('images',[])
            embedded=all('bufferView' in im and 'uri' not in im for im in images)
            portable.append({'id':item['id'],'format':fmt,'embeddedImages':len(images),'passed':embedded})
            bpy.ops.import_scene.gltf(filepath=str(path))
        else:
            tree,_=fbx_parse.parse(str(path));sizes=contents(tree);embedded=all(sz>0 for sz in sizes)
            portable.append({'id':item['id'],'format':fmt,'embeddedImages':len(sizes),'passed':embedded})
            bpy.ops.import_scene.fbx(filepath=str(path),use_anim=False)
        if not embedded:errors.append(name+'.'+fmt+': non-portable texture')
        meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
        bpy.context.view_layer.update()
        actual=sum(len(o.data.polygons) for o in meshes)
        valid=len(meshes)==1 and actual==tris
        dim_error=max(abs(meshes[0].dimensions[i]-dimensions[i]) for i in range(3)) if meshes else 999
        if dim_error>1e-5:valid=False
        if not valid:errors.append(f'{name}.{fmt}: mesh={len(meshes)} triangles={actual}/{tris} dimension error={dim_error}')
        roundtrips.append({'id':item['id'],'format':fmt,'passed':valid,'dimensionErrorMeters':dim_error})
        for obj in list(bpy.data.objects):bpy.data.objects.remove(obj,do_unlink=True)
        bpy.data.orphans_purge(do_recursive=True)
    print('ROUNDTRIP',name,flush=True)
triangles=[d['triangles'] for d in details]
report={'status':'PASS' if not errors else 'FAIL','menuCount':len(details),'uniqueGeometry':catalog['uniqueGeometry'],'triangles':{'min':min(triangles),'max':max(triangles),'median':statistics.median(triangles),'total':sum(triangles)},'sharedFamilies':sharing,'exportRoundTrips':len(roundtrips),'passedRoundTrips':sum(x['passed'] for x in roundtrips),'errors':errors,'meshes':details,'roundTrips':roundtrips,'texturePortability':portable}
(ROOT/'QA/validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
print('VALIDATION_COMPLETE',report['status'],report['passedRoundTrips'],len(errors),flush=True)
if errors:raise RuntimeError('\n'.join(errors))
