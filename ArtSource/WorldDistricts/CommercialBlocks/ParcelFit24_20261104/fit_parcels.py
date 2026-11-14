"""Bake actual Small parcel normalization into approved 24 building assets.

p_new = scale * (p_source - source_bounds_bottom_center).
scale = 12 / longest total-renderer horizontal bound. Root stays identity.
16 m parcel guides are Presentation_ONLY and never exported with the building.
"""
import bpy,json,sys,copy,ast,hashlib,shutil,math,os
from pathlib import Path
from mathutils import Vector,Matrix
sys.dont_write_bytecode=True
OUT=Path(__file__).resolve().parent
LOWPOLY=OUT.parent/'LowPoly_20261104'
SOURCE=LOWPOLY/'KoreanPalette/SignageReady/FrontOnly/GroundClear/StoreyVariants'
for folder in ('Models','Exports','Previews','Catalog','QA'):(OUT/folder).mkdir(exist_ok=True)

def reuse(path,names):
    tree=ast.parse(path.read_text(encoding='utf8'))
    nodes=[n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name in names]
    exec(compile(ast.Module(body=nodes,type_ignores=[]),str(path),'exec'),globals())

reuse(LOWPOLY/'KoreanPalette/recolor_existing.py',{'descendant','asset_objects'})
reuse(LOWPOLY/'KoreanPalette/SignageReady/retrofit_signage.py',{'bounds','box'})
reuse(LOWPOLY/'KoreanPalette/SignageReady/FrontOnly/build_front_only.py',{'material_specs','export','render'})

def relative(path):return Path(os.path.relpath(path.resolve(),OUT)).as_posix()

def mesh_signature(ob):
    mesh=ob.data
    record={'edges':[list(e.vertices) for e in mesh.edges],
      'polygons':[[list(p.vertices),p.material_index,p.use_smooth] for p in mesh.polygons],
      'uv':{layer.name:[list(v.uv) for v in layer.data] for layer in mesh.uv_layers},
      'materials':[{'name':m.name,'color':list(m.diffuse_color),'baseColor':list(m.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value)} for m in mesh.materials]}
    return hashlib.sha256(json.dumps(record,sort_keys=True,separators=(',',':')).encode()).hexdigest()

def snapshot(root):
    return {ob.name:{'type':ob.type,'world':ob.matrix_world.copy(),
      'vertices':[ob.matrix_world@v.co for v in ob.data.vertices] if ob.type=='MESH' else None,
      'signature':mesh_signature(ob) if ob.type=='MESH' else None} for ob in asset_objects(root)}

def verify(root,source_snapshot,scale,pivot):
    checks=[]
    assert max(abs(root.matrix_world[i][j]-(1 if i==j else 0)) for i in range(4) for j in range(4))<1e-7
    for ob in asset_objects(root):
        old=source_snapshot[ob.name];check={'name':ob.name,'type':ob.type}
        if ob.type=='MESH':
            assert len(ob.data.vertices)==len(old['vertices'])
            error=max((ob.matrix_world@v.co-scale*(p-pivot)).length for v,p in zip(ob.data.vertices,old['vertices']))
            check.update(affineMaxErrorMetres=error,topologyUVMaterialsUnchanged=mesh_signature(ob)==old['signature'])
            assert error<.00003 and check['topologyUVMaterialsUnchanged'],str(check)
        if ob!=root:
            expected=scale*(old['world'].translation-pivot)
            error=(ob.matrix_world.translation-expected).length
            check['anchorOrObjectPositionError']=error
            assert error<.00003
        check['pass']=True;checks.append(check)
    return checks

def guide_material(name,color):
    m=bpy.data.materials.new(name);m.use_nodes=True;m.diffuse_color=(*color,1)
    p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(*color,1);p.inputs['Roughness'].default_value=1
    return m

def presentation(row):
    stage=bpy.data.collections['Presentation_ONLY']
    pad=guide_material('Presentation_ParcelPad',(.46,.49,.46))
    line=guide_material('Presentation_ParcelLine',(.11,.23,.18))
    lettering=guide_material('Presentation_DimensionText',(.045,.10,.075))
    box('ParcelGuide_16m_Pad',(0,0,-.025),(16,16,.04),pad,None,stage)
    for y in (-8,8):box('ParcelGuide_16m_Edge',(0,y,.004),(16.06,.055,.012),line,None,stage)
    for x in (-8,8):box('ParcelGuide_16m_Edge',(x,0,.004),(.055,16.06,.012),line,None,stage)
    def label(name,text,loc,size):
        data=bpy.data.curves.new(name,'FONT');data.body=text;data.align_x='CENTER';data.align_y='CENTER';data.size=size
        data.materials.append(lettering);ob=bpy.data.objects.new(name,data);stage.objects.link(ob);ob.location=loc
    label('ParcelGuide_WidthLabel','16 m PARCEL',(0,-8.6,.015),.48)
    front=row['setbacks']['front']
    label('ParcelGuide_FrontSpace',f'FRONT  {front:.2f} m',(0,-8+front*.50,.015),.36)
    # Orthographic framing uses the building and all plot corners, avoiding crop.
    camera=bpy.context.scene.camera;height=row['exportBounds']['max'][2]
    target=Vector((0,0,height*.45));camera.location=target+Vector((24,-32,24))
    camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
    bpy.context.view_layer.update()
    limits=row['exportBounds'];points=[Vector((x,y,z)) for x in (limits['min'][0],limits['max'][0]) for y in (limits['min'][1],limits['max'][1]) for z in (0,height)]
    points += [Vector((x,y,0)) for x in (-8.5,8.5) for y in (-9.1,8.5)]
    inverse=camera.matrix_world.inverted();screen=[inverse@p for p in points]
    cx=(min(p.x for p in screen)+max(p.x for p in screen))/2;cy=(min(p.y for p in screen)+max(p.y for p in screen))/2
    camera.location += camera.matrix_world.to_3x3()@Vector((cx,cy,0))
    camera.data.ortho_scale=max(max(p.x for p in screen)-min(p.x for p in screen),max(p.y for p in screen)-min(p.y for p in screen))*1.10
    scene=bpy.context.scene;scene.render.resolution_x=1100;scene.render.resolution_y=1100;scene.cycles.samples=32

def save_catalog(row):
    path=OUT/'manifest.json'
    data=json.loads(path.read_text(encoding='utf8')) if path.exists() else {'schemaVersion':1,'items':[],'accessories':[]}
    data['items']=sorted([r for r in data['items'] if r['id']!=row['id']]+[row],key=lambda r:(r['floorCount'],r['baseId']))
    data['style']='24 commercial buildings normalized to Small 16 m parcels / 12 m maximum renderer extent'
    data['accessories']=copy.deepcopy(json.loads((SOURCE/'manifest.json').read_text(encoding='utf8'))['accessories'])
    for accessory in data['accessories']:
        accessory['sourceApprovedBlend']=relative(SOURCE/accessory['blend']);accessory['copiedUnchangedFrom']=accessory['sourceApprovedBlend']
        hashes={}
        for field in ('blend','fbx','glb','preview','validationReport'):
            src=SOURCE/accessory[field];dst=OUT/accessory[field];dst.parent.mkdir(parents=True,exist_ok=True)
            if not dst.exists():shutil.copyfile(src,dst)
            assert src.read_bytes()==dst.read_bytes();hashes[field]=hashlib.sha256(dst.read_bytes()).hexdigest()
        accessory['unchangedBinarySHA256']=hashes
    path.write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf8')
    catalog=copy.deepcopy(data)
    for item in catalog['items']+catalog['accessories']:
        for field in ('blend','fbx','glb','preview','sourceApprovedBlend','sourceDesign','parcelFitReport','validationReport','copiedUnchangedFrom'):
            if field in item:item[field]='../'+item[field]
    (OUT/'Catalog/catalog.json').write_text(json.dumps(catalog,ensure_ascii=False,indent=2),encoding='utf8')

def fit(ident):
    old=next(r for r in json.loads((SOURCE/'manifest.json').read_text(encoding='utf8'))['items'] if r['id']==ident)
    source=SOURCE/old['blend'];bpy.ops.wm.open_mainfile(filepath=str(source));bpy.context.view_layer.update()
    root=bpy.data.objects[ident];objects=asset_objects(root);meshes=[o for o in objects if o.type=='MESH']
    source_bounds=bounds(meshes)
    pivot=Vector(((source_bounds['min'][0]+source_bounds['max'][0])/2,(source_bounds['min'][1]+source_bounds['max'][1])/2,source_bounds['min'][2]))
    scale=12/max(source_bounds['max'][axis]-source_bounds['min'][axis] for axis in (0,1))
    before=snapshot(root);processed=set()
    for ob in meshes:
        if ob.data.as_pointer() in processed:continue
        processed.add(ob.data.as_pointer())
        for v in ob.data.vertices:v.co*=scale
        ob.data.update()
    def depth(ob):
        n=0
        while ob.parent:n+=1;ob=ob.parent
        return n
    for ob in sorted(objects,key=depth):
        if ob==root:ob.matrix_world=Matrix.Identity(4)
        else:
            desired=before[ob.name]['world'].copy();desired.translation=scale*(desired.translation-pivot);ob.matrix_world=desired
        for key in ('WidthMetres','HeightMetres','EntryClearWidthMetres'):
            if key in ob:ob[key]=float(ob[key])*scale
    base_height=-float(pivot.z)*scale
    root['DimensionsMetres']=f"{old['width']*scale:.5f} x {old['depth']*scale:.5f}"
    root['ParcelTier']='Small';root['ParcelSizeMetres']=16.;root['MaxHorizontalExtentMetres']=12.
    root['BakedUniformScale']=scale;root['RoofSlabHeight']=old['roofSlabHeight']*scale+base_height
    root['Variant']='ParcelFit24_20261104'
    bpy.context.view_layer.update();checks=verify(root,before,scale,pivot)
    limits=bounds(meshes)
    for axis in (0,1):assert abs(limits['min'][axis]+limits['max'][axis])<.00003
    assert abs(limits['min'][2])<.00003
    assert abs(max(limits['max'][a]-limits['min'][a] for a in (0,1))-12)<.00003
    row={key:copy.deepcopy(old[key]) for key in ('id','baseId','floorCount','styleLabel','paletteLabel','opaqueGlazing','externalTextures','frontSignCount','attachedBlade','attachedSideSigns','originalDesignTriangles','sourceDesignSHA256') if key in old}
    row.update(revision=10,variant='ParcelFitSmall16m',assetRoot=ident,
       width=old['width']*scale,depth=old['depth']*scale,entryClearWidth=old['entryClearWidth']*scale,
       floorHeights=[h*scale for h in old['floorHeights']],floorBaseHeight=base_height,
       roofSlabHeight=old['roofSlabHeight']*scale+base_height,triangles=old['triangles'],meshObjects=len(meshes),
       exportBounds=limits,materials=material_specs(meshes),fitScale=scale,
       sourceExportBounds=source_bounds,sourcePivotBottomCenter=list(pivot),
       sourceFloorHeights=old['floorHeights'],sourceRoofSlabHeight=old['roofSlabHeight'],
       parcelTier='Small',parcelSize=[16,16],maxBuildingSide=12,rootTransform='Identity; bounds bottom-center pivot',
       setbacks={'left':8+limits['min'][0],'right':8-limits['max'][0],'front':8+limits['min'][1],'back':8-limits['max'][1]},
       groundDecorationPresent=False,bakedUniformXYZ=True,
       sourceApprovedBlend=relative(source),sourceDesign=relative(SOURCE/old['sourceDesign']),
       blend=f'Models/{ident}.blend',fbx=f'Exports/{ident}.fbx',glb=f'Exports/{ident}.glb',preview=f'Previews/{ident}.png',
       parcelFitReport=f'QA/{ident}_ParcelFit.json')
    row['signs']=[]
    for spec in old['signs']:
        new=copy.deepcopy(spec);anchor=bpy.data.objects[spec['name']]
        board=bpy.data.objects[spec['board']]
        for key in ('width','height','depth'):new[key]=spec[key]*scale
        new['position']=list(anchor.matrix_world.translation);new['boardBounds']=bounds([board]);new['mountBounds']=bounds(asset_objects(anchor))
        row['signs'].append(new)
    row['doorAndEntryAnchors']={name:list(bpy.data.objects[name].matrix_world.translation) for name in ('Door_Leaf_01','Door_Leaf_02','EntryAnchor','InteriorSpawnAnchor')}
    row['doorLeafBounds']={name:bounds(asset_objects(bpy.data.objects[name])) for name in ('Door_Leaf_01','Door_Leaf_02')}
    assert row['setbacks']['front']>=3.7
    # Export selection is root descendants only; guides remain outside that hierarchy.
    presentation(row);export(root,row)
    bpy.ops.wm.open_mainfile(filepath=str(OUT/row['blend']));bpy.context.view_layer.update();root=bpy.data.objects[ident]
    checks=verify(root,before,scale,pivot)
    report={'id':ident,'pass':all(c['pass'] for c in checks),'sourceBlendSHA256':hashlib.sha256(source.read_bytes()).hexdigest(),
       'operation':'p_new = scale * (p_source - bounds_bottom_center); root identity; scale baked into mesh vertices and object translations',
       'scale':scale,'sourceBounds':source_bounds,'sourcePivotBottomCenter':list(pivot),'resultBounds':limits,
       'objectChecks':checks,'plotGuidesPresentationOnly':True,'setbacks':row['setbacks']}
    (OUT/row['parcelFitReport']).write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
    render(OUT/row['preview']);save_catalog(row)
    print('PARCEL_FIT_COMPLETE',ident,'scale',scale,'front',row['setbacks']['front'],flush=True)

args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
for ident in (args or [r['id'] for r in json.loads((SOURCE/'manifest.json').read_text(encoding='utf8'))['items']]):fit(ident)
