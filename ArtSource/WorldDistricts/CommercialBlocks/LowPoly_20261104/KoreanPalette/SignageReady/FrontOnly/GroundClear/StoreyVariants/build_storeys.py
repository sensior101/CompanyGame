"""Add 3.2 m storeys to approved models through connected-component editing.

The approved second floor is repeated; the authored top floor and roof shift.
Continuous vertical components extend at Z=7.2. CB08 loggia ledge moves with top.
No building regeneration, object-wide scaling, palette or ground-floor edits.
"""
import bpy,json,sys,copy,ast,hashlib,shutil,math
from pathlib import Path
from mathutils import Vector
sys.dont_write_bytecode=True
OUT=Path(__file__).resolve().parent;SOURCE=OUT.parent
for folder in ('Models','Exports','Previews','Catalog','QA'):(OUT/folder).mkdir(exist_ok=True)

def reuse(path,names):
    tree=ast.parse(path.read_text(encoding='utf8'))
    nodes=[n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name in names]
    exec(compile(ast.Module(body=nodes,type_ignores=[]),str(path),'exec'),globals())

reuse(SOURCE.parents[2]/'recolor_existing.py',{'descendant','asset_objects','fingerprint','combined'})
reuse(SOURCE.parents[1]/'retrofit_signage.py',{'bounds'})
reuse(SOURCE.parent/'build_front_only.py',{'material_specs','export','render'})
reuse(SOURCE/'clear_ground_decor.py',{'components'})
CUT=7.2;STEP=3.2;EPS=.0001

def category(base,ob,part):
    ancestor=ob
    while ancestor:
        if ancestor.name in ('Signage','Entrance'):return 'fixed'
        ancestor=ancestor.parent
    lo=part['bounds']['min'][2];hi=part['bounds']['max'][2]
    if base=='CB_08' and ob.name.startswith('Terraces') and 6.8<lo<7.0 and 7.1<hi<7.2+EPS:
        return 'shift_top_loggia_ledge'
    if lo>=CUT-EPS:return 'shift_top'
    if hi<=CUT+EPS and lo>=3.9-EPS and hi>4.1:return 'repeat_second_floor'
    # The second-floor interior floor starts at 4.0 and is only 4 cm thick.
    if lo>=4-EPS and hi<=4.1+EPS:
        return 'repeat_second_floor' if ob.name.startswith('InteriorHint') else 'fixed'
    if lo<CUT-EPS and hi>CUT+EPS:
        if hi-lo<3.0:raise RuntimeError(f'Ambiguous cut crossing: {base} {ob.name} {lo}..{hi}')
        return 'extend_vertical'
    return 'fixed'

def source_rows():return json.loads((SOURCE/'manifest.json').read_text(encoding='utf8'))['items']

def inventory():
    rows=[]
    for old in source_rows():
        bpy.ops.wm.open_mainfile(filepath=str(SOURCE/old['blend']));bpy.context.view_layer.update()
        records=[]
        for ob in asset_objects(bpy.data.objects[old['id']]):
            if ob.type!='MESH':continue
            for part in components(ob):
                part['action']=category(old['id'],ob,part);records.append(part)
        counts={action:sum(r['action']==action for r in records) for action in sorted({r['action'] for r in records})}
        row={'baseId':old['id'],'counts':counts,'components':records}
        rows.append(row);print('STOREY_INVENTORY',old['id'],counts,flush=True)
    (OUT/'QA/ComponentInventory.json').write_text(json.dumps({'cutHeight':CUT,'repeatStep':STEP,'items':rows},ensure_ascii=False,indent=2),encoding='utf8')

def face_records(ob,polygon_ids=None):
    mesh=ob.data;polys=list(mesh.polygons) if polygon_ids is None else [mesh.polygons[i] for i in polygon_ids]
    return [{'vertices':list(poly.vertices),'materialIndex':poly.material_index,'smooth':poly.use_smooth,
       'uv':{layer.name:[tuple(layer.data[li].uv) for li in poly.loop_indices] for layer in mesh.uv_layers}} for poly in polys]

def extend_mesh(base,ob,extra):
    old=ob.data;records=components(ob);vertices=[v.co.copy() for v in old.vertices]
    faces=face_records(ob);delta=extra*STEP
    local_delta=ob.matrix_world.to_3x3().inverted()@Vector((0,0,delta))
    proof=[]
    for part in records:
        action=category(base,ob,part);part['action']=action
        part['addedStoreys']=extra;part['totalVerticalShift']=delta
        if action.startswith('shift_top'):
            for i in part['vertices']:vertices[i]+=local_delta
        elif action=='extend_vertical':
            moved=[]
            for i in part['vertices']:
                if (ob.matrix_world@old.vertices[i].co).z>CUT+EPS:
                    vertices[i]+=local_delta;moved.append(i)
            part['extendedVertexIndices']=moved
        elif action=='repeat_second_floor':
            part['copies']=[]
            for k in range(1,extra+1):
                offset=ob.matrix_world.to_3x3().inverted()@Vector((0,0,k*STEP))
                remap={old_index:len(vertices)+j for j,old_index in enumerate(part['vertices'])}
                vertices.extend(old.vertices[i].co+offset for i in part['vertices'])
                newfaces=face_records(ob,part['polygonIndices'])
                first=len(faces)
                for face in newfaces:face['vertices']=[remap[i] for i in face['vertices']]
                faces.extend(newfaces);part['copies'].append({'offsetZ':k*STEP,'firstPolygon':first,'polygonCount':len(newfaces)})
        proof.append(part)
    if all(p['action']=='fixed' for p in proof):return proof
    new=bpy.data.meshes.new(old.name+'_StoreyVariant')
    new.from_pydata(vertices,[],[f['vertices'] for f in faces])
    for material in old.materials:new.materials.append(material)
    for poly,face in zip(new.polygons,faces):poly.material_index=face['materialIndex'];poly.use_smooth=face['smooth']
    for layer in old.uv_layers:
        uv=new.uv_layers.new(name=layer.name)
        for poly,face in zip(new.polygons,faces):
            for li,coord in zip(poly.loop_indices,face['uv'][layer.name]):uv.data[li].uv=coord
    new.update();ob.data=new
    return proof

def ground_fingerprint(root):
    records=[]
    for ob in asset_objects(root):
        if ob.type!='MESH':continue
        mesh=ob.data
        for poly in mesh.polygons:
            world=[ob.matrix_world@mesh.vertices[i].co for i in poly.vertices]
            if max(v.z for v in world)<=4+EPS:
                records.append({'object':ob.name,'world':[list(v) for v in world],
                  'material':mesh.materials[poly.material_index].name,'color':list(mesh.materials[poly.material_index].diffuse_color),
                  'uv':{layer.name:[list(layer.data[li].uv) for li in poly.loop_indices] for layer in mesh.uv_layers}})
    return hashlib.sha256(json.dumps(records,sort_keys=True,separators=(',',':')).encode()).hexdigest()

def entrance_and_sign_fingerprint(root):
    all_hashes=fingerprint(root)
    protected=set()
    for name in ('Signage','Entrance'):
        group=bpy.data.objects[name]
        protected.update(o.name for o in asset_objects(group) if o!=group)
    return {name:all_hashes[name] for name in sorted(protected)}

def save_catalog(row):
    path=OUT/'manifest.json'
    data=json.loads(path.read_text(encoding='utf8')) if path.exists() else {'schemaVersion':1,'items':[],'accessories':[]}
    data['items']=sorted([r for r in data['items'] if r['id']!=row['id']]+[row],key=lambda r:(r['floorCount'],r['baseId']))
    data['style']='3 / 4 / 5 storey commercial buildings from approved GroundClear models'
    data['accessories']=copy.deepcopy(json.loads((SOURCE/'manifest.json').read_text(encoding='utf8'))['accessories'])
    for accessory in data['accessories']:
        accessory['sourceApprovedBlend']='../'+accessory['blend'];accessory['copiedUnchangedFrom']='../'+accessory['blend']
        for field in ('blend','fbx','glb','preview','validationReport'):
            src=SOURCE/accessory[field];dst=OUT/accessory[field];dst.parent.mkdir(parents=True,exist_ok=True)
            if not dst.exists():shutil.copyfile(src,dst)
            assert src.read_bytes()==dst.read_bytes()
    path.write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf8')
    catalog=copy.deepcopy(data)
    for item in catalog['items']+catalog['accessories']:
        for field in ('blend','fbx','glb','preview','sourceApprovedBlend','sourceDesign','componentPreservationReport','validationReport','copiedUnchangedFrom','storeyTransformationReport'):
            if field in item:item[field]='../'+item[field]
    (OUT/'Catalog/catalog.json').write_text(json.dumps(catalog,ensure_ascii=False,indent=2),encoding='utf8')

def copied_three_storeys(old):
    row=copy.deepcopy(old);ident=old['id']
    row.update(baseId=ident,floorCount=3,assetRoot=ident,variant='3F_ApprovedCopy',sourceApprovedBlend='../'+old['blend'],
       blend=f'Models/{ident}.blend',fbx=f'Exports/{ident}.fbx',glb=f'Exports/{ident}.glb',preview=f'Previews/{ident}.png',copiedApprovedBinary=True)
    hashes={}
    for field in ('blend','fbx','glb','preview'):
        src=SOURCE/old[field];dst=OUT/row[field]
        if not dst.exists():shutil.copyfile(src,dst)
        assert src.read_bytes()==dst.read_bytes();hashes[field]=hashlib.sha256(dst.read_bytes()).hexdigest()
    row['unchangedBinarySHA256']=hashes
    row['componentPreservationReport']='../'+old['componentPreservationReport']
    row['sourceDesign']='../'+old['sourceDesign'];save_catalog(row)
    print('THREE_STOREY_COPY',ident,flush=True)

def build(number,floors):
    old=next(r for r in source_rows() if r['id']==f'CB_{number:02d}')
    if floors==3:return copied_three_storeys(old)
    base=old['id'];ident=base+f'_{floors}F';extra=floors-3;delta=extra*STEP
    source=SOURCE/old['blend'];bpy.ops.wm.open_mainfile(filepath=str(source));bpy.context.view_layer.update()
    root=bpy.data.objects[base];before=ground_fingerprint(root)
    protected_before=entrance_and_sign_fingerprint(root)
    anchors={o.name:[list(r) for r in o.matrix_world] for o in asset_objects(root) if o.type=='EMPTY' and o!=root}
    original_mats=material_specs([o for o in asset_objects(root) if o.type=='MESH'])
    proof=[]
    for ob in asset_objects(root):
        if ob.type=='MESH':proof+=extend_mesh(base,ob,extra)
    root.name=ident;root['Storeys']=floors;root['AddedStoreys']=extra;root['RoofSlabHeight']=10.4+delta
    root.users_collection[0].name=ident+'_ASSET'
    bpy.context.view_layer.update()
    assert before==ground_fingerprint(root),'Ground-floor geometry changed'
    assert protected_before==entrance_and_sign_fingerprint(root),'Entrance or signage geometry changed'
    assert all([list(r) for r in bpy.data.objects[name].matrix_world]==matrix for name,matrix in anchors.items()),'Anchor changed'
    meshes=[o for o in asset_objects(root) if o.type=='MESH']
    mats=material_specs(meshes);assert original_mats==mats,'Material palette changed'
    triangles=sum(len(p.vertices)-2 for ob in meshes for p in ob.data.polygons)
    row=copy.deepcopy(old)
    for field in ('componentPreservationReport','architectureDoorsSignsUnchanged','upperDecorationPreserved'):row.pop(field,None)
    row.update(id=ident,baseId=base,assetRoot=ident,revision=9,variant=f'{floors}F',floorCount=floors,
       floorHeights=[4]+[STEP]*(floors-1),roofSlabHeight=10.4+delta,addedStoreys=extra,
       previousRevisionTriangles=old['triangles'],triangles=triangles,meshObjects=len(meshes),exportBounds=bounds(meshes),
       triangleReductionPercent=round(100*(1-triangles/old['originalDesignTriangles']),2),materials=mats,
       sourceApprovedBlend='../'+old['blend'],sourceDesign='../'+old['sourceDesign'],
       blend=f'Models/{ident}.blend',fbx=f'Exports/{ident}.fbx',glb=f'Exports/{ident}.glb',preview=f'Previews/{ident}.png',
       groundFloorUnchanged=True,footprintUnchanged=True,topFloorAndRoofShift=delta,
       repeatedStorey='Original second-floor components at Z approximately 4..7.2 m',
       storeyTransformationReport=f'QA/{ident}_Transformation.json')
    assert all(abs(row['exportBounds'][axis][i]-old['exportBounds'][axis][i])<.0001 for axis in ('min','max') for i in (0,1)),'Footprint changed'
    scene=bpy.context.scene;camera=scene.camera
    camera.location=(20,-28,20+delta/2)
    camera.rotation_euler=(Vector((0,0,5.5+delta/2))-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.ortho_scale=19.7+delta*.95
    export(root,row)
    bpy.ops.wm.open_mainfile(filepath=str(OUT/row['blend']));bpy.context.view_layer.update();root=bpy.data.objects[ident]
    assert before==ground_fingerprint(root),'Saved ground-floor geometry changed'
    assert protected_before==entrance_and_sign_fingerprint(root),'Saved entrance or signage geometry changed'
    report={'id':ident,'baseId':base,'floorCount':floors,'sourceBlendSHA256':hashlib.sha256(source.read_bytes()).hexdigest(),
       'pass':True,'groundBeforeSHA256':before,'groundAfterSavedSHA256':ground_fingerprint(root),
       'cutHeight':CUT,'step':STEP,'addedStoreys':extra,'topAndRoofShift':delta,
       'componentActions':proof,'sourceBounds':old['exportBounds'],'outputBounds':row['exportBounds'],
       'materialPaletteUnchanged':True,'groundAndSignAnchorsUnchanged':True,
       'signageAndEntranceGeometryUVUnchanged':True,'protectedGeometrySHA256':protected_before,
       'specialCase':'CB08 projecting loggia ledge at 6.89..7.19 m moves with top floor, never cloned' if base=='CB_08' else None}
    (OUT/row['storeyTransformationReport']).write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
    render(OUT/row['preview']);save_catalog(row)
    print('STOREY_COMPLETE',ident,'triangles',triangles,'height',row['exportBounds']['max'][2],flush=True)

args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
if args==['inspect']:inventory()
elif args:
    for arg in args:
        number,floors=(int(v) for v in arg.split(':'));build(number,floors)
else:
    for floors in (3,4,5):
        for number in range(1,9):build(number,floors)
