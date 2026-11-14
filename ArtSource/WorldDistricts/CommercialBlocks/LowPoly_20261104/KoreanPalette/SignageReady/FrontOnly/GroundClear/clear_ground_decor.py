"""Remove ground-floor planting components only; preserve upper planting and architecture.

Blender --background --python-exit-code 1 --python clear_ground_decor.py -- inspect
Blender --background --python-exit-code 1 --python clear_ground_decor.py -- 1 2 3 4 5 6 7 8
"""
import bpy,json,sys,copy,ast,hashlib,shutil
from pathlib import Path
sys.dont_write_bytecode=True
OUT=Path(__file__).resolve().parent;SOURCE=OUT.parent
for folder in ('Models','Exports','Previews','Catalog','QA'):(OUT/folder).mkdir(exist_ok=True)

def reuse(path,names):
    tree=ast.parse(path.read_text(encoding='utf8'))
    nodes=[n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name in names]
    exec(compile(ast.Module(body=nodes,type_ignores=[]),str(path),'exec'),globals())

reuse(SOURCE.parents[1]/'recolor_existing.py',{'descendant','asset_objects','fingerprint','combined'})
reuse(SOURCE.parent/'retrofit_signage.py',{'bounds'})
reuse(SOURCE/'build_front_only.py',{'material_specs','export','render'})

def components(ob):
    mesh=ob.data;parent=list(range(len(mesh.vertices)))
    def find(x):
        while parent[x]!=x:parent[x]=parent[parent[x]];x=parent[x]
        return x
    for edge in mesh.edges:
        a,b=(find(i) for i in edge.vertices);parent[b]=a
    groups={}
    for v in mesh.vertices:groups.setdefault(find(v.index),[]).append(v.index)
    records=[]
    for ids in groups.values():
        members=set(ids);polys=[p for p in mesh.polygons if p.vertices[0] in members]
        coords=[ob.matrix_world@mesh.vertices[i].co for i in ids]
        payload=[]
        for poly in polys:
            payload.append({'vertices':[list(mesh.vertices[i].co) for i in poly.vertices],
                'materialIndex':poly.material_index,'material':mesh.materials[poly.material_index].name,
                'smooth':poly.use_smooth,
                'uv':{layer.name:[list(layer.data[li].uv) for li in poly.loop_indices] for layer in mesh.uv_layers}})
        digest=hashlib.sha256(json.dumps(payload,sort_keys=True,separators=(',',':')).encode()).hexdigest()
        limits={'min':[min(v[a] for v in coords) for a in range(3)],'max':[max(v[a] for v in coords) for a in range(3)]}
        records.append({'object':ob.name,'vertices':ids,'polygonIndices':[p.index for p in polys],
            'triangles':sum(len(p.vertices)-2 for p in polys),'bounds':limits,
            'materials':sorted({mesh.materials[p.material_index].name for p in polys}),
            'geometryUVMaterialSHA256':digest,'ground':limits['max'][2]<3.95})
    return records

def planting_meshes(root):
    group=bpy.data.objects.get('Planting')
    return [o for o in asset_objects(root) if o.type=='MESH' and group and descendant(o,group)]

def nonplanting(root):
    excluded={o.name for o in asset_objects(bpy.data.objects['Planting'])}
    hashes={k:v for k,v in fingerprint(root).items() if k not in excluded}
    mats={o.name:[(m.name,list(m.diffuse_color),list(m.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value)) for m in o.data.materials] for o in asset_objects(root) if o.type=='MESH' and o.name not in excluded}
    return {'geometry':hashes,'materialColors':mats}

def keep_components(ob,records):
    original=ob.data;ids=sorted({i for record in records for i in record['vertices']})
    if not ids:
        bpy.data.objects.remove(ob,do_unlink=True)
        return
    polygon_ids=sorted({i for record in records for i in record['polygonIndices']})
    mapping={old:new for new,old in enumerate(ids)}
    surviving=[original.polygons[i] for i in polygon_ids]
    new=bpy.data.meshes.new(original.name+'_GroundClear')
    new.from_pydata([tuple(original.vertices[i].co) for i in ids],[],[[mapping[i] for i in poly.vertices] for poly in surviving])
    for material in original.materials:new.materials.append(material)
    for source,target in zip(surviving,new.polygons):
        target.material_index=source.material_index;target.use_smooth=source.use_smooth
    for layer in original.uv_layers:
        target=new.uv_layers.new(name=layer.name)
        for source_poly,new_poly in zip(surviving,new.polygons):
            for source_li,target_li in zip(source_poly.loop_indices,new_poly.loop_indices):target.data[target_li].uv=layer.data[source_li].uv
    new.update();ob.data=new

def source_rows():return json.loads((SOURCE/'manifest.json').read_text(encoding='utf8'))['items']

def inventory():
    rows=[]
    for old in source_rows():
        bpy.ops.wm.open_mainfile(filepath=str(SOURCE/old['blend']));bpy.context.view_layer.update()
        root=bpy.data.objects[old['id']]
        records=[c for ob in planting_meshes(root) for c in components(ob)]
        low=[r for r in records if r['ground']];upper=[r for r in records if not r['ground']]
        row={'id':old['id'],'groundComponents':low,'upperComponents':upper,
            'groundTriangles':sum(c['triangles'] for c in low),'upperTriangles':sum(c['triangles'] for c in upper),
            'groundPlanterCount':2,'otherGroundDecoration':'None found; ground entries, threshold, plinth, frames and signs are architectural and preserved.'}
        assert len(low)==6 and row['groundTriangles']==88,'Unexpected decoration inventory'
        rows.append(row)
        print('INVENTORY',old['id'],'ground',len(low),row['groundTriangles'],'upper',len(upper),row['upperTriangles'],flush=True)
    (OUT/'QA/DecorationInventory.json').write_text(json.dumps({'items':rows},ensure_ascii=False,indent=2),encoding='utf8')

def save_catalog(row=None):
    path=OUT/'manifest.json'
    manifest=json.loads(path.read_text(encoding='utf8')) if path.exists() else {'schemaVersion':1,'items':[],'accessories':[]}
    if row:manifest['items']=sorted([r for r in manifest['items'] if r['id']!=row['id']]+[row],key=lambda r:r['id'])
    old=json.loads((SOURCE/'manifest.json').read_text(encoding='utf8'))
    manifest['accessories']=copy.deepcopy(old['accessories'])
    for accessory in manifest['accessories']:
        accessory['copiedUnchangedFrom']='../'+accessory['blend']
        accessory['sourceApprovedBlend']='../'+accessory['blend']
        hashes={}
        for field in ('blend','fbx','glb','preview','validationReport'):
            source=SOURCE/accessory[field];target=OUT/accessory[field]
            target.parent.mkdir(exist_ok=True,parents=True)
            if not target.exists():shutil.copyfile(source,target)
            assert source.read_bytes()==target.read_bytes(),'Accessory copy changed'
            hashes[field]=hashlib.sha256(target.read_bytes()).hexdigest()
        accessory['unchangedBinarySHA256']=hashes
    manifest['style']='Front-only commercial buildings; clear ground-floor decoration; upper planting preserved'
    path.write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf8')
    catalog=copy.deepcopy(manifest)
    for item in catalog['items']+catalog['accessories']:
        for field in ('blend','fbx','glb','preview','sourceApprovedBlend','sourceDesign','componentPreservationReport','validationReport','copiedUnchangedFrom'):
            if field in item:item[field]='../'+item[field]
    (OUT/'Catalog/catalog.json').write_text(json.dumps(catalog,ensure_ascii=False,indent=2),encoding='utf8')

def clear(number):
    old=next(r for r in source_rows() if r['id']==f'CB_{number:02d}')
    source=SOURCE/old['blend'];ident=old['id']
    bpy.ops.wm.open_mainfile(filepath=str(source));bpy.context.view_layer.update();root=bpy.data.objects[ident]
    before=nonplanting(root);removed=[];preserved=[];matrices={}
    for ob in planting_meshes(root):
        records=components(ob);low=[r for r in records if r['ground']];upper=[r for r in records if not r['ground']]
        assert all(r['bounds']['max'][2]<1.5 for r in low),'Ground component taller than inspected props'
        removed+=low;preserved+=upper
        if upper:matrices[ob.name]=[list(r) for r in ob.matrix_world]
        keep_components(ob,upper)
    assert len(removed)==6 and sum(r['triangles'] for r in removed)==88
    bpy.context.view_layer.update()
    after_upper=[c for ob in planting_meshes(root) for c in components(ob)]
    assert sorted(r['geometryUVMaterialSHA256'] for r in preserved)==sorted(r['geometryUVMaterialSHA256'] for r in after_upper)
    assert before==nonplanting(root),'Architecture, door, sign or color changed'
    assert all([list(r) for r in bpy.data.objects[name].matrix_world]==matrix for name,matrix in matrices.items())
    meshes=[ob for ob in asset_objects(root) if ob.type=='MESH'];triangles=sum(len(p.vertices)-2 for ob in meshes for p in ob.data.polygons)
    assert triangles==old['triangles']-88
    row=copy.deepcopy(old)
    for field in ('nonSignageGeometryUnchanged','nonSignageFingerprintReport','frontPreview','signageModified'):row.pop(field,None)
    stem=ident+'_GroundClear'
    row.update(revision=8,variant='GroundClear',groundDecorationRemoved=True,upperDecorationPreserved=True,
       architectureDoorsSignsUnchanged=True,sourceApprovedBlend='../'+old['blend'],previousRevisionTriangles=old['triangles'],
       triangles=triangles,removedGroundDecorationTriangles=88,removedGroundPlanters=2,meshObjects=len(meshes),
       exportBounds=bounds(meshes),triangleReductionPercent=round(100*(1-triangles/old['originalDesignTriangles']),2),
       blend=f'Models/{stem}.blend',fbx=f'Exports/{stem}.fbx',glb=f'Exports/{stem}.glb',preview=f'Previews/{stem}.png',
       materials=material_specs(meshes),componentPreservationReport=f'QA/{ident}_ComponentPreservation.json')
    row['sourceDesign']='../'+old['sourceDesign']
    export(root,row)
    bpy.ops.wm.open_mainfile(filepath=str(OUT/row['blend']));bpy.context.view_layer.update();root=bpy.data.objects[ident]
    saved_upper=[c for ob in planting_meshes(root) for c in components(ob)]
    assert not any(r['ground'] for r in saved_upper)
    assert sorted(r['geometryUVMaterialSHA256'] for r in preserved)==sorted(r['geometryUVMaterialSHA256'] for r in saved_upper)
    assert before==nonplanting(root)
    assert all([list(r) for r in bpy.data.objects[name].matrix_world]==matrix for name,matrix in matrices.items())
    report={'id':ident,'pass':True,'sourceBlendSHA256':hashlib.sha256(source.read_bytes()).hexdigest(),
       'removedComponents':removed,'preservedUpperComponents':preserved,'savedUpperComponents':saved_upper,
       'preservedPlantingObjectTransforms':matrices,'nonPlantingBefore':before,'nonPlantingAfterSaved':nonplanting(root),
       'removedTriangles':88,'meshReconstruction':'Surviving vertices/faces copied in source order; original face material indices, flat flags, UV loop values and object transforms retained. Empty Planting group remains.'}
    (OUT/row['componentPreservationReport']).write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
    render(OUT/row['preview']);save_catalog(row)
    print('GROUND_CLEAR_COMPLETE',ident,'removed88','triangles',triangles,'upperPreserved',len(preserved),flush=True)

args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
if args==['inspect']:inventory()
else:
    for arg in (args or [str(i) for i in range(1,9)]):clear(int(arg))
